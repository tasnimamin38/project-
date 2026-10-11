// ============================================================================
// ProgressStore.cs  (level models + encrypted repository)
// ----------------------------------------------------------------------------
// Layer   : Storage / Game Data Model
// Purpose : Typed progression model for the tiered campaign:
//             Tiers: Basic → Silver → Gold → Diamond → Platinum → Multi-Platinum
//             Each tier contains exactly 10 sub-levels (indices 0..9).
//           ProgressStore serializes to JSON, routes ALL persistence through
//           ILocalVault (so upgrading to cloud sync later = new adapter only),
//           and version-gates the schema for forward migration.
//
// Currency note: amounts are stored as long; "lifetime unlimited" grants from
// the Owner console use LongMaxSentinel rather than a magic float so economy
// math never overflows or renders NaN in UI bindings.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Anomaly.Security;
using UnityEngine;

namespace Anomaly.Storage
{
    // ------------------------------------------------------------------------
    // Enumerations & records
    // ------------------------------------------------------------------------

    public enum Tier { Basic = 0, Silver, Gold, Diamond, Platinum, MultiPlatinum }

    /// <summary>Persisted state of one sub-level.</summary>
    [Serializable]
    public sealed class SubLevelState
    {
        public int   tierIndex;              // 0..5 (Tier enum) — int for stable serialization
        public int   subLevelIndex;          // 0..9
        public bool  unlocked;
        public bool  completed;
        public int   stars;                  // 0..3
        public float bestTimeSeconds;        // 0 = no record yet
        public long  score;
        public long  updatedAtTicks;         // server-sync conflict resolution later
    }

    /// <summary>Wallet snapshot. Sentinel value == "unlimited" (Owner grant).</summary>
    [Serializable]
    public sealed class CurrencyWallet
    {
        public const long LongMaxSentinel = long.MaxValue;

        public long gold;
        public long diamond;
        public long platinum;

        public static bool IsUnlimited(long v) => v == LongMaxSentinel;

        public void GrantUnlimitedLifetime()
        {
            gold = diamond = platinum = LongMaxSentinel;
        }
    }

    /// <summary>Root save document. Keep field set additive-only across versions.</summary>
    [Serializable]
    public sealed class PlayerProgress
    {
        public const int CurrentSchemaVersion = 1;
        public const int SubLevelsPerTier     = 10;   // hard design constant

        public int schemaVersion = CurrentSchemaVersion;
        public string profileId = "";                 // OAuth subject claim binding
        public CurrencyWallet wallet = new();
        public List<SubLevelState> levels = new(60);  // 6 tiers × 10
        public int currentTier = 0;
        public int currentSubLevel = 0;
    }

    // ------------------------------------------------------------------------
    // Store
    // ------------------------------------------------------------------------

    /// <summary>
    /// Encrypted progress repository. One instance per authenticated profile.
    /// </summary>
    public sealed class ProgressStore
    {
        private readonly ILocalVault _vault;
        private readonly string _recordKey;           // vault key: "progress.<profileHash>"
        private PlayerProgress _cache;                // single source of truth while loaded

        public PlayerProgress Current =>
            _cache ?? throw new InvalidOperationException("ProgressStore.LoadAsync not awaited.");

        public ProgressStore(ILocalVault vault, string profileId)
        {
            _vault = vault ?? throw new ArgumentNullException(nameof(vault));
            // Hash the profile id so raw identifiers never appear in file names.
            _recordKey = "progress." + Hashing.Sha256Hex(profileId ?? "anon").Substring(0, 16);
        }

        // ---- Load / Save ----------------------------------------------------

        public async Task<PlayerProgress> LoadAsync()
        {
            byte[] data = await _vault.ReadAsync(_recordKey);
            if (data == null)
            {
                _cache = CreateFresh();
                await SaveAsync();                       // first-run write establishes baseline
            }
            else
            {
                _cache = DeserializeOrMigrate(data);
            }
            return _cache;
        }

        /// <summary>Serializes + seals + durably writes. Await on level-complete / pause.</summary>
        public async Task SaveAsync()
        {
            if (_cache == null) throw new InvalidOperationException("Load first.");
            TouchTimestamps();
            await _vault.WriteAsync(_recordKey, JsonUtility.ToJson(_cache).AsUtf8());
        }

        /// <summary>Hot-path variant: buffered, flushed by vault Flush() on pause/quit.</summary>
        public void SaveBuffered()
        {
            if (_cache == null) return;
            TouchTimestamps();
            if (_vault is SecureLocalVault slv)
                slv.WriteBuffered(_recordKey, JsonUtility.ToJson(_cache).AsUtf8());
            else
                _ = SaveAsync();     // interface-only fallback implementations must be async
        }

        // ---- Gameplay mutations ---------------------------------------------

