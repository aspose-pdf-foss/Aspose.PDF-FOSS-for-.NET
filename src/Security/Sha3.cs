// Copyright (c) 2001-2026 Aspose Pty Ltd. All Rights Reserved.

using System;
using System.Security.Cryptography;

namespace Aspose.Pdf.Security
{
    /// <summary>
    /// Own managed SHA-3 (FIPS 202) sponge over the Keccak-f[1600] permutation.
    /// No dependency on the platform's System.Security.Cryptography.SHA3 — this is
    /// the FOSS fallback used when the runtime/OS does not provide SHA-3 natively,
    /// mirroring how <see cref="ShaDigest"/> replaces the BCL SHA-2 primitives.
    /// </summary>
    internal sealed class Sha3Core
    {
        // Keccak-f[1600] round constants (ι step).
        private static readonly ulong[] RoundConstants =
        {
            0x0000000000000001UL, 0x0000000000008082UL, 0x800000000000808AUL, 0x8000000080008000UL,
            0x000000000000808BUL, 0x0000000080000001UL, 0x8000000080008081UL, 0x8000000000008009UL,
            0x000000000000008AUL, 0x0000000000000088UL, 0x0000000080008009UL, 0x000000008000000AUL,
            0x000000008000808BUL, 0x800000000000008BUL, 0x8000000000008089UL, 0x8000000000008003UL,
            0x8000000000008002UL, 0x8000000000000080UL, 0x000000000000800AUL, 0x800000008000000AUL,
            0x8000000080008081UL, 0x8000000000008080UL, 0x0000000080000001UL, 0x8000000080008008UL,
        };

        // Rotation offsets (ρ step), indexed by lane = x + 5*y.
        private static readonly int[] Rho =
        {
             0,  1, 62, 28, 27,
            36, 44,  6, 55, 20,
             3, 10, 43, 25, 39,
            41, 45, 15, 21,  8,
            18,  2, 61, 56, 14,
        };

        private readonly ulong[] _state = new ulong[25];
        private readonly byte[] _block;
        private readonly int _rate;        // sponge rate in bytes
        private readonly int _digestBytes;
        private int _pos;

        public Sha3Core(int digestBytes)
        {
            _digestBytes = digestBytes;
            _rate = 200 - 2 * digestBytes; // rate = 1600/8 - capacity; capacity = 2 * digest size
            _block = new byte[_rate];
            Reset();
        }

        public void Reset()
        {
            Array.Clear(_state, 0, _state.Length);
            _pos = 0;
        }

        public void Update(byte[] data, int offset, int count)
        {
            for (int i = 0; i < count; i++)
            {
                _block[_pos++] = data[offset + i];
                if (_pos == _rate)
                {
                    AbsorbBlock();
                    _pos = 0;
                }
            }
        }

        public byte[] Digest()
        {
            // SHA-3 padding: domain-separation bits 01 then pad10*1 => first pad
            // byte 0x06, last rate byte high bit 0x80 (both XORed into the block).
            for (int i = _pos; i < _rate; i++) _block[i] = 0;
            _block[_pos] ^= 0x06;
            _block[_rate - 1] ^= 0x80;
            AbsorbBlock();

            // Squeeze. For SHA3-256/384/512 the digest never exceeds the rate, so a
            // single output block suffices; loop anyway for completeness.
            var output = new byte[_digestBytes];
            int produced = 0;
            while (produced < _digestBytes)
            {
                int take = Math.Min(_rate, _digestBytes - produced);
                for (int i = 0; i < take; i++)
                    output[produced + i] = (byte)(_state[i >> 3] >> (8 * (i & 7)));
                produced += take;
                if (produced < _digestBytes) KeccakF(_state);
            }

            Reset();
            return output;
        }

        private void AbsorbBlock()
        {
            for (int i = 0; i < _rate / 8; i++)
            {
                ulong lane = 0;
                for (int b = 0; b < 8; b++)
                    lane |= (ulong)_block[i * 8 + b] << (8 * b);
                _state[i] ^= lane;
            }
            KeccakF(_state);
        }

        private static ulong Rol(ulong v, int n) => (v << n) | (v >> (64 - n));

        private static void KeccakF(ulong[] a)
        {
            var c = new ulong[5];
            var d = new ulong[5];
            var b = new ulong[25];

            for (int round = 0; round < 24; round++)
            {
                // θ
                for (int x = 0; x < 5; x++)
                    c[x] = a[x] ^ a[x + 5] ^ a[x + 10] ^ a[x + 15] ^ a[x + 20];
                for (int x = 0; x < 5; x++)
                    d[x] = c[(x + 4) % 5] ^ Rol(c[(x + 1) % 5], 1);
                for (int x = 0; x < 5; x++)
                    for (int y = 0; y < 25; y += 5)
                        a[x + y] ^= d[x];

                // ρ and π
                for (int x = 0; x < 5; x++)
                    for (int y = 0; y < 5; y++)
                        b[y + ((2 * x + 3 * y) % 5) * 5] = Rol(a[x + 5 * y], Rho[x + 5 * y]);

                // χ
                for (int y = 0; y < 25; y += 5)
                    for (int x = 0; x < 5; x++)
                        a[x + y] = b[x + y] ^ ((~b[(x + 1) % 5 + y]) & b[(x + 2) % 5 + y]);

                // ι
                a[0] ^= RoundConstants[round];
            }
        }
    }

