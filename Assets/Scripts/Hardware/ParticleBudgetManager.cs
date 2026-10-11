// ============================================================================
// ParticleBudgetManager.cs
// ----------------------------------------------------------------------------
// Layer   : Hardware / VFX Governance
// Purpose : Single knob that scales the live particle budget engine-wide.
//           The GraphicsAutoTuner writes GlobalBudgetPercent; every VFX spawner
//           reads it before emitting, so weak devices never even instantiate
//           expensive emitters (cheaper than runtime culling).
// ============================================================================

using System;
using UnityEngine;

namespace Anomaly.Hardware
{
    public static class ParticleBudgetManager
    {
        private const int MaxSafePercent = 200; // guard against fat-finger tuning data

        /// <summary>
        /// Composite percentage: tier preset × adaptive notch factor.
        /// 100 == authored budget; clamped to [0, 200].
        /// </summary>
        public static int GlobalBudgetPercent
        {
            get => _percent;
            set
            {
                int v = Mathf.Clamp(value, 0, MaxSafePercent);
                if (v != _percent)
                {
                    _percent = v;
                    OnBudgetChanged?.Invoke(v);
                }
            }
        }
        private static int _percent = 100;

        /// <summary>Fired whenever the budget changes — VFX managers re-scale in response.</summary>
        public static event Action<int> OnBudgetChanged;

        /// <summary>
        /// Convenience: returns false when a spawner should NOT emit at all
        /// (used by low tiers to skip decorative effects entirely below 35%).
        /// </summary>
        public static bool ShouldEmit(string effectTag, int authoredMaxParticles) =>
            _percent >= 35 || string.Equals(effectTag, "critical", StringComparison.OrdinalIgnoreCase)
                              && authoredMaxParticles <= 64; // tiny gameplay-critical cues always survive
    }
}
