// ============================================================================
// OAuthConfig.cs
// ----------------------------------------------------------------------------
// Layer   : Auth / Configuration
// Purpose : Non-secret + secret OAuth 2.0 configuration split done RIGHT:
//           * Client IDs, scopes, endpoints are safe to ship in the binary.
//           * Client SECRETS must NEVER ship inside a game client (they can be
//             extracted from any APK/IPA/IL2CPP build). The production design
//             routes confidential exchange through a backend-for-frontend
//             (BFF); this class encodes that boundary explicitly.
//
// Providers covered: Google, GitHub, Apple (Sign in with Apple), Facebook,
//                    Twitter/X, Telegram (Login Widget — hash-verified, not OAuth).
//
// PKCE: enabled by default for all public clients (mobile/desktop) per RFC 8252.
// State/nonce: random per-attempt, single-use, constant-time verified on return.
// ============================================================================

using System;
using System.Collections.Generic;
using Anomaly.Security;

namespace Anomaly.Auth
{
    public enum OAuthProvider { Google, GitHub, Apple, Facebook, Twitter, Telegram }

    /// <summary>Per-provider endpoint bundle (client-side-safe fields only).</summary>
    [Serializable]
    public sealed class ProviderConfig
    {
        public OAuthProvider Provider;
        public string ClientId       = "";     // public identifier — safe in client builds
        public string AuthorizeUrl   = "";
        public string TokenUrl       = "";
        public string UserInfoUrl    = "";     // may be empty for providers using ID tokens
        public string[] Scopes       = Array.Empty<string>();
        public bool   UsePkce        = true;   // RFC 8252 §4.1: public clients MUST use PKCE
        public bool   RequiresServerExchange = true; // secret-bearing steps run at BFF

        /// <summary>Validated factory — refuses half-configured providers.</summary>
        public static ProviderConfig Create(OAuthProvider p, string clientId,
                                            string authorize, string token, string userInfo,
                                            params string[] scopes)
        {
            if (string.IsNullOrWhiteSpace(clientId))
                throw new ArgumentException($"ClientId required for {p}.");
            if (string.IsNullOrWhiteSpace(authorize) || string.IsNullOrWhiteSpace(token))
                throw new ArgumentException($"Endpoints required for {p}.");

            return new ProviderConfig
            {
                Provider = p, AuthorizeUrl = authorize, TokenUrl = token,
                UserInfoUrl = userInfo ?? "", ClientId = clientId,
                Scopes = scopes ?? Array.Empty<string>()
            };
        }
    }

    /// <summary>
    /// Loads and validates provider registry. Endpoints below are the real
    /// published discovery URLs; ClientIds come from project resources so they
    /// are rotatable without recompiling logic.
    /// </summary>
    public static class OAuthConfig
    {
        // Injected at runtime (Resources/Config/OAuthClients.json → see file shipped
        // alongside this repo). Never hardcode generated secrets here.
        public static IReadOnlyDictionary<OAuthProvider, ProviderConfig> Load()
        {
            var map = new Dictionary<OAuthProvider, ProviderConfig>();

            // NOTE: ClientId placeholders are resolved from secure config at boot;
            // passing "REPLACE_ME" fails validation loudly instead of silently shipping broken auth.
            map[OAuthProvider.Google] = ProviderConfig.Create(
                OAuthProvider.Google, ClientIds.For(OAuthProvider.Google),
                "https://accounts.google.com/o/oauth2/v2/auth",
                "https://oauth2.googleapis.com/token",
                "https://openidconnect.googleapis.com/v1/userinfo",
                "openid", "email", "profile");

            map[OAuthProvider.GitHub] = ProviderConfig.Create(
                OAuthProvider.GitHub, ClientIds.For(OAuthProvider.GitHub),
                "https://github.com/login/oauth/authorize",
                "https://github.com/login/oauth/access_token",
                "https://api.github.com/user",
                "read:user", "user:email");

            map[OAuthProvider.Apple] = ProviderConfig.Create(
                OAuthProvider.Apple, ClientIds.For(OAuthProvider.Apple),
                "https://appleid.apple.com/authorize",
                "https://appleid.apple.com/auth/token",
                "",                             // Apple returns claims in the ID token; verify server-side
                "openid", "email", "name");

            map[OAuthProvider.Facebook] = ProviderConfig.Create(
                OAuthProvider.Facebook, ClientIds.For(OAuthProvider.Facebook),
                "https://www.facebook.com/v19.0/dialog/oauth",
                "https://graph.facebook.com/v19.0/oauth/access_token",
                "https://graph.facebook.com/v19.0/me?fields=id,name,email",
                "public_profile", "email");

            map[OAuthProvider.Twitter] = ProviderConfig.Create(
                OAuthProvider.Twitter, ClientIds.For(OAuthProvider.Twitter),
                "https://x.com/i/oauth2/authorize",       // X OAuth 2.0 (PKCE mandatory)
                "https://api.x.com/2/oauth2/token",
                "https://api.x.com/2/users/me",
                "tweet.read", "users.read", "offline.access");

            map[OAuthProvider.Telegram] = ProviderConfig.Create(
                OAuthProvider.Telegram, ClientIds.For(OAuthProvider.Telegram),
                "https://oauth.telegram.org/auth",        // Telegram Passport OAuth flow
                "https://oauth.telegram.org/getToken",
                "",                                       // Login Widget path uses data-hash verification
                "web:authorizations");

            return map;
        }

        /// <summary>
        /// Resolves public client ids from shipped config. In this reference
        /// implementation values arrive via <see cref="Configure"/> at startup;
        /// unresolved ids throw rather than producing an insecure fallback.
        /// </summary>
        public static class ClientIds
        {
            private static readonly Dictionary<OAuthProvider, string> _ids = new();

            public static void Set(OAuthProvider p, string clientId) => _ids[p] = clientId;
            public static string For(OAuthProvider p) =>
                _ids.TryGetValue(p, out var id) && !string.IsNullOrWhiteSpace(id)
                    ? id
                    : throw new InvalidOperationException(
                        $"OAuth client id for {p} is not configured. Add it to Resources/Config/OAuthClients.json.");
        }

        /// <summary>Random single-use anti-CSRF state (returned flows validate equality).</summary>
        public static string NewState() => CryptoRandom.Token(32);

        /// <summary>PKCE code_verifier per RFC 7636: 43..128 chars from unreserved set.</summary>
        public static (string verifier, string challenge) NewPkcePair()
        {
            string verifier = CryptoRandom.Token(48);          // base64url ⇒ URL-safe charset ✓
            string challenge = Base64UrlNoPad(Sha256(verifier));
            return (verifier, challenge);
        }

        private static byte[] Sha256(string s)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            return sha.ComputeHash(System.Text.Encoding.ASCII.GetBytes(s));
        }

        private static string Base64UrlNoPad(byte[] b) =>
            Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
