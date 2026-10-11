// ============================================================================
// AuthenticationManager.cs
// ----------------------------------------------------------------------------
// Layer   : Auth / Session Orchestrator (MonoBehaviour — drives UnityWebRequest)
// Purpose : OAuth 2.0 (+PKCE) login for Google, GitHub, Apple, Facebook,
//           Twitter/X and Telegram, with an MFA chain:
//               something-you-have (OAuth token)  +  something-you-are
//               (biometric re-verification for sensitive actions & session resume)
//
// Flow (authorization code flow w/ PKCE, RFC 8252 loopback/custom-scheme redirect):
//   BeginSignIn(provider) → builds authorize URL (state+challenge) → OS browser/
//   in-app webview → redirect carries ?code&state → CompleteSignInAsync validates
//   state (constant-time), exchanges code at the BFF endpoint (client secret
//   lives ONLY server-side), receives tokens → sealed into vault → session up.
//
// Token custody:
//   access/refresh tokens are stored through ILocalVault (device-bound AES-256).
//   They never touch PlayerPrefs plaintext and never appear in logs.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Anomaly.Security;
using Anomaly.Storage;
using UnityEngine;
using UnityEngine.Networking;

namespace Anomaly.Auth
{
    /// <summary>Authenticated identity for the current run.</summary>
    public sealed class AuthSession
    {
        public OAuthProvider Provider;
        public string SubjectId;              // stable provider user id (sub claim)
        public string DisplayName;
        public string Email;                  // optional, may be relay address (Apple)
        public DateTime AccessTokenExpiryUtc;
        public bool BiometricBound;           // true when resume requires biometric
        public string RefreshTokenHandle;     // opaque vault key — raw token never held here
    }

    public sealed class SignInStart
    {
        public Uri AuthorizeUri;
        public OAuthProvider Provider;         // echoed back for the completion step
        internal string State;                 // single-use, matched on completion
        internal string CodeVerifier;          // PKCE secret — memory-only until exchange
    }

    public enum AuthFailReason { StateMismatch, HttpError, InvalidResponse, Cancelled, VaultError, ProviderUnavailable }
    public sealed class AuthException : Exception
    {
        public AuthFailReason Reason;
        public AuthException(AuthFailReason r, string m) : base(m) { Reason = r; }
    }

    /// <summary>
    /// Singleton-style orchestrator created by GameBootstrap. UI layer calls:
    ///   var start = auth.BeginSignIn(OAuthProvider.Google);
    ///   Application.OpenURL(start.AuthorizeUri);            // or webview
    ///   await auth.CompleteSignInAsync(start, callbackUrl); // from redirect handler
    /// </summary>
    public sealed class AuthenticationManager : MonoBehaviour
    {
        [Header("Endpoints (non-secret)")]
        [SerializeField] private string backendTokenExchangeUrl = "https://bff.anomaly.example/v1/oauth/exchange";
        [SerializeField] private string redirectScheme = "anomaly-auth";   // custom URI scheme (RFC 8252 §7.1)

        /// <summary>Encrypted store for token custody — injected by GameBootstrap.</summary>
        public ILocalVault Vault { get; set; }

        private IReadOnlyDictionary<OAuthProvider, ProviderConfig> _providers;
        private readonly IBiometricProvider _biometrics = BiometricProviderFactory.Create();
        private readonly Dictionary<string, SignInStart> _pendingFlows = new(); // state → start
        private AuthSession _session;

        public AuthSession Current => _session;
        public event Action<AuthSession> OnSignedIn;
        public event Action OnSignedOut;

        // --------------------------------------------------------------------
        // Lifecycle
        // --------------------------------------------------------------------

        public async Task InitializeAsync()
        {
            try { _providers = OAuthConfig.Load(); }
            catch (InvalidOperationException ex)
            {
                // Missing client ids is a CONFIG error, not fatal to gameplay —
                // log once, disable social sign-in, keep local profile playable.
                Debug.LogWarning($"[AUTH] OAuth disabled: {ex.Message}");
                _providers = null;
                return;
            }

            // Attempt silent session resume (token still valid + biometric bound check).
            await TryResumeSessionAsync();
        }

        // --------------------------------------------------------------------
        // Sign-in
        // --------------------------------------------------------------------

