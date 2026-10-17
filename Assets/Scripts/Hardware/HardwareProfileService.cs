// ============================================================================
// HardwareProfileService.cs
// ----------------------------------------------------------------------------
// Layer   : Hardware / Capability Profiling
// Purpose : Detects RAM, CPU cores, GPU renderer/API/tier at initialization and
//           produces an immutable HardwareProfile that every downstream system
//           (graphics auto-tune, physics budget, particle LOD) consumes.
// Notes   : Pure probe — applies NO settings itself. Safe to call from any
//           platform; unknown GPUs degrade gracefully to "conservative" tier.
// ============================================================================

using System;
using System.Threading.Tasks;
using UnityEngine;
// NOTE: `SystemInfo` lives in the global namespace. Importing `UnityEngine.SystemInfo`
// as a namespace is illegal and would break compilation — deliberately omitted.

namespace Anomaly.Hardware
{
    /// <summary>Coarse device classification produced by the profiler.</summary>
    public enum DeviceTier { EntryMobile, MidMobile, HighMobile, ConsoleClass, DesktopHighEnd }

    /// <summary>Graphics API families we tune against (Vulkan/Metal behave differently).</summary>
    public enum GfxApiFamily { OpenGL, Vulkan, Metal, Direct3D11, Direct3D12, Unknown }

    /// <summary>
    /// Immutable snapshot of host capability. Generated once at boot;
    /// re-probe only on explicit request (e.g., thermal state change).
    /// </summary>
    public sealed class HardwareProfile
    {
        // ---- Raw facts -----------------------------------------------------
        public int   TotalSystemMemoryMB   { get; init; }   // SystemInfo.systemMemorySize (MB)
        public int   CpuCoreCount          { get; init; }   // Environment.ProcessorCount
        public string CpuModel              { get; init; }
        public string GpuDeviceName         { get; init; }  // SystemInfo.graphicsDeviceName
        public int   GpuMemoryMB            { get; init; }  // SystemInfo.graphicsMemorySize (MB)
        public GfxApiFamily GfxApi          { get; init; }
        public int   ShaderLevel            { get; init; }  // SystemInfo.graphicsShaderLevel (50 = SM5)
        public bool  SupportsComputeShaders { get; init; }
        public bool  SupportsInstancing     { get; init; }   // GPU-instancing capable => big world batching
        public bool  IsBatteryPowered       { get; init; }   // mobile path => throttle-aware
        public int   ScreenDPI              { get; init; }
        public string OperatingSystem       { get; init; }

        // ---- Derived verdict -----------------------------------------------
        public DeviceTier Tier                { get; init; }
        /// <summary>Rough fill-rate proxy: pixels × relative GPU score, used for shadow/particle scaling.</summary>
        public float    RelativeGpuScore      { get; init; } // 0.0 (weak) .. 3.0 (enthusiast)

        public override string ToString() =>
            $"[HW] tier={Tier} ram={TotalSystemMemoryMB}MB cores={CpuCoreCount} " +
            $"gpu='{GpuDeviceName}' vram={GpuMemoryMB}MB api={GfxApi} shader={ShaderLevel} score={RelativeGpuScore:F2}";
    }

    /// <summary>
    /// Passive hardware probe component. Add via GameBootstrap; read <see cref="Current"/> afterwards.
    /// </summary>
    public sealed class HardwareProfileService : MonoBehaviour
    {
        /// <summary>Last completed profile. Throws if probed before initialization.</summary>
        public HardwareProfile Current =>
            _profile ?? throw new InvalidOperationException("HardwareProfileService.ProbeAsync() has not completed.");

        private HardwareProfile _profile;

        /// <summary>
        /// Runs detection as an async initialization step. UnityEngine.SystemInfo must be
        /// sampled on the render/main thread, so sampling happens here synchronously and
        /// only the (pure) tier computation is deferred to a worker task.
        /// </summary>
        public Task ProbeAsync()
        {
            var raw = SampleUnityApis();                    // main-thread Unity API sampling
            return Task.Run(() =>
            {
                _profile = BuildProfile(raw);               // pure math — safe off-thread
                Debug.Log(_profile.ToString());
            });
        }

        /// <summary>Synchronous variant for editor tools / tests.</summary>
        public HardwareProfile ProbeNow()
        {
            _profile = BuildProfile(SampleUnityApis());
            return _profile;
        }

        // --------------------------------------------------------------------
        // Internals
        // --------------------------------------------------------------------

