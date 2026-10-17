// ============================================================================
// GameBootstrap.cs
// ----------------------------------------------------------------------------
// Project   : ANOMALY // Anti-Gravity Open World (Unity 2022.3+ / URP)
// Layer     : Core / Composition Root
// Author    : Lead Engine Architect
// ----------------------------------------------------------------------------
// Purpose:
//   Single deterministic entry point for the whole runtime stack. Boot order
//   is security-critical: subsystems must be initialized in dependency order
//   so that no privileged surface (Owner Dashboard, Save System) is ever
//   reachable before its guard has been installed.
//
// Boot sequence contract (DO NOT REORDER):
//   1. HardwareProfileService  -> passive capability probe (no side effects)
//   2. GraphicsAutoTuner       -> applies LOD / quality before first frame
//   3. LocalVault              -> AES-256 save layer (keyed by device owner)
//   4. AuthenticationManager   -> OAuth 2.0 + biometric session bootstrap
//   5. OwnerSystemController   -> INSTALLED LAST, deliberately invisible; it
//                                 self-links only after auth is ready and is
//                                 stripped entirely from release builds.
//   6. PhysicsWorldDirector    -> anti-gravity simulation (consumes LOD data)
//
// Build-time isolation:
//   The Owner subsystem lives behind [OWNER_CONSOLE]. Unity "Define Symbols"
//   are NOT trusted as a sole boundary (they can be recompiled into a build),
//   therefore at RUNTIME we additionally require a hardware-bound credential
//   (see OwnerSystemController.TryResolveEntitlement). Release shipping
//   builds should ALSO be compiled without the symbol for defense in depth.
// ============================================================================

using System;
using System.Threading.Tasks;
using Anomaly.Hardware;
using Anomaly.Auth;
using Anomaly.Security;
using Anomaly.Storage;
using Anomaly.Dashboard;
using Anomaly.Physics;
using UnityEngine;

namespace Anomaly.Core
{
    /// <summary>
    /// Deterministic, ordered runtime composition root.
    /// Attach to an empty GameObject marked `DontDestroyOnLoad` in the boot scene.
    /// </summary>
    public sealed class GameBootstrap : MonoBehaviour
    {
        [Header("Boot Configuration (non-sensitive only)")]
        [SerializeField] private bool verboseBootLog = true;

        // Public read-only handles so other systems depend on interfaces, not scenes.
        public static HardwareProfileService Hardware    { get; private set; }
        public static GraphicsAutoTuner      AutoTuner   { get; private set; }
        public static ILocalVault            Vault       { get; private set; }
        public static AuthenticationManager  Auth        { get; private set; }
        public static PhysicsWorldDirector   Physics     { get; private set; }

#if OWNER_CONSOLE
        // Never surfaced publicly: only reachable through the controller itself.
        private OwnerSystemController _ownerConsole;
#endif

        private bool _bootComplete;

        private async void Awake()
        {
            // Enforce singleton exactly once per process lifetime.
            if (Hardware != null)
            {
                Destroy(gameObject);
                return;
            }
            DontDestroyOnLoad(gameObject);

            try
            {
                await BootAsync();
                _bootComplete = true;
                Log("[BOOT] Runtime stack online.");
            }
            catch (Exception ex)
            {
                // Fail closed: never continue with a half-initialized security stack.
                Debug.LogError($"[BOOT] Fatal initialization error: {ex.Message}");
                enabled = false;
                throw;
            }
        }

        private async Task BootAsync()
        {
            Log("[BOOT] 1/6 Hardware profiling ...");
            var go = new GameObject("[Anomaly Services]");
            go.transform.SetParent(transform, false);

            Hardware = go.AddComponent<HardwareProfileService>();
            await Hardware.ProbeAsync();                       // RAM / cores / GPU

            Log("[BOOT] 2/6 Graphics auto-tune ...");
            AutoTuner = go.AddComponent<GraphicsAutoTuner>();
            AutoTuner.Apply(Hardware.Current);                 // LOD & throttling guards

            Log("[BOOT] 3/6 Local vault ...");
            Vault = new SecureLocalVault();                    // AES-256 + HMAC-SHA256 (encrypt-then-MAC)
            await Vault.OpenAsync();                           // opens device-scoped store

            Log("[BOOT] 4/6 Authentication ...");
            Auth = go.AddComponent<AuthenticationManager>();
            Auth.Vault = Vault;                                // token custody via encrypted store
            await Auth.InitializeAsync();                      // OAuth config + session resume

            Log("[BOOT] 5/6 Privileged console ...");
#if OWNER_CONSOLE
            _ownerConsole = go.AddComponent<OwnerSystemController>();
            await _ownerConsole.InstallAsync(Vault, Auth);     // inert until entitlement + biometric
#endif

            Log("[BOOT] 6/6 Physics world ...");
            Physics = go.AddComponent<PhysicsWorldDirector>();
            Physics.Configure(AutoTuner.SimulationBudget);     // fixed-step budget from LOD tier

            Log("[BOOT] Complete.");
        }

        /// <summary>
        /// Clean-shutdown hook: guarantees encrypted flush of pending writes.
        /// </summary>
        private void OnApplicationQuit()
        {
            if (!_bootComplete) return;
            try { Vault?.Flush(); }
            catch (Exception ex) { Debug.LogError($"[BOOT] Flush failed on quit: {ex.Message}"); }
        }

        private void Log(string msg)
        {
            if (verboseBootLog) Debug.Log(msg);
        }
    }
}