    /// <summary>SHA3-256 (FIPS 202) — own Keccak implementation.</summary>
    public sealed class Sha3_256 : HashAlgorithm
    {
        private readonly Sha3Core _core = new Sha3Core(32);
        public Sha3_256() { HashSizeValue = 256; }
        public static new Sha3_256 Create() => new Sha3_256();
        public override void Initialize() => _core.Reset();
        protected override void HashCore(byte[] array, int ibStart, int cbSize) => _core.Update(array, ibStart, cbSize);
        protected override byte[] HashFinal() => _core.Digest();
    }

    /// <summary>SHA3-384 (FIPS 202) — own Keccak implementation.</summary>
    public sealed class Sha3_384 : HashAlgorithm
    {
        private readonly Sha3Core _core = new Sha3Core(48);
        public Sha3_384() { HashSizeValue = 384; }
        public static new Sha3_384 Create() => new Sha3_384();
        public override void Initialize() => _core.Reset();
        protected override void HashCore(byte[] array, int ibStart, int cbSize) => _core.Update(array, ibStart, cbSize);
        protected override byte[] HashFinal() => _core.Digest();
    }

    /// <summary>SHA3-512 (FIPS 202) — own Keccak implementation.</summary>
    public sealed class Sha3_512 : HashAlgorithm
    {
        private readonly Sha3Core _core = new Sha3Core(64);
        public Sha3_512() { HashSizeValue = 512; }
        public static new Sha3_512 Create() => new Sha3_512();
        public override void Initialize() => _core.Reset();
        protected override void HashCore(byte[] array, int ibStart, int cbSize) => _core.Update(array, ibStart, cbSize);
        protected override byte[] HashFinal() => _core.Digest();
    }

    /// <summary>
    /// Creates a <see cref="HashAlgorithm"/> for a <see cref="DigestHashAlgorithm"/>.
    /// For SHA-3 the platform implementation is preferred when available
    /// (System.Security.Cryptography.SHA3_* — fast, hardware-backed); otherwise the
    /// own managed Keccak (<see cref="Sha3_256"/> etc.) is used so SHA-3 works on
    /// runtimes/OSes that do not provide it natively.
    /// </summary>
    public static class HashAlgorithmFactory
    {
        public static HashAlgorithm CreateHashAlgorithm(DigestHashAlgorithm algorithm)
        {
            switch (algorithm)
            {
                case DigestHashAlgorithm.Sha1: return SHA1.Create();
                case DigestHashAlgorithm.Sha256: return SHA256.Create();
                case DigestHashAlgorithm.Sha384: return SHA384.Create();
                case DigestHashAlgorithm.Sha512: return SHA512.Create();
                case DigestHashAlgorithm.Sha3_256: return SHA3_256.IsSupported ? (HashAlgorithm)SHA3_256.Create() : new Sha3_256();
                case DigestHashAlgorithm.Sha3_384: return SHA3_384.IsSupported ? (HashAlgorithm)SHA3_384.Create() : new Sha3_384();
                case DigestHashAlgorithm.Sha3_512: return SHA3_512.IsSupported ? (HashAlgorithm)SHA3_512.Create() : new Sha3_512();
                default:
                    throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, null);
            }
        }

        /// <summary>
        /// The digest OID for a <see cref="DigestHashAlgorithm"/>. SHA-1 keeps its
        /// legacy OIW OID; the SHA-2 and SHA-3 families live under the NIST arc
        /// (2.16.840.1.101.3.4.2.x). <see cref="DigestHashAlgorithm.Auto"/> resolves
        /// to SHA-256, the default digest everywhere else in the signer.
        /// </summary>
        public static string DigestHashAlgorithmToOid(DigestHashAlgorithm hashAlgorithm)
        {
            switch (hashAlgorithm)
            {
                case DigestHashAlgorithm.Sha1: return "1.3.14.3.2.26";
                case DigestHashAlgorithm.Auto:
                case DigestHashAlgorithm.Sha256: return "2.16.840.1.101.3.4.2.1";
                case DigestHashAlgorithm.Sha384: return "2.16.840.1.101.3.4.2.2";
                case DigestHashAlgorithm.Sha512: return "2.16.840.1.101.3.4.2.3";
                case DigestHashAlgorithm.Sha3_256: return "2.16.840.1.101.3.4.2.8";
                case DigestHashAlgorithm.Sha3_384: return "2.16.840.1.101.3.4.2.9";
                case DigestHashAlgorithm.Sha3_512: return "2.16.840.1.101.3.4.2.10";
                default:
                    throw new ArgumentOutOfRangeException(nameof(hashAlgorithm), hashAlgorithm, null);
            }
        }

        /// <summary>The inverse of <see cref="DigestHashAlgorithmToOid"/>; an
        /// unrecognised OID yields <see cref="DigestHashAlgorithm.Auto"/>.</summary>
        public static DigestHashAlgorithm OidToDigestHashAlgorithm(string hashAlgorithmOid)
        {
            switch (hashAlgorithmOid)
            {
                case "1.3.14.3.2.26": return DigestHashAlgorithm.Sha1;
                case "2.16.840.1.101.3.4.2.1": return DigestHashAlgorithm.Sha256;
                case "2.16.840.1.101.3.4.2.2": return DigestHashAlgorithm.Sha384;
                case "2.16.840.1.101.3.4.2.3": return DigestHashAlgorithm.Sha512;
                case "2.16.840.1.101.3.4.2.8": return DigestHashAlgorithm.Sha3_256;
                case "2.16.840.1.101.3.4.2.9": return DigestHashAlgorithm.Sha3_384;
                case "2.16.840.1.101.3.4.2.10": return DigestHashAlgorithm.Sha3_512;
                default: return DigestHashAlgorithm.Auto;
            }
        }
    }
}
