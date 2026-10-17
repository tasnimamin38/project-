// ============================================================================
// GraphicsAutoTuner.cs
// ----------------------------------------------------------------------------
// Layer   : Hardware / Runtime Quality Governance
// Purpose : Converts a HardwareProfile into concrete engine settings BEFORE the
//           first rendered frame, and continuously guards against thermal /
//           frame-time degradation ("hardware throttling prevention").
//
// Tuning matrix (authored per DeviceTier):
//   texture resolution bias | shadow distance/resolution/cascade mode | target FPS
//   particle budget scale   | LOD bias | dynamic-resolution limits | vsync
//
// Adaptive loop:
//   Samples smoothed frame time; if it exceeds the tier's ceiling for a sustained
//   window it degrades ONE notch (never more than one per window) and recovers
//   symmetrically. Hysteresis prevents oscillation ("rubber-banding").
// ============================================================================

using System;
using Anomaly.Hardware;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;        // URP asset access (package com.unity.render-pipelines.universal)

namespace Anomaly.Hardware
{
    /// <summary>Simulation-side budget handed to the physics director.</summary>
    public sealed class SimulationBudget
    {
        public float FixedDeltaTime   { get; init; }  // physics step size
        public int   MaxSubSteps      { get; init; }  // catch-up steps per frame
        public int   RaycastLayerMask { get; init; }  // reduced collision layers on weak tiers
        public bool  UseAsyncPhysics  { get; init; }  // Unity Physics async (multi-core) when cores allow
    }

    /// <summary>One notch of visual fidelity. The tuner walks these up/down.</summary>
    public enum QualityNotch { UltraLow, Low, Medium, High, Ultra }

    /// <summary>
    /// Applies and adapts graphics settings. Add via GameBootstrap; call Apply(profile) once.
    /// </summary>
    public sealed class GraphicsAutoTuner : MonoBehaviour
    {
        // ---- Authored per-tier presets -------------------------------------
        [Serializable]
        private sealed class TierPreset
        {
            public QualityNotch startingNotch;
            public QualityNotch minNotch = QualityNotch.UltraLow;
            public QualityNotch maxNotch = QualityNotch.Ultra;
            public int  targetFps;               // frame-rate cap (thermal guard)
            public int  textureBiasPercent;      // -100..0 -> full-res..1/2^x res
            public ShadowQualitySetting shadows; // our own enum (mapped to engine below)
            public float shadowDistanceMeters;
            public int  particleBudgetScalePercent; // % of authored particle emitters allowed live
            public float lodBias;                   // <1 = higher quality meshes at distance
            public Vector2 dynamicResRange;         // min..max render scale
        }

        public enum ShadowQualitySetting { Off, HardOnly, TwoCascades, FourCascades }

        // Notch → concrete engine values. Single source of truth for the mapping.
        private static readonly ShadowQualitySetting[] NotchShadow =
            { ShadowQualitySetting.Off, ShadowQualitySetting.HardOnly, ShadowQualitySetting.TwoCascades,
              ShadowQualitySetting.TwoCascades, ShadowQualitySetting.FourCascades };
        private static readonly int[] NotchShadowRes = { 0, 512, 1024, 2048, 4096 };
        private static readonly int[] NotchTexBias   = { -2, -1, -1, 0, 0 };
        private static readonly float[] NotchLodBias = { 2.0f, 1.5f, 1.2f, 1.0f, 0.7f };
        private static readonly int[] NotchParticles = { 30, 50, 75, 100, 100 };

        private static readonly TierPreset[] Presets =
        {
            // EntryMobile: 2GB-3GB phones, Adreno 6xx/Mali-G5x — battery & thermals first.
            new TierPreset { startingNotch=QualityNotch.Low,     maxNotch=QualityNotch.Medium, targetFps=30,
                             textureBiasPercent=-50, shadows=ShadowQualitySetting.HardOnly, shadowDistanceMeters=30f,
                             particleBudgetScalePercent=40, lodBias=1.6f, dynamicResRange=new Vector2(0.55f, 0.8f) },
            // MidMobile
            new TierPreset { startingNotch=QualityNotch.Medium, maxNotch=QualityNotch.High,   targetFps=60,
                             textureBiasPercent=-25, shadows=ShadowQualitySetting.TwoCascades, shadowDistanceMeters=60f,
                             particleBudgetScalePercent=65, lodBias=1.2f, dynamicResRange=new Vector2(0.7f, 1.0f) },
            // HighMobile
            new TierPreset { startingNotch=QualityNotch.High,   maxNotch=QualityNotch.Ultra,  targetFps=60,
                             textureBiasPercent=0,   shadows=ShadowQualitySetting.FourCascades, shadowDistanceMeters=90f,
                             particleBudgetScalePercent=90, lodBias=1.0f, dynamicResRange=new Vector2(0.8f, 1.0f) },
            // ConsoleClass
            new TierPreset { startingNotch=QualityNotch.High,   maxNotch=QualityNotch.Ultra,  targetFps=60,
                             textureBiasPercent=0,   shadows=ShadowQualitySetting.FourCascades, shadowDistanceMeters=150f,
                             particleBudgetScalePercent=100, lodBias=1.0f, dynamicResRange=new Vector2(0.9f, 1.0f) },
            // DesktopHighEnd
            new TierPreset { startingNotch=QualityNotch.Ultra,  maxNotch=QualityNotch.Ultra,  targetFps=144,
                             textureBiasPercent=0,   shadows=ShadowQualitySetting.FourCascades, shadowDistanceMeters=250f,
                             particleBudgetScalePercent=100, lodBias=0.7f, dynamicResRange=new Vector2(1.0f, 1.4f) },
        };

