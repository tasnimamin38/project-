// ============================================================================
// PhysicsWorldDirector.cs
// ----------------------------------------------------------------------------
// Layer   : Physics / Simulation Director
// Purpose : Owns the global gravity contract (zeroes built-in gravity so the
//           AntiGravityField system is the single source of acceleration),
//           receives the per-tier SimulationBudget from GraphicsAutoTuner, and
//           applies field forces to subscribed bodies in FixedUpdate.
// ============================================================================

using System;
using System.Collections.Generic;
using Anomaly.Hardware;
using UnityEngine;

namespace Anomaly.Physics
{
    /// <summary>
    /// Attach to any Rigidbody body that should react to anti-gravity fields.
    /// </summary>
    public interface IAntiGravityBody
    {
        Rigidbody Body { get; }
        bool AffectedByFields { get; }
    }

    /// <summary>
    /// Central physics governor. Exactly one instance exists (created by GameBootstrap).
    /// </summary>
    public sealed class PhysicsWorldDirector : MonoBehaviour
    {
        [SerializeField] private float globalGravityScale = 1f;   // designer knob (0 = pure zero-g)

        private SimulationBudget _budget;
        private static readonly List<IAntiGravityBody> Bodies = new(512);

        // --------------------------------------------------------------------
        // Configuration (called once by GameBootstrap after auto-tune)
        // --------------------------------------------------------------------
        public void Configure(SimulationBudget budget)
        {
            _budget = budget ?? throw new ArgumentNullException(nameof(budget));

            // THE core trick: kill engine gravity; fields become the only force source.
            Physics.gravity = Vector3.zero;

            Time.fixedDeltaTime     = budget.FixedDeltaTime;
            Physics.autoSimulation  = true;                       // keep deterministic step ownership

            // Async multi-core physics where the hardware allows it (Unity 2022+ API surface).
            if (budget.UseAsyncPhysics && Application.isEditor == false)
            {
                try { Physics.Simulate(_budget.FixedDeltaTime); } // warm-up validates the step size
                catch (Exception ex) { Debug.LogWarning($"[PHYS] async precheck skipped: {ex.Message}"); }
            }

            Debug.Log($"[PHYS] Budget: step={budget.FixedDeltaTime * 1000:F1}ms substeps={budget.MaxSubSteps} " +
                      $"async={budget.UseAsyncPhysics} fields={AntiGravityField.ActiveFieldCount}");
        }

        // --------------------------------------------------------------------
        // Body registry — bodies call these in OnEnable/OnDisable.
        // --------------------------------------------------------------------
        public static void Register(IAntiGravityBody body)
        {
            if (body != null && !Bodies.Contains(body)) Bodies.Add(body);
        }

        public static void Unregister(IAntiGravityBody body) => Bodies.Remove(body);

        // --------------------------------------------------------------------
        // Fixed update: O(N×M) worst case but bounded — each body samples only
        // fields whose radius covers it (cheap sqr-distance reject first).
        // --------------------------------------------------------------------
        private void FixedUpdate()
        {
            if (_budget == null || Bodies.Count == 0) return;

            for (int i = 0; i < Bodies.Count; i++)
            {
                var ab = Bodies[i];
                if (ab == null) { Bodies.RemoveAt(i--); continue; }          // tolerate destroyed refs
                if (!ab.AffectedByFields || ab.Body == null || ab.Body.isKinematic) continue;

                Vector3 acc = AntiGravityField.SampleAll(ab.Body.position) * globalGravityScale;

                // Terminal-velocity guard: open-world falls through stacked fields must not
                // accumulate unbounded speed (tunneling & jitter on low tiers).
                const float MaxSpeed = 180f;
                var vel = ab.Body.velocity;                       // Unity 2022.3 API (`linearVelocity` is Unity 6)
                if (vel.sqrMagnitude > MaxSpeed * MaxSpeed) continue;

                ab.Body.AddForce(acc, ForceMode.Acceleration);
            }
        }
    }
}
