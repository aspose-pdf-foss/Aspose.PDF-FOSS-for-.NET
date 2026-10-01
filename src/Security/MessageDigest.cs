using System;
using System.Collections.Generic;
using System.IO;

namespace Aspose.Pdf.Security;

/// <summary>
/// A message digest fed in pieces: MD2 (RFC 1319), MD5, SHA-1, the SHA-2 family, RIPEMD-160,
/// SHA-3 and SHAKE (FIPS 202, SHAKE128 giving 32 octets and SHAKE256 64). <see cref="Finish"/>
/// returns the digest and starts over.
/// </summary>
internal abstract class MessageDigest
{
    /// <summary>The canonical name: MD2, MD5, SHA-1, SHA-224, SHA-256, SHA-384, SHA-512, RIPEMD160,
    /// SHA3-224, SHA3-256, SHA3-384, SHA3-512, SHAKE128 or SHAKE256.</summary>
    public abstract string Name { get; }

    /// <summary>The digest's length in octets.</summary>
    public abstract int Length { get; }

    public abstract void Update(byte[] data, int offset, int count);
    public abstract byte[] Finish();
    public abstract void Reset();

    /// <summary>A digest by its canonical name; null for one not known.</summary>
    public static MessageDigest? Create(string name) => Makers.TryGetValue(name, out var make) ? make() : null;

    /// <summary>The canonical names known.</summary>
    public static IEnumerable<string> Names => Makers.Keys;

    private static readonly Dictionary<string, Func<MessageDigest>> Makers = new()
    {
        ["MD2"] = () => new Md2(),
        ["MD5"] = () => new Buffered("MD5", 16, Md5Digest.Hash),
        ["SHA-1"] = () => new Buffered("SHA-1", 20, Compat.Sha1),
        ["SHA-224"] = () => new Buffered("SHA-224", 28, d => ShaDigest.Sha224(d, 0, d.Length)),
        ["SHA-256"] = () => new Buffered("SHA-256", 32, ShaDigest.Sha256),
        ["SHA-384"] = () => new Buffered("SHA-384", 48, ShaDigest.Sha384),
        ["SHA-512"] = () => new Buffered("SHA-512", 64, ShaDigest.Sha512),
        ["RIPEMD160"] = () => new Buffered("RIPEMD160", 20, Ripemd160Digest.Hash),
        ["SHA3-224"] = () => new Sponge("SHA3-224", Sha3Core.Sha3(28)),
        ["SHA3-256"] = () => new Sponge("SHA3-256", Sha3Core.Sha3(32)),
        ["SHA3-384"] = () => new Sponge("SHA3-384", Sha3Core.Sha3(48)),
        ["SHA3-512"] = () => new Sponge("SHA3-512", Sha3Core.Sha3(64)),
        ["SHAKE128"] = () => new Sponge("SHAKE128", Sha3Core.Shake(128, 32)),
        ["SHAKE256"] = () => new Sponge("SHAKE256", Sha3Core.Shake(256, 64)),
    };

    /// <summary>A digest over a one-shot hash: the pieces are kept until the end.</summary>
    private sealed class Buffered(string name, int length, Func<byte[], byte[]> hash) : MessageDigest
    {
        private readonly MemoryStream data = new();
        public override string Name => name;
        public override int Length => length;
        public override void Update(byte[] bytes, int offset, int count) => data.Write(bytes, offset, count);

        public override byte[] Finish()
        {
            var digest = hash(data.ToArray());
            Reset();
            return digest;
        }

        public override void Reset() => data.SetLength(0);
    }

    private sealed class Sponge(string name, Sha3Core core) : MessageDigest
    {
        public override string Name => name;
        public override int Length => core.DigestLength;
        public override void Update(byte[] data, int offset, int count) => core.Update(data, offset, count);
        public override byte[] Finish() => core.Digest();
        public override void Reset() => core.Reset();
    }