        // ---- Public surface -------------------------------------------------
        public SimulationBudget SimulationBudget { get; private set; }
        public QualityNotch CurrentNotch => _notch;
        public event Action<QualityNotch> OnNotchChanged;

        // ---- Adaptive-loop state -------------------------------------------
        private HardwareProfile _profile;
        private TierPreset _preset;
        private QualityNotch _notch;
        private float _smoothedFrameMs;
        private const float SmoothingAlpha = 0.1f;   // EMA coefficient
        private const float DegradeWindowS = 6f;     // sustained-over-budget seconds before degrade
        private const float RecoverWindowS = 20f;    // headroom seconds before upgrade (slower = stable)
        private double _overBudgetTimer, _headroomTimer;

        /// <summary>Initial application. Call exactly once after hardware probe.</summary>
        public void Apply(HardwareProfile profile)
        {
            if (_profile != null) throw new InvalidOperationException("GraphicsAutoTuner.Apply called twice.");
            _profile = profile ?? throw new ArgumentNullException(nameof(profile));
            _preset  = Presets[(int)profile.Tier];
            _notch   = ClampNotch(_preset.startingNotch);

            ApplyNotch();
            ApplyStaticSettings();
            SimulationBudget = BuildBudget(profile);
            Debug.Log($"[QUALITY] Auto-tuned to {_notch} for tier {profile.Tier} @ cap {_preset.targetFps}fps");
        }

        private void Update()
        {
            if (_profile == null) return;

            // Exponentially-smoothed frame time (robust to single spikes).
            float dtMs = Time.unscaledDeltaTime * 1000f;
            _smoothedFrameMs = Mathf.Lerp(_smoothedFrameMs <= 0 ? dtMs : _smoothedFrameMs, dtMs, SmoothingAlpha);

            double budgetMs  = 1000.0 / _preset.targetFps;
            double degradeAt = budgetMs * 1.25;   // 25% over target => struggling
            double recoverAt = budgetMs * 0.80;   // 20% headroom      => can afford more

            // Timers accumulate with unscaled real delta (survives timescale/pause changes).
            float realDelta = Time.unscaledDeltaTime;
            if (dtMs > degradeAt) { _overBudgetTimer += realDelta; _headroomTimer = 0; }
            else if (dtMs < recoverAt) { _headroomTimer += realDelta; _overBudgetTimer = 0; }
            else { _overBudgetTimer = Math.Max(0, _overBudgetTimer - realDelta * 0.5);
                   _headroomTimer  = Math.Max(0, _headroomTimer  - realDelta * 0.5); }

            if (_overBudgetTimer >= DegradeWindowS) { StepNotch(-1); _overBudgetTimer = 0; }
            if (_headroomTimer  >= RecoverWindowS)  { StepNotch(+1); _headroomTimer  = 0; }

            // Thermal/battery guard on mobile: OS tells us it is throttling.
            if (_profile.IsBatteryPowered && Application.isMobilePlatform &&
                SystemInfo.batteryStatus is BatteryStatus.Low or BatteryStatus.Critical)
            {
                ForceCap(Math.Min(_preset.targetFps, 30)); // protect SoC from sustained boost
            }
        }

        // --------------------------------------------------------------------
        // Internals
        // --------------------------------------------------------------------

        private void StepNotch(int direction)
        {
            var next = ClampNotch((QualityNotch)((int)_notch + direction));
            if (next == _notch) return;                 // already at preset boundary — hysteresis holds
            _notch = next;
            ApplyNotch();
            OnNotchChanged?.Invoke(_notch);
            Debug.Log($"[QUALITY] Adaptive notch -> {_notch} ({_smoothedFrameMs:F1}ms avg)");
        }