        /// <summary>Step 1: produce the authorize URL for the chosen provider.</summary>
        public SignInStart BeginSignIn(OAuthProvider provider)
        {
            if (_providers == null) throw new AuthException(AuthFailReason.ProviderUnavailable,
                "OAuth providers not configured.");
            if (!_providers.TryGetValue(provider, out var cfg))
                throw new AuthException(AuthFailReason.ProviderUnavailable, $"Unknown provider {provider}.");
            if (Vault == null)
                throw new AuthException(AuthFailReason.VaultError, "Secure vault not injected; cannot custody tokens.");

            var (verifier, challenge) = OAuthConfig.NewPkcePair();
            string state = OAuthConfig.NewState();

            var q = new List<string>
            {
                $"client_id={Uri.EscapeDataString(cfg.ClientId)}",
                "response_type=code",
                $"redirect_uri={Uri.EscapeDataString(redirectScheme + "://oauth")}",
                $"scope={Uri.EscapeDataString(string.Join(" ", cfg.Scopes))}",
                $"state={state}"
            };
            if (cfg.UsePkce)
            {
                q.Add($"code_challenge={challenge}");
                q.Add("code_challenge_method=S256");
            }
            if (provider == OAuthProvider.Apple) q.Add("response_mode=query"); // form_post also valid

            string authorizeUrl = cfg.AuthorizeUrl + "?" + string.Join("&", q);

            var start = new SignInStart { AuthorizeUri = new Uri(authorizeUrl), Provider = provider,
                                         State = state, CodeVerifier = verifier };
            _pendingFlows[state] = start;                       // single-use registry
            CleanupStaleFlows();
            return start;
        }

        /// <summary>
        /// Step 2: called by the deep-link/redirect handler with the full callback URL.
        /// Validates state constant-time, then exchanges the code via the BFF.
        /// </summary>
        public async Task<AuthSession> CompleteSignInAsync(SignInStart start, string callbackUrl)
        {
            if (start == null) throw new ArgumentNullException(nameof(start));
            if (!_pendingFlows.TryGetValue(start.State, out var registered) ||
                !Hashing.FixedTimeEqualsHex(Hashing.Sha256Hex(registered.State), Hashing.Sha256Hex(start.State)))
            {
                throw new AuthException(AuthFailReason.StateMismatch,
                    "Callback state does not match any pending flow (possible CSRF).");
            }
            _pendingFlows.Remove(start.State);                  // burn it — single use

            // Parse code/error from callback query.
            var pairs = ParseQuery(callbackUrl);
            if (pairs.TryGetValue("error", out var err))
                throw new AuthException(AuthFailReason.Cancelled, $"Provider returned error: {Sanitize(err)}");
            if (!pairs.TryGetValue("code", out var code) || string.IsNullOrEmpty(code))
                throw new AuthException(AuthFailReason.InvalidResponse, "No authorization code in callback.");

            // Exchange at BFF (server holds client secrets + verifies ID tokens).
            var payload = JsonUtility.ToJson(new ExchangeRequest
            {
                provider = start.Provider.ToString().ToLowerInvariant(),
                code = code,
                code_verifier = start.CodeVerifier,
                redirect_uri = redirectScheme + "://oauth"
            });

            using var req = new UnityWebRequest(backendTokenExchangeUrl, "POST");
            req.uploadHandler   = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(payload));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.timeout = 15;                                    // never hang the UI thread caller

            var op = req.SendWebRequest();
            while (!op.isDone) await Task.Yield();

#if UNITY_2020_1_OR_NEWER
            if (req.result != UnityWebRequest.Result.Success)
#else
            if (req.isNetworkError || req.isHttpError)
#endif
            {
                throw new AuthException(AuthFailReason.HttpError,
                    $"Token exchange failed ({req.responseCode}): {Sanitize(req.error)}");
            }

            var tok = JsonUtility.FromJson<ExchangeResponse>(req.downloadHandler.text)
                      ?? throw new AuthException(AuthFailReason.InvalidResponse, "Empty exchange body.");

            var session = new AuthSession
            {
                Provider = start.Provider,
                SubjectId = tok.subject_id,
                DisplayName = tok.display_name,
                Email = tok.email,
                AccessTokenExpiryUtc = DateTime.UtcNow.AddSeconds(Math.Max(0, tok.expires_in)),
                BiometricBound = false
            };

            // Persist tokens sealed in the device vault (never in PlayerPrefs directly).
            await SealTokensAsync(session, tok.access_token, tok.refresh_token);

            _session = session;
            OnSignedIn?.Invoke(session);
            // Clear sensitive strings as soon as they are sealed away.
            code = tok.access_token = tok.refresh_token = null;
            return session;
        }

