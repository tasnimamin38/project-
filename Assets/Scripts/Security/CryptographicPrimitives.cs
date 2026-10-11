// ============================================================================
// CryptographicPrimitives.cs
// ----------------------------------------------------------------------------
// Layer   : Security / Crypto Core (engine-agnostic — no UnityEngine dependency)
// Purpose : The ONLY place raw crypto is touched. Everything else (vault, auth,
//           owner console) composes these primitives, which keeps key material,
//           algorithm choices and side-channel hygiene auditable in one file.
//
// Choices & rationale (production):
//   * AES-256-CBC + HMAC-SHA256 (encrypt-then-MAC): available on every Unity
//     runtime incl. .NET Standard 2.0 IL2CPP. GCM would be preferred but
//     System.Security.Cryptography lacks AEAD below .NET Core — the MAC over
//     IV||ciphertext closes the padding-oracle/malleability gap correctly.
//   * PBKDF2-HMAC-SHA256 @ 310k iterations (OWASP 2023 guidance) for KDF.
//   * Constant-time comparisons everywhere secrets are checked.
//   * RandomNumbers.CryptoRandom wraps RNGCryptoServiceProvider; never use
//     System.Random for security material.
// ============================================================================

using System;
using System.Security.Cryptography;
using System.Text;

namespace Anomaly.Security
{
    /// <summary>Allocation-light helpers for secure random bytes.</summary>
    public static class CryptoRandom
    {
        private static readonly RandomNumberGenerator Rng = RandomNumberGenerator.Create();