        private QualityNotch ClampNotch(QualityNotch n) =>
            (QualityNotch)Mathf.Clamp((int)n, (int)_preset.minNotch, (int)_preset.maxNotch);

        private void ApplyNotch()
        {
            int i = (int)_notch;

            // --- Textures ---
            // masterTextureLimit = number of mip levels to skip (0 = full resolution).
            // NotchTexBias is authored as a negative "resolution bias" percentage-style
            // hint; here it maps directly to mip-skip counts, combined with tier intent.
            int notchSkip   = -NotchTexBias[i];                       // 0..2
            int presetSkip  = _preset.textureBiasPercent switch { -50 => 1, -25 => 1, _ => 0 };
            QualitySettings.masterTextureLimit = Math.Max(notchSkip, presetSkip);

            // --- Shadows ---
            QualitySettings.shadowResolution = NotchShadowRes[i] switch
            {
                <= 512 => UnityEngine.Rendering.ShadowResolution.None,   // "None" == disable in modern Unity
                <= 1024 => UnityEngine.Rendering.ShadowResolution.Low,
                <= 2048 => UnityEngine.Rendering.ShadowResolution.Medium,
                <= 4096 => UnityEngine.Rendering.ShadowResolution.High,
                _       => UnityEngine.Rendering.ShadowResolution.VeryHigh
            };
            QualitySettings.shadowDistance = _preset.shadowDistanceMeters * (0.5f + 0.125f * i);
            QualitySettings.shadows = NotchShadow[i] switch
            {
                ShadowQualitySetting.Off          => ShadowQuality.Disable,
                ShadowQualitySetting.HardOnly     => ShadowQuality.HardOnly,
                ShadowQualitySetting.TwoCascades  => ShadowQuality.All,
                _                                 => ShadowQuality.All
            };
            QualitySettings.shadowCascades = NotchShadow[i] switch
            {
                ShadowQualitySetting.TwoCascades  => 2,
                ShadowQualitySetting.FourCascades => 4,
                _                                 => 0
            };

            // --- Frame rate / vsync ---
            ForceCap(_preset.targetFps);
            QualitySettings.vSyncCount = _profile.IsBatteryPowered ? 0 : (_notch >= QualityNotch.High ? 1 : 0);

            // --- LOD & particles ---
            QualitySettings.lodBias = _preset.lodBias * (1f + (2 - i) * 0.15f);
            ParticleBudgetManager.GlobalBudgetPercent =
                _preset.particleBudgetScalePercent * NotchParticles[i] / 100;

            // --- Dynamic resolution (URP) ---
            var urp = UniversalRenderPipeline.asset;
            if (urp != null)
            {
                urp.renderScale = Mathf.Lerp(_preset.dynamicResRange.x,
                                             _preset.dynamicResRange.y, i / 4f);
            }

            // --- Async physics availability ---
            // (flag consumed by PhysicsWorldDirector through SimulationBudget)
        }

        private void ApplyStaticSettings()
        {
            // Anisotropic filtering only where fill-rate allows; cheap win on desktops.
            QualitySettings.anisotropicFiltering = _notch >= QualityNotch.High
                ? AnisotropicFiltering.Enable : AnisotropicFiltering.Disable;

            // Realtime GI off below High — baked lighting carries the world.
            QualitySettings.realtimeGICPUUsage = _notch >= QualityNotch.High
                ? RealtimeGICPUUsage.High : RealtimeGICPUUsage.Low;

            // Skin-width rendering & SSAO are tier-gated inside URP renderer features;
            // here we flip global toggles that apply everywhere:
            QualitySettings.softParticles = _notch >= QualityNotch.Medium;
        }

        private SimulationBudget BuildBudget(HardwareProfile p) => new SimulationBudget
        {
            // Weak CPUs cannot afford 120Hz physics: widen step, cap catch-up.
            FixedDeltaTime  = p.Tier <= DeviceTier.MidMobile ? 1f / 40f : 1f / 60f,
            MaxSubSteps     = p.Tier <= DeviceTier.EntryMobile ? 1 : p.Tier <= DeviceTier.HighMobile ? 2 : 4,
            // Entry tiers collide only against World/Ground layers (see LayerMaskCache).
            RaycastLayerMask = p.Tier <= DeviceTier.MidMobile
                ? LayerMask.GetMask("World", "Ground")
                : ~0,
            UseAsyncPhysics = p.CpuCoreCount >= 6 && !Application.isMobilePlatform
        };

        private void ForceCap(int fps)
        {
            // targetFrameRate is the cross-platform throttle knob (mobile drivers honor it).
            Application.targetFrameRate = Mathf.Max(fps, 24);
        }
    }
}