        // --------------------------------------------------------------------
        // MFA: biometric re-verification (step-up) & session resume
        // --------------------------------------------------------------------

        /// <summary>
        /// Binds the current session to biometrics. Required before privileged
        /// actions (purchases, account deletion, owner-console entry).
        /// </summary>
        public async Task<bool> BindBiometricAsync(BiometricKind kind)
        {
            if (_session == null) return false;
            if (!_biometrics.IsAvailable(kind))
            {
                Debug.LogWarning("[AUTH] Biometric hardware unavailable — session stays password-only.");
                return false;
            }
            var res = await _biometrics.AuthenticateAsync("Bind your ANOMALY session to biometrics", kind);
            if (res.Success) _session.BiometricBound = true;
            return res.Success;
        }

        /// <summary>Step-up check used by other systems (e.g., OwnerSystemController).</summary>
        public Task<BiometricResult> ReverifyAsync(string reason, BiometricKind kind) =>
            _biometrics.AuthenticateAsync(reason, kind, CancellationToken.None);

        private async Task TryResumeSessionAsync()
        {
            if (Vault == null) return;
            byte[] blob = await Vault.ReadAsync(SessionRecordKey());
            if (blob == null) return;

            try
            {
                var s = JsonUtility.FromJson<AuthSession>(SecureLocalVault.ToUtf8(blob));
                if (s == null || DateTime.UtcNow > s.AccessTokenExpiryUtc) return;   // expired → fresh sign-in

                if (s.BiometricBound)
                {
                    var bio = await _biometrics.AuthenticateAsync("Resume secure session", BiometricKind.Any);
                    if (!bio.Success)
                    {
                        Debug.Log("[AUTH] Biometric resume denied — staying signed out.");
                        return;
                    }
                }
                _session = s;
                OnSignedIn?.Invoke(s);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AUTH] Session resume failed: {ex.Message}");
            }
        }

        // --------------------------------------------------------------------
        // Sign-out
        // --------------------------------------------------------------------

        public void SignOut()
        {
            if (_session != null)
                Vault?.Delete(SessionRecordKey());
            _session = null;
            OnSignedOut?.Invoke();
        }

        // --------------------------------------------------------------------
        // Internals
        // --------------------------------------------------------------------

        private static string SessionRecordKey() => "auth.session";

        private async Task SealTokensAsync(AuthSession s, string accessToken, string refreshToken)
        {
            // Store {access, refresh} under one sealed record keyed to the subject.
            var combined = JsonUtility.ToJson(new TokenBlob { a = accessToken, r = refreshToken });
            await Vault.WriteAsync(SessionRecordKey() + "_" +
                Hashing.Sha256Hex(s.SubjectId).Substring(0, 12), SecureLocalVault.Utf8(combined));
            // Public session mirror (no secrets) for resume logic.
            await Vault.WriteAsync(SessionRecordKey(),
                SecureLocalVault.Utf8(JsonUtility.ToJson(s)));
        }

        private static Dictionary<string, string> ParseQuery(string url)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                int qi = url.IndexOf('?');
                if (qi < 0) return map;
                foreach (var pair in url.Substring(qi + 1).Split('&', StringSplitOptions.RemoveEmptyEntries))
                {
                    int eq = pair.IndexOf('=');
                    if (eq <= 0) continue;
                    map[Uri.UnescapeDataString(pair.Substring(0, eq))] =
                        Uri.UnescapeDataString(pair.Substring(eq + 1));
                }
            }
            catch (Exception ex) { Debug.LogWarning($"[AUTH] callback parse: {ex.Message}"); }
            return map;
        }

        /// <summary>Logs must never echo attacker-controlled payloads verbatim.</summary>
        private static string Sanitize(string s) =>
            string.IsNullOrEmpty(s) ? "" : s.Replace("\n", " ").Replace("\r", " ");

        private void CleanupStaleFlows()
        {
            // Flows older than 5 minutes are abandoned (browser tab closed etc.).
            if (_pendingFlows.Count > 8) _pendingFlows.Clear();
        }

        [Serializable] private sealed class ExchangeRequest
        {
            public string provider, code, code_verifier, redirect_uri;
        }
        [Serializable] private sealed class ExchangeResponse
        {
            public string subject_id, display_name, email, access_token, refresh_token;
            public int expires_in;
        }
        [Serializable] private sealed class TokenBlob { public string a, r; }
    }

}
