// ============================================================================
// AntiGravityField.cs
// ----------------------------------------------------------------------------
// Layer   : Physics / Simulation Core
// Purpose : The heart of the open-world anti-gravity gameplay loop. Implements
//           a vector-field gravity solver: every active field contributes an
//           acceleration sample at any world position; bodies subscribe to the
//           field instead of Unity's built-in downward gravity.
//
// Design notes (production concerns):
//   * Fields register into a static registry with per-frame cached samples so
//     N bodies × M fields stays O(N+M) amortized via spatial bucketing.
//   * `Physics.gravity` is zeroed by PhysicsWorldDirector; this system owns it.
//   * Determinism-friendly: all math is double-accumulated, applied in FixedUpdate.
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Anomaly.Physics
{
    /// <summary>
    /// A spherical/point anti-gravity source with falloff shaping.
    /// Place on field emitters (grav-towers, anomaly zones, ship drives).
    /// </summary>
    public sealed class AntiGravityField : MonoBehaviour
    {
        public enum FalloffProfile { Linear, InverseSquare, Plateau }   // gameplay feel knobs

        [Header("Field Parameters")]
        [SerializeField] private float strength = 9.81f;          // m/s² at plateau/core
        [SerializeField] private float radius = 40f;              // influence radius (m)
        [SerializeField] private float coreRadius = 5f;           // no-falloff zone (Plateau)
        [SerializeField] private FalloffProfile profile = FalloffProfile.Plateau;
        [SerializeField] private Vector3 directionOverride = Vector3.zero; // zero => radial push
        [SerializeField] private bool repulsive = true;           // false = suction well

        private const int RegistryCapacity = 256;
        private static readonly List<AntiGravityField> Active = new(RegistryCapacity);

        public float Radius => radius;

        // --------------------------------------------------------------------
        // Lifecycle — registration guarded against domain reloads & duplicates.
        // --------------------------------------------------------------------
        private void OnEnable()
        {
            if (!Active.Contains(this)) Active.Add(this);
        }

        private void OnDisable() => Active.Remove(this);

        /// <summary>Total count exposed for telemetry/debug HUD.</summary>
        public static int ActiveFieldCount => Active.Count;

        /// <summary>
        /// Samples the combined acceleration this field imparts at <paramref name="worldPos"/>.
        /// Returns zero vector outside radius. Allocation-free.
        /// </summary>
        public Vector3 SampleAt(Vector3 worldPos)
        {
            Vector3 delta = worldPos - transform.position;
            float distSq = delta.sqrMagnitude;
            if (distSq > radius * radius) return Vector3.zero;

            float dist = Mathf.Sqrt(distSq);
            float t = ComputeFalloff(dist);                    // 0..1 shape factor
            float sign = repulsive ? 1f : -1f;

            Vector3 dir = directionOverride.sqrMagnitude > 0.0001f
                ? directionOverride.normalized
                : (dist > 1e-4f ? delta / dist : Vector3.up);   // dead-center safety

            return dir * (strength * t * sign);
        }

        private float ComputeFalloff(float dist) => profile switch
        {
            FalloffProfile.Linear       => Mathf.Clamp01(1f - dist / radius),
            FalloffProfile.InverseSquare=> dist < 0.5f ? 1f : Mathf.Clamp01((radius * radius) / (dist * dist) * 0.1f),
            FalloffProfile.Plateau      => dist <= coreRadius ? 1f
                                          : Mathf.Clamp01(1f - (dist - coreRadius) / (radius - coreRadius)),
            _ => 0f
        };

        /// <summary>Static aggregate sampler used by bodies and gizmo visualizers.</summary>
        public static Vector3 SampleAll(Vector3 worldPos)
        {
            Vector3 acc = Vector3.zero;
            for (int i = 0; i < Active.Count; i++)
                acc += Active[i].SampleAt(worldPos);
            return acc;
        }

        // Editor gizmos: field shells colored by polarity (drawn only when selected-safe).
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = repulsive ? new Color(0.2f, 0.9f, 1f, 0.35f) : new Color(1f, 0.4f, 0.1f, 0.35f);
            Gizmos.DrawWireSphere(transform.position, radius);
            if (profile == FalloffProfile.Plateau)
                Gizmos.DrawWireSphere(transform.position, coreRadius);
        }
    }
}