    /// <summary>MD2 (RFC 1319): a 16-octet checksum appended, then 18 rounds per 48-octet block
    /// over the substitution table built from the digits of pi.</summary>
    private sealed class Md2 : MessageDigest
    {
        private const int BlockSize = 16;
        private const int Rounds = 18;
        private readonly MemoryStream data = new();
        public override string Name => "MD2";
        public override int Length => BlockSize;
        public override void Update(byte[] bytes, int offset, int count) => data.Write(bytes, offset, count);
        public override void Reset() => data.SetLength(0);

        public override byte[] Finish()
        {
            var message = data.ToArray();
            Reset();
            var pad = BlockSize - message.Length % BlockSize;
            var padded = new byte[message.Length + pad + BlockSize];
            Array.Copy(message, padded, message.Length);
            for (var i = message.Length; i < message.Length + pad; i++) padded[i] = (byte)pad;
            var checksum = new byte[BlockSize];
            byte last = 0;
            for (var block = 0; block < message.Length + pad; block += BlockSize)
                for (var j = 0; j < BlockSize; j++)
                    last = checksum[j] ^= PiSubst[padded[block + j] ^ last];
            Array.Copy(checksum, 0, padded, message.Length + pad, BlockSize);
            var state = new byte[3 * BlockSize];
            for (var block = 0; block < padded.Length; block += BlockSize)
            {
                for (var j = 0; j < BlockSize; j++)
                {
                    state[BlockSize + j] = padded[block + j];
                    state[2 * BlockSize + j] = (byte)(state[BlockSize + j] ^ state[j]);
                }
                var t = 0;
                for (var round = 0; round < Rounds; round++)
                {
                    for (var k = 0; k < state.Length; k++) t = state[k] ^= PiSubst[t];
                    t = (t + round) & 0xFF;
                }
            }
            var digest = new byte[BlockSize];
            Array.Copy(state, digest, BlockSize);
            return digest;
        }

        // RFC 1319 section 3.2: a permutation of 0..255 built from the digits of pi.
        private static readonly byte[] PiSubst =
        [
            41, 46, 67, 201, 162, 216, 124, 1, 61, 54, 84, 161, 236, 240, 6, 19, 98, 167, 5, 243, 192, 199, 115, 140,
            152, 147, 43, 217, 188, 76, 130, 202, 30, 155, 87, 60, 253, 212, 224, 22, 103, 66, 111, 24, 138, 23, 229, 18,
            190, 78, 196, 214, 218, 158, 222, 73, 160, 251, 245, 142, 187, 47, 238, 122, 169, 104, 121, 145, 21, 178, 7, 63,
            148, 194, 16, 137, 11, 34, 95, 33, 128, 127, 93, 154, 90, 144, 50, 39, 53, 62, 204, 231, 191, 247, 151, 3,
            255, 25, 48, 179, 72, 165, 181, 209, 215, 94, 146, 42, 172, 86, 170, 198, 79, 184, 56, 210, 150, 164, 125, 182,
            118, 252, 107, 226, 156, 116, 4, 241, 69, 157, 112, 89, 100, 113, 135, 32, 134, 91, 207, 101, 230, 45, 168, 2,
            27, 96, 37, 173, 174, 176, 185, 246, 28, 70, 97, 105, 52, 64, 126, 15, 85, 71, 163, 35, 221, 81, 175, 58,
            195, 92, 249, 206, 186, 197, 234, 38, 44, 83, 13, 110, 133, 40, 132, 9, 211, 223, 205, 244, 65, 129, 77, 82,
            106, 220, 55, 200, 108, 193, 171, 250, 36, 225, 123, 8, 12, 189, 177, 74, 120, 136, 149, 139, 227, 99, 232, 109,
            233, 203, 213, 254, 59, 0, 29, 57, 242, 239, 183, 14, 102, 88, 208, 228, 166, 119, 114, 248, 235, 117, 75, 10,
            49, 68, 80, 180, 143, 237, 31, 26, 219, 153, 141, 51, 159, 17, 131, 20,
        ];
    }
}