        /// <summary>Marks a sub-level complete, awards stars, unlocks the next level in tier order.</summary>
        public void CompleteLevel(Tier tier, int subIndex, float timeSeconds, long score, int stars)
        {
            ValidateCoords(tier, subIndex);
            var s = GetOrCreate(tier, subIndex);
            s.completed = true;
            s.unlocked  = true;
            s.stars     = Math.Max(s.stars, Mathf.Clamp(stars, 0, 3));
            s.score     = Math.Max(s.score, score);
            if (s.bestTimeSeconds <= 0 || timeSeconds < s.bestTimeSeconds)
                s.bestTimeSeconds = timeSeconds;

            // Cascade unlock: next sub-level, else next tier's first level.
            int t = (int)tier;
            if (subIndex + 1 < PlayerProgress.SubLevelsPerTier)
                GetOrCreate(tier, subIndex + 1).unlocked = true;
            else if (t + 1 <= (int)Tier.MultiPlatinum)
                GetOrCreate((Tier)(t + 1), 0).unlocked = true;
        }

        public void SetCurrentLevel(Tier tier, int subIndex)
        {
            ValidateCoords(tier, subIndex);
            if (!GetOrCreate(tier, subIndex).unlocked)
                throw new InvalidOperationException($"Level {tier}/{subIndex} is locked.");
            _cache.currentTier = (int)tier;
            _cache.currentSubLevel = subIndex;
        }

        /// <summary>Supported currencies of the economy.</summary>
        public enum Currency { Gold, Diamond, Platinum }

        /// <summary>
        /// Economy debit. The unlimited sentinel is sticky: spending while granted
        /// "lifetime unlimited" always succeeds and never drains the sentinel.
        /// </summary>
        public bool TrySpend(Currency currency, long amount)
        {
            if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));
            if (_cache == null) throw new InvalidOperationException("Load first.");

            var w = _cache.wallet;

            // Unlimited sentinel is sticky per-currency: never debit it.
            switch (currency)
            {
                case Currency.Gold:
                    if (CurrencyWallet.IsUnlimited(w.gold)) return true;
                    if (w.gold < amount) return false;
                    w.gold -= amount;
                    return true;
                case Currency.Diamond:
                    if (CurrencyWallet.IsUnlimited(w.diamond)) return true;
                    if (w.diamond < amount) return false;
                    w.diamond -= amount;
                    return true;
                case Currency.Platinum:
                    if (CurrencyWallet.IsUnlimited(w.platinum)) return true;
                    if (w.platinum < amount) return false;
                    w.platinum -= amount;
                    return true;
                default:
                    throw new ArgumentOutOfRangeException(nameof(currency));
            }
        }

        // ---- Tamper response --------------------------------------------------

        /// <summary>
        /// Call when vault surfaces TamperDetectedException: quarantine + rebuild fresh.
        /// Never silently trust decrypted-but-suspicious data.
        /// </summary>
        public async Task QuarantineAndResetAsync()
        {
            Debug.LogError($"[PROGRESS] Tamper detected on '{_recordKey}' — resetting to fresh state.");
            _vault.Delete(_recordKey);
            _cache = CreateFresh();
            await SaveAsync();
        }

        // ---- Internals ----------------------------------------------------------

        private static PlayerProgress CreateFresh()
        {
            var p = new PlayerProgress { profileId = "" };
            // Launch state: Basic tier, sub-level 0 unlocked.
            p.levels.Add(new SubLevelState { tierIndex = 0, subLevelIndex = 0, unlocked = true });
            return p;
        }

        private SubLevelState GetOrCreate(Tier tier, int subIndex)
        {
            foreach (var l in _cache.levels)
                if (l.tierIndex == (int)tier && l.subLevelIndex == subIndex) return l;

            var created = new SubLevelState { tierIndex = (int)tier, subLevelIndex = subIndex };
            _cache.levels.Add(created);
            return created;
        }

        private static void ValidateCoords(Tier tier, int subIndex)
        {
            if (tier < Tier.Basic || tier > Tier.MultiPlatinum)
                throw new ArgumentOutOfRangeException(nameof(tier));
            if (subIndex < 0 || subIndex >= PlayerProgress.SubLevelsPerTier)
                throw new ArgumentOutOfRangeException(nameof(subIndex),
                    $"Sub-levels are 0..{PlayerProgress.SubLevelsPerTier - 1}.");
        }

        private void TouchTimestamps()
        {
            long now = DateTime.UtcNow.Ticks;
            foreach (var l in _cache.levels) l.updatedAtTicks = now;
        }

        private PlayerProgress DeserializeOrMigrate(byte[] data)
        {
            try
            {
                var p = JsonUtility.FromJson<PlayerProgress>(SecureLocalVault.ToUtf8(data))
                        ?? throw new InvalidCastException("Null root object.");

                if (p.schemaVersion > PlayerProgress.CurrentSchemaVersion)
                    throw new NotSupportedException(
                        $"Save v{p.schemaVersion} newer than build v{PlayerProgress.CurrentSchemaVersion}.");

                // Additive migration hook point (v0 -> v1 etc.). Keep forward-only.
                p.wallet ??= new CurrencyWallet();
                p.levels ??= new List<SubLevelState>();
                p.schemaVersion = PlayerProgress.CurrentSchemaVersion;
                return p;
            }
            catch (Exception ex)
            {
                // Corrupt-but-MAC-valid (shouldn't happen) → treat as tamper.
                Debug.LogError($"[PROGRESS] Deserialization failed: {ex.Message}");
                return CreateFresh();
            }
        }
    }

    internal static class StringCodec
    {
        public static byte[] AsUtf8(this string s) => System.Text.Encoding.UTF8.GetBytes(s);
    }
}
