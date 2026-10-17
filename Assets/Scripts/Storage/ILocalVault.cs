// ============================================================================
// ILocalVault.cs
// ----------------------------------------------------------------------------
// Layer   : Storage / Contract
// Purpose : Stable abstraction over the encrypted persistence layer so callers
//           (ProgressStore, OwnerSystemController, AuthManager) never depend on
//           the concrete implementation. Swapping local AES storage for a
//           cloud-synced vault later touches ONE class.
// ============================================================================

using System.Threading.Tasks;

namespace Anomaly.Storage
{
    public interface ILocalVault
    {
        /// <summary>Opens/initializes the backing store. Must be awaited before first use.</summary>
        Task OpenAsync();

        /// <summary>Sealed write: value is encrypted + MAC'd under a per-key derived envelope.</summary>
        Task WriteAsync(string key, byte[] payload);

        /// <summary>Returns plaintext payload or null when absent. Throws TamperDetectedException upstream.</summary>
        Task<byte[]> ReadAsync(string key);

        /// <summary>Atomically removes a record.</summary>
        void Delete(string key);

        /// <summary>True when a record exists (never decrypts).</summary>
        bool Exists(string key);

        /// <summary>Blocking flush of pending writes to durable media (call on quit/pause).</summary>
        void Flush();

        /// <summary>Secure wipe of every record + key material (used by tamper response & account deletion).</summary>
        void Wipe();
    }
}