        private static RawSample SampleUnityApis() => new RawSample
        {
            RamMB        = SystemInfo.systemMemorySize,
            VramMB       = SystemInfo.graphicsMemorySize,
            GpuName      = SystemInfo.graphicsDeviceName ?? "unknown",
            ShaderLevel  = SystemInfo.graphicsShaderLevel,
            GfxApi       = SystemInfo.graphicsDeviceType,
            ComputeOK    = SystemInfo.supportsComputeShaders,
            InstancingOK = SystemInfo.supportsInstancing,
            DPI          = Mathf.RoundToInt(Screen.dpi <= 0 ? 96 : Screen.dpi),
            OS           = SystemInfo.operatingSystem ?? "unknown",
            Battery      = Application.isMobilePlatform && SystemInfo.batteryStatus != BatteryStatus.Charging,
            Cores        = SystemInfo.processorCount,
            CpuModel     = SystemInfo.processorType ?? "unknown"
        };

        private static HardwareProfile BuildProfile(RawSample s)
        {
            var api = MapApi(s.GfxApi);

            // Heuristic composite GPU score:
            //   shader model band  + VRAM band + API modernity bonus + name-keyword bonus.
            float shaderScore = s.ShaderLevel switch
            {
                >= 55 => 1.4f,   // SM5.0+ / Metal tier high
                >= 50 => 1.1f,
                >= 45 => 0.7f,
                >= 40 => 0.45f,
                _     => 0.2f
            };
            float vramScore = s.VramMB switch
            {
                >= 8192 => 1.0f,
                >= 4096 => 0.7f,
                >= 2048 => 0.45f,
                >= 1024 => 0.25f,
                _       => 0.1f
            };
            float apiBonus = api switch
            {
                GfxApiFamily.Direct3D12 or GfxApiFamily.Vulkan or GfxApiFamily.Metal => 0.35f,
                GfxApiFamily.Direct3D11 => 0.2f,
                _ => 0f
            };
            float nameBonus = 0f;
            foreach (var kw in new[] { "RTX", "RX 7", "RX 6", "Apple M", "Adreno 7", "Mali-G7", "GeForce" })
                if (s.GpuName.Contains(kw, StringComparison.OrdinalIgnoreCase)) { nameBonus = 0.4f; break; }

            float score = Mathf.Clamp01((shaderScore + vramScore + apiBonus + nameBonus) / 2.75f) * 3f;

            // Tier fusion: memory + cores + gpu score. Thresholds chosen so that a
            // 3GB/4-core phone lands EntryMobile while a 16GB/8-core desktop lands DesktopHighEnd.
            DeviceTier tier;
            if (score > 2.1f && s.RamMB >= 12_000 && s.Cores >= 8)      tier = DeviceTier.DesktopHighEnd;
            else if (score > 1.5f && !Application.isMobilePlatform)      tier = DeviceTier.ConsoleClass;
            else if (score > 1.2f && s.RamMB >= 4_096)                   tier = DeviceTier.HighMobile;
            else if (score > 0.7f && s.RamMB >= 3_072)                   tier = DeviceTier.MidMobile;
            else                                                          tier = DeviceTier.EntryMobile;

            return new HardwareProfile
            {
                TotalSystemMemoryMB   = s.RamMB,
                CpuCoreCount          = Math.Max(1, s.Cores),
                CpuModel              = s.CpuModel,
                GpuDeviceName         = s.GpuName,
                GpuMemoryMB           = s.VramMB,
                GfxApi                = api,
                ShaderLevel           = s.ShaderLevel,
                SupportsComputeShaders= s.ComputeOK,
                SupportsInstancing    = s.InstancingOK,
                IsBatteryPowered      = s.Battery,
                ScreenDPI             = s.DPI,
                OperatingSystem       = s.OS,
                Tier                  = tier,
                RelativeGpuScore      = score
            };
        }

        private static GfxApiFamily MapApi(GraphicsDeviceType t) => t switch
        {
            GraphicsDeviceType.OpenGLES2 or GraphicsDeviceType.OpenGLES3 => GfxApiFamily.OpenGL,
            GraphicsDeviceType.Vulkan                                   => GfxApiFamily.Vulkan,
            GraphicsDeviceType.Metal                                    => GfxApiFamily.Metal,
            GraphicsDeviceType.Direct3D11                               => GfxApiFamily.Direct3D11,
            GraphicsDeviceType.Direct3D12                               => GfxApiFamily.Direct3D12,
            _                                                           => GfxApiFamily.Unknown
        };

        private struct RawSample
        {
            public int RamMB, VramMB, ShaderLevel, DPI, Cores;
            public string GpuName, OS, CpuModel;
            public GraphicsDeviceType GfxApi;
            public bool ComputeOK, InstancingOK, Battery;
        }
    }
}
