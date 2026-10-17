// ============================================================================
// SecurePrefs.cs
// ----------------------------------------------------------------------------
// Layer   : Security / Device-Bound Key Hierarchy
// Purpose : Wraps Unity's PlayerPrefs (plaintext on most platforms!) with a
//           device-bound AES-256 envelope so anything stored is unreadable
//           without THIS device + THIS install.
//
// Key hierarchy (local-first MVP, cloud-ready later):
//   wrapKey  = PBKDF2( deviceUniqueIdentifier | bundleId | version ,
//                      fixed pepper salt )            — regenerated every run,
//                                                        never persisted.
//   masterKey= random 32 bytes, minted on first launch, stored SEALED under
//              wrapKey via AesHmacEnvelope (encrypt-then-MAC).
//   subkeys  = AesHmacEnvelope.FromMaster(masterKey, per-record context)
//
// Threat model notes:
//   * Copying PlayerPrefs to another device → wrapKey differs → MAC fails → wipe.
//   * Editing the sealed blob on-device      → MAC fails → wipe (anti forging).
//   * Production hardening path: replace seed material with platform keystore
//     attestation (Android KeyStore API 23+ / iOS ThisDeviceOnly accessibility).
// ============================================================================

using System;
using Anomaly.Security;
using UnityEngine;

namespace Anomaly.Storage
{
    public static class SecurePrefs
    {
        private const string WrappedKey     = "anomaly.vault.wkey";   // sealed master key (base64 envelope)
        private const int    WrapIterations = 150_000;                // boot-path KDF: strong but not UI-blocking

        // Fixed pepper salt for the deterministic wrap key. This is NOT secret-grade;
        // it provides domain separation only — real secrecy comes from device identity.
        private static readonly byte[] WrapSalt =
            Hashing.FromHex("5a6e6f6d616c792d7661756c742d777261702d73616c7421"); // "Anomaly-vault-wrap-salt!" as hex

        /// <summary>Returns the device-wrapped vault master key (32 bytes), creating it on first use.</summary>
        public static byte[] GetOrCreateMasterKey()
        {
            var envelope = AesHmacEnvelope.FromMaster(DeriveWrapKey(), "vault-wrap");

            if (!PlayerPrefs.HasKey(WrappedKey))
            {
                // First launch: mint master key and persist it sealed.
                byte[] master = CryptoRandom.Bytes(32);
                try
                {
                    PlayerPrefs.SetString(WrappedKey, Convert.ToBase64String(envelope.Seal(master)));
                    PlayerPrefs.Save();
                }
                finally { Array.Clear(master, 0, master.Length); } // zero before GC loses control of it
            }

            try
            {
                byte[] sealedKey = Convert.FromBase64String(PlayerPrefs.GetString(WrappedKey));
                return envelope.Open(sealedKey);   // throws TamperDetectedException if moved/copied/edited
            }
            catch (TamperDetectedException ex)
            {
                // Fail closed. On a legit device this only happens after manual tampering.
                Debug.LogError($"[VAULT] Master key integrity failure — secure wipe. {ex.Message}");
                WipeAll();
                throw new InvalidOperationException("Vault corrupted by tampering; wiped.", ex);
            }
            catch (Exception ex) when (ex is FormatException or InvalidCastException)
            {
                Debug.LogError("[VAULT] Malformed key storage; reinitializing.");
                WipeAll();
                return GetOrCreateMasterKey();     // bounded single retry — storage was just reset
            }
        }

        /// <summary>Securely removes all vault artifacts from PlayerPrefs.</summary>
        public static void WipeAll()
        {
            PlayerPrefs.DeleteKey(WrappedKey);
            PlayerPrefs.Save();
        }

        // --------------------------------------------------------------------
        // Internals
        // --------------------------------------------------------------------

        /// <summary>Deterministic per-device wrap key (see header for hardening path).</summary>
        private static byte[] DeriveWrapKey()
        {
            string seedMaterial = $"{SystemInfo.deviceUniqueIdentifier}|{Application.identifier}|{Application.version}";
            using var kdf = new System.Security.Cryptography.Rfc2898DeriveBytes(
                seedMaterial, WrapSalt, WrapIterations, System.Security.Cryptography.HashAlgorithmName.SHA256);
            return kdf.GetBytes(32);
        }
    }
}
