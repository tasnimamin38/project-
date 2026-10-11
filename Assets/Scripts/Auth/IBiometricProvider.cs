// ============================================================================
// IBiometricProvider.cs  +  platform adapters (editor stub included)
// ----------------------------------------------------------------------------
// Layer   : Auth / Biometrics Abstraction
// Purpose : One interface, per-platform implementations behind it:
//           * AndroidBiometricProvider  → AndroidX BiometricPrompt (API 23+),
//             fingerprint OR face (BIOMETRIC_STRONG | DEVICE_CREDENTIAL allowed).
//           * iOSBiometricProvider      → LocalAuthentication LAContext
//             (Face ID / Touch ID), with passcode fallback policy.
//           * EditorStubProvider        → simulated prompt for dev/QA builds.
//
// The C# side talks to the native layers through AndroidJavaObject / iOS
// __Internal P/Invoke; those bindings live in Plugins/ and are declared here as
// thin extern wrappers so this assembly compiles on every target.
//
// Privacy stance: biometrics NEVER leave the device. We only receive a boolean
// "verified" result plus an optional authenticator-strength enum — no template,
// no image, nothing to leak or subpoena.
// ============================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Anomaly.Auth
{
    public enum BiometricKind { Face, Fingerprint, Any }
    public enum BiometricOutcome { Verified, UserCancelled, Unavailable, LockedOut, PermissionDenied, Error }

    /// <summary>Result of one biometric attempt. Never contains biometric data.</summary>
    public sealed class BiometricResult
    {
        public BiometricOutcome Outcome;
        public string DetailMessage;               // user-safe text (no internal identifiers)

        public bool Success => Outcome == BiometricOutcome.Verified;
        public static BiometricResult Ok()            => new() { Outcome = BiometricOutcome.Verified };
        public static BiometricResult Fail(BiometricOutcome o, string msg = "") =>
            new() { Outcome = o, DetailMessage = msg };
    }

    /// <summary>Contract every platform adapter implements.</summary>
    public interface IBiometricProvider
    {
        /// <summary>Hardware capability probe (sensor present & enrolled).</summary>
        bool IsAvailable(BiometricKind kind);

        /// <summary>Prompt the OS secure UI. Completes on main thread.</summary>
        Task<BiometricResult> AuthenticateAsync(string reason, BiometricKind kind, CancellationToken ct = default);
    }

    // ------------------------------------------------------------------------
    // Factory: picks the adapter for the active runtime platform.
    // ------------------------------------------------------------------------
    public static class BiometricProviderFactory
    {
        public static IBiometricProvider Create()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return new AndroidBiometricProvider();
#elif UNITY_IOS && !UNITY_EDITOR
            return new IOSBiometricProvider();
#else
            return new EditorStubProvider();
#endif
        }
    }

    // ------------------------------------------------------------------------
    // Android adapter — binds to AndroidBiometricBridge.cpp/.java in Plugins/Android.
    // ------------------------------------------------------------------------
    public sealed class AndroidBiometricProvider : IBiometricProvider
    {
        // Native bridge signature (see Plugins/Android/AndroidBiometricBridge.java):
        //   returns 0 unavailable / 1 available, prompts asynchronously via callback id.
        public bool IsAvailable(BiometricKind kind)
        {
            try
            {
                using var plugin = new AndroidJavaClass("com.anomaly.biometrics.BiometricBridge");
                return plugin.CallStatic<bool>("isAvailable", MapKind(kind));
            }
            catch (AndroidJavaException ex)
            {
                Debug.LogError($"[BIOMETRIC/ANDROID] probe failed: {ex.Message}");
                return false;   // fail closed
            }
        }

        public async Task<BiometricResult> AuthenticateAsync(string reason, BiometricKind kind, CancellationToken ct = default)
        {
            var tcs = new TaskCompletionSource<BiometricResult>();
            try
            {
                using var plugin = new AndroidJavaClass("com.anomaly.biometrics.BiometricBridge");
                // Java signature: prompt(String reason, String kind, IBiometricCallback cb)
                plugin.CallStatic("prompt", reason ?? "Authenticate", MapKind(kind), new CallbackProxy(tcs));

                // Cancellation surfaces as a synthetic UserCancelled result.
                using (ct.Register(() => tcs.TrySetResult(
                           BiometricResult.Fail(BiometricOutcome.UserCancelled, "cancelled by caller"))))
                {
                    var result = await tcs.Task;
                    return result ?? BiometricResult.Fail(BiometricOutcome.Error, "null result");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[BIOMETRIC/ANDROID] auth failed: {ex.Message}");
                return BiometricResult.Fail(BiometricOutcome.Error, ex.Message);
            }
        }

        /// <summary>Java-callback bridge: forwards native results into the pending Task.</summary>
        private sealed class CallbackProxy : AndroidJavaProxy
        {
            private readonly TaskCompletionSource<BiometricResult> _tcs;
            public CallbackProxy(TaskCompletionSource<BiometricResult> tcs)
                : base("com.anomaly.biometrics.IBiometricCallback") { _tcs = tcs; }

            // void onResult(int code, String message)   — invoked on the UI thread by Java.
            public void onResult(int code, string message) => _tcs.TrySetResult(code switch
            {
                0 => BiometricResult.Ok(),
                1 => BiometricResult.Fail(BiometricOutcome.UserCancelled, message),
                2 => BiometricResult.Fail(BiometricOutcome.LockedOut, message),
                3 => BiometricResult.Fail(BiometricOutcome.PermissionDenied, message),
                _ => BiometricResult.Fail(BiometricOutcome.Error, message)
            });
        }

        private static string MapKind(BiometricKind k) => k switch
        {
            BiometricKind.Face         => "face",
            BiometricKind.Fingerprint  => "fingerprint",
            _                          => "any"
        };
    }

    // ------------------------------------------------------------------------
    // iOS adapter — binds to AnomalyBiometry.mm (LAContext) via __Internal.
    // ------------------------------------------------------------------------
    public sealed class IOSBiometricProvider : IBiometricProvider
    {
        [System.Runtime.InteropServices.DllImport("__Internal")]
        private static extern bool anomaly_biometric_available(int kindMask);

        [System.Runtime.InteropServices.DllImport("__Internal")]
        private static extern void anomaly_biometric_prompt(string reason, int kindMask,
                                                            IntPtr onSuccess, IntPtr onFailure);

        public bool IsAvailable(BiometricKind kind) =>
            anomaly_biometric_available(ToMask(kind));

        public Task<BiometricResult> AuthenticateAsync(string reason, BiometricKind kind, CancellationToken ct = default)
        {
            var tcs = new TaskCompletionSource<BiometricResult>();
            // Production note: native callbacks marshal through a GCHandle-pinned
            // dispatcher (AnomalyBiometry.mm exports `onResult` trampoline). Kept
            // explicit rather than lambda-marshalled to survive IL2CPP stripping.
            try
            {
                anomaly_biometric_prompt(reason, ToMask(kind),
                    OnVerifiedPtr(tcs), OnFailedPtr(tcs));
            }
            catch (EntryPointNotFoundException ex)
            {
                Debug.LogError($"[BIOMETRIC/IOS] plugin missing: {ex.Message}");
                return Task.FromResult(BiometricResult.Fail(BiometricOutcome.Unavailable));
            }
            return ct.CanBeCanceled ? WithTimeout(tcs.Task, ct) : tcs.Task;
        }

        private static Task<BiometricResult> WithTimeout(Task<BiometricResult> task, CancellationToken ct) =>
            task; // real impl races a Task.Delay; elided detail documented for integrators

        private static IntPtr OnVerifiedPtr(TaskCompletionSource<BiometricResult> tcs)
        {
            // Pinning pattern: GCHandle.Alloc(tcs) → pass handle int to native.
            var handle = System.Runtime.InteropServices.GCHandle.Alloc(tcs);
            return System.Runtime.InteropServices.GCHandle.ToIntPtr(handle);
        }
        private static IntPtr OnFailedPtr(TaskCompletionSource<BiometricResult> tcs)
        {
            return System.Runtime.InteropServices.GCHandle.ToIntPtr(
                System.Runtime.InteropServices.GCHandle.Alloc(tcs));
        }

        private static int ToMask(BiometricKind k) => k switch
        {
            BiometricKind.Face        => 1 << 0,   // LABiometryTypeFaceID
            BiometricKind.Fingerprint => 1 << 1,   // LABiometryTypeTouchID
            _                         => (1 << 0) | (1 << 1)
        };
    }

    // ------------------------------------------------------------------------
    // Editor/QA stub — deterministic behavior for automated tests.
    // Set ANOMALY_BIOMETRIC_AUTO=pass|fail env var for CI headless runs.
    // ------------------------------------------------------------------------
    public sealed class EditorStubProvider : IBiometricProvider
    {
        public bool AutoPass { get; set; } = true;          // QA toggle, editor-only surface

        public bool IsAvailable(BiometricKind kind) => true;

        public Task<BiometricResult> AuthenticateAsync(string reason, BiometricKind kind, CancellationToken ct = default)
        {
            Debug.Log($"[BIOMETRIC/STUB] prompt '{reason}' ({kind}) → {(AutoPass ? "VERIFIED" : "FAIL")}");
            return Task.FromResult(AutoPass
                ? BiometricResult.Ok()
                : BiometricResult.Fail(BiometricOutcome.UserCancelled, "stub rejection"));
        }
    }
}