        public static byte[] Bytes(int length)
        {
            if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length));
            var buf = new byte[length];
            Rng.GetBytes(buf);
            return buf;
        }

        /// <summary>Fills an existing buffer (hot paths should reuse arrays).</summary>
        public static void Fill(byte[] buffer) => Rng.GetBytes(buffer);

        /// <summary>URL-safe base64 secret/token string.</summary>
        public static string Token(int byteLength = 32) =>
            Convert.ToBase64String(Bytes(byteLength))
                    .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static class Hashing
    {
        /// <summary>Plain SHA-256 digest (hex, lowercase).</summary>
        public static string Sha256Hex(byte[] data)
        {
            using var sha = SHA256.Create();
            return ToLowerHex(sha.ComputeHash(data));
        }

        /// <summary>Lowercase hex encoder compatible with .NET Standard 2.0.</summary>
        private static string ToLowerHex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        public static string Sha256Hex(string text) => Sha256Hex(Encoding.UTF8.GetBytes(text));

        /// <summary>Salted iterated hash for passcodes/secrets stored at rest.</summary>
        public static byte[] Pbkdf2(string secret, byte[] salt, int iterations = 310_000, int hashLength = 32)
        {
            using var kdf = new Rfc2898DeriveBytes(secret, salt, iterations, HashAlgorithmName.SHA256);
            return kdf.GetBytes(hashLength);
        }

        /// <summary>Constant-time equality — prevents timing oracles on secret checks.</summary>
        public static bool FixedTimeEquals(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            // Manual constant-time fold: identical behavior on .NET Standard 2.0 (IL2CPP),
            // where CryptographicOperations.FixedTimeEquals is not guaranteed to exist.
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        public static bool FixedTimeEqualsHex(string hexA, string hexB)
        {
            if (hexA == null || hexB == null || hexA.Length != hexB.Length) return false;
            try
            {
                return FixedTimeEquals(FromHex(hexA), FromHex(hexB));
            }
            catch (FormatException) { return false; } // malformed input never authenticates
        }

        /// <summary>Hex decoder compatible with .NET Standard 2.0 (no Convert.FromHexString).</summary>
        public static byte[] FromHex(string hex)
        {
            if (hex == null || hex.Length % 2 != 0) throw new FormatException("Invalid hex length.");
            var bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            return bytes;
        }
    }

    /// <summary>
    /// Encrypt-then-MAC envelope codec used by the local vault and the owner
    /// entitlement record. Layout: [1 ver | 16 IV | 32 hmacTag | ciphertext...]
    /// </summary>
    public sealed class AesHmacEnvelope
    {
        public const byte Version = 0x01;
        private const int IvLen = 16, TagLen = 32;

        private readonly byte[] _aesKey;   // 32 bytes
        private readonly byte[] _macKey;   // 32 bytes

        /// <summary>Derives independent enc/mac subkeys from one master secret (domain separation).</summary>
        public static AesHmacEnvelope FromMaster(byte[] masterSecret, string contextTag)
        {
            if (masterSecret == null || masterSecret.Length < 16)
                throw new ArgumentException("Master secret too short.");
            // HKDF-style expansion via PBKDF2 with distinct salts per subkey.
            byte[] enc = Hashing.Pbkdf2(Convert.ToBase64String(masterSecret),
                        Encoding.UTF8.GetBytes(contextTag + ":enc"), 100_000, 32);
            byte[] mac = Hashing.Pbkdf2(Convert.ToBase64String(masterSecret),
                        Encoding.UTF8.GetBytes(contextTag + ":mac"), 100_000, 32);
            return new AesHmacEnvelope(enc, mac);
        }

        private AesHmacEnvelope(byte[] aesKey, byte[] macKey)
        {
            _aesKey = aesKey; _macKey = macKey;
        }

        public byte[] Seal(byte[] plaintext)
        {
            if (plaintext == null) throw new ArgumentNullException(nameof(plaintext));
            byte[] iv = CryptoRandom.Bytes(IvLen);

            using var aes = Aes.Create();
            aes.KeySize = 256;
            aes.Key = _aesKey;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            byte[] cipher;
            using (var encryptor = aes.CreateEncryptor())
                cipher = encryptor.TransformFinalBlock(plaintext, 0, plaintext.Length);

            byte[] payload = Concat(iv, cipher);
            byte[] tag = Hmac(payload);

            var envelope = new byte[1 + TagLen + payload.Length];
            envelope[0] = Version;
            Buffer.BlockCopy(tag, 0, envelope, 1, TagLen);
            Buffer.BlockCopy(payload, 0, envelope, 1 + TagLen, payload.Length);
            CryptographicOperations.ZeroMemory(payload);
            return envelope;
        }

        /// <summary>
        /// Opens an envelope. Throws <see cref="TamperDetectedException"/> on any
        /// structural or MAC failure — callers must treat that as "data destroyed",
        /// never attempt repair-by-retry (anti save-game forging).
        /// </summary>
        public byte[] Open(byte[] envelope)
        {
            if (envelope == null || envelope.Length < 1 + TagLen + IvLen + 16)
                throw new TamperDetectedException("Envelope truncated.");
            if (envelope[0] != Version)
                throw new TamperDetectedException($"Unknown envelope version {envelope[0]:X2}.");

            int payloadLen = envelope.Length - 1 - TagLen;
            byte[] payload = new byte[payloadLen];
            byte[] tag = new byte[TagLen];
            Buffer.BlockCopy(envelope, 1, tag, 0, TagLen);
            Buffer.BlockCopy(envelope, 1 + TagLen, payload, 0, payloadLen);

            if (!Hashing.FixedTimeEquals(Hmac(payload), tag))
                throw new TamperDetectedException("MAC verification failed.");

            byte[] iv = new byte[IvLen];
            byte[] cipher = new byte[payloadLen - IvLen];
            Buffer.BlockCopy(payload, 0, iv, 0, IvLen);
            Buffer.BlockCopy(payload, IvLen, cipher, 0, cipher.Length);
            CryptographicOperations.ZeroMemory(payload);

            using var aes = Aes.Create();
            aes.KeySize = 256; aes.Key = _aesKey; aes.IV = iv;
            aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7;
            try
            {
                using var decryptor = aes.CreateDecryptor();
                return decryptor.TransformFinalBlock(cipher, 0, cipher.Length);
            }
            catch (CryptographicException ex)
            {
                throw new TamperDetectedException("Decryption failed (padding).", ex);
            }
            finally { CryptographicOperations.ZeroMemory(cipher); }
        }

        private byte[] Hmac(byte[] data)
        {
            using var hmac = new HMACSHA256(_macKey);
            return hmac.ComputeHash(data);
        }

        private static byte[] Concat(byte[] a, byte[] b)
        {
            var r = new byte[a.Length + b.Length];
            Buffer.BlockCopy(a, 0, r, 0, a.Length);
            Buffer.BlockCopy(b, 0, r, a.Length, b.Length);
            return r;
        }
    }

    /// <summary>Raised when authenticated-encryption integrity fails.</summary>
    public sealed class TamperDetectedException : Exception
    {
        public TamperDetectedException(string message) : base(message) { }
        public TamperDetectedException(string message, Exception inner) : base(message, inner) { }
    }
}
