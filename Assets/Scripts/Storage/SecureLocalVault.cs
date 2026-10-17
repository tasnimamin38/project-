// ============================================================================
// SecureLocalVault.cs
// ----------------------------------------------------------------------------
// Layer   : Storage / AES-256 Implementation
// Purpose : Local-first encrypted record store. Records live as individual
//           files under persistentPath/vault/, each one an AesHmacEnvelope
//           sealed with a per-record subkey derived from the device-bound
//           master key (domain separation: key name is part of the KDF context).
//
// Why files instead of PlayerPrefs for payloads?
//   * Large saves (level states) exceed PlayerPrefs ergonomics.
//   * Atomic replace via temp-file + move prevents half-written corruption.
//   * Per-record subkeys mean a leak of one record cannot be decrypted by
//     reading another (defense in depth on rooted devices).
//
// Integrity: any MAC failure surfaces as TamperDetectedException; callers must
// quarantine the record, never silently "repair" it.
// ============================================================================

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Anomaly.Security;
using UnityEngine;

namespace Anomaly.Storage
{
    public sealed class SecureLocalVault : ILocalVault
    {
        private const string VaultFolderName = "vault";
        private const string FileExtension   = ".anvlt";       // Anomaly Vault Record

        private readonly ConcurrentDictionary<string, byte[]> _pending = new(); // write-behind buffer
        private byte[] _masterKey;                               // 32 bytes, zeroed on Wipe/Dispose
        private string _rootPath;
        private bool _open;

        // --------------------------------------------------------------------
        // ILocalVault
        // --------------------------------------------------------------------

        public async Task OpenAsync()
        {
            if (_open) return;

            // Master key derivation runs PBKDF2 — keep it off the main thread.
            _masterKey = await Task.Run(SecurePrefs.GetOrCreateMasterKey);

            _rootPath = Path.Combine(Application.persistentDataPath, VaultFolderName);
            Directory.CreateDirectory(_rootPath);

            _open = true;
            Debug.Log($"[VAULT] Opened at {_rootPath}");
        }

        public async Task WriteAsync(string key, byte[] payload)
        {
            EnsureOpen();
            ValidateKey(key);
            if (payload == null) throw new ArgumentNullException(nameof(payload));

            // Seal on a worker thread: envelopes are CPU-bound (AES + HMAC).
            var envelope = await Task.Run(() => EnvelopeFor(key).Seal(payload));
            string path = PathOf(key);

            // Atomic write: temp then move — crash mid-write can't corrupt prior save.
            string tmp = path + ".tmp";
            await Task.Run(() =>
            {
                File.WriteAllBytes(tmp, envelope);
                if (File.Exists(path)) File.Replace(tmp, path, null);
                else File.Move(tmp, path);
            });

            _pending.TryRemove(key, out _); // buffered value now durable
            CryptographicOperations.ZeroMemory(envelope);
        }

        public async Task<byte[]> ReadAsync(string key)
        {
            EnsureOpen();
            ValidateKey(key);
            string path = PathOf(key);
            if (!File.Exists(path)) return null;

            return await Task.Run(() =>
            {
                byte[] envelope = File.ReadAllBytes(path);
                try { return EnvelopeFor(key).Open(envelope); }
                finally { CryptographicOperations.ZeroMemory(envelope); }
            });
        }

        public void Delete(string key)
        {
            EnsureOpen();
            ValidateKey(key);
            _pending.TryRemove(key, out _);
            string path = PathOf(key);
            if (File.Exists(path)) File.Delete(path);
        }

        public bool Exists(string key)
        {
            EnsureOpen();
            ValidateKey(key);
            return File.Exists(PathOf(key));
        }

        /// <summary>
        /// Flushes any buffered hot-path writes (ProgressStore uses WriteBuffered).
        /// Safe to call from OnApplicationPause/Quit.
        /// </summary>
        public void Flush()
        {
            if (!_open) return;
            foreach (var (key, payload) in _pending.ToArray())
            {
                try { WriteAsync(key, payload).GetAwaiter().GetResult(); }
                catch (Exception ex) { Debug.LogError($"[VAULT] Flush failed for '{key}': {ex.Message}"); }
            }
        }

        public void Wipe()
        {
            if (_rootPath != null && Directory.Exists(_rootPath))
                Directory.Delete(_rootPath, recursive: true);
            _pending.Clear();
            if (_masterKey != null) CryptographicOperations.ZeroMemory(_masterKey);
            _masterKey = null;
            _open = false;
            SecurePrefs.WipeAll();
            Debug.LogWarning("[VAULT] Full secure wipe completed.");
        }

        /// <summary>Fire-and-forget style buffered write for frequent gameplay updates.</summary>
        public void WriteBuffered(string key, byte[] payload)
        {
            EnsureOpen();
            ValidateKey(key);
            _pending[key] = payload; // latest wins; flushed periodically / on pause
        }

        // --------------------------------------------------------------------
        // Internals
        // --------------------------------------------------------------------

        private void EnsureOpen()
        {
            if (!_open) throw new InvalidOperationException("SecureLocalVault.OpenAsync() was not awaited.");
        }

        private string PathOf(string key) => Path.Combine(_rootPath, key + FileExtension);

        /// <summary>Keys become file names — sanitize aggressively (no traversal).</summary>
        private static void ValidateKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key) || key.Length > 96)
                throw new ArgumentException("Invalid vault key.", nameof(key));
            foreach (char c in key)
                if (!(char.IsLetterOrDigit(c) || c is '.' or '_' or '-'))
                    throw new ArgumentException($"Vault key contains illegal char '{c}'.", nameof(key));
            if (key.Contains("..")) throw new ArgumentException("Traversal rejected.", nameof(key));
        }

        // --------------------------------------------------------------------
        // Internals
        // --------------------------------------------------------------------

        // Envelope cache: per-record subkey derivation is PBKDF2-heavy, so we mint
        // each envelope once and reuse it for the vault's lifetime.
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, AesHmacEnvelope> _envelopes = new();

        /// <summary>Per-record envelope: master key × domain-separated context.</summary>
        private AesHmacEnvelope EnvelopeFor(string key) =>
            _envelopes.GetOrAdd(key, k => AesHmacEnvelope.FromMaster(_masterKey, "record:" + k));

        // Convenience codec so callers deal with strings without leaking encoding details.
        public static byte[] Utf8(string s)  => Encoding.UTF8.GetBytes(s);
        public static string ToUtf8(byte[] b) => Encoding.UTF8.GetString(b);
    }
}
