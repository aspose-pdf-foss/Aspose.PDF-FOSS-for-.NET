namespace Aspose.Pdf.IO.Filters;

internal static partial class Jbig2Encoder
{
    /// <summary>The adaptive states of a group of contexts: each context's QM state and its more
    /// probable symbol.</summary>
    private sealed class ContextSet(int size)
    {
        public readonly byte[] State = new byte[size];
        public readonly byte[] Mps = new byte[size];
    }

    /// <summary>The QM arithmetic encoder (T.88 Annex E.2), in the byte-out form with carry
    /// propagation into the byte already written. Several coders (generic region, integers,
    /// symbol IDs) share one, each with contexts of its own.</summary>
    private sealed class MqEncoder
    {
        private readonly List<byte> _bytes = [0];   // [0] stands before the data (BPST - 1)
        private uint _a = 0x8000;
        private uint _c;
        private int _ct = 12;

        public void Encode(ContextSet contexts, int context, bool bit)
        {
            var index = contexts.State[context];
            var qe = Jbig2QmStates.QeTable[index];
            _a -= qe;
            if ((bit ? 1 : 0) == contexts.Mps[context])
            {
                if ((_a & 0x8000) != 0)
                {
                    _c += qe;
                    return;
                }
                if (_a < qe) _a = qe;
                else _c += qe;
                contexts.State[context] = (byte)Jbig2QmStates.NmpsTable[index];
            }
            else
            {
                if (_a < qe) _c += qe;
                else _a = qe;
                if (Jbig2QmStates.SwitchTable[index] != 0) contexts.Mps[context] ^= 1;
                contexts.State[context] = (byte)Jbig2QmStates.NlpsTable[index];
            }
            do
            {
                _a <<= 1;
                _c <<= 1;
                if (--_ct == 0) ByteOut();
            } while ((_a & 0x8000) == 0);
        }

        private void ByteOut()
        {
            var last = _bytes.Count - 1;
            if (_bytes[last] == 0xFF)
            {
                Emit(20);
                return;
            }
            if ((_c & 0x8000000) != 0)
            {
                _bytes[last]++;
                if (_bytes[last] == 0xFF)
                {
                    _c &= 0x7FFFFFF;
                    Emit(20);
                    return;
                }
            }
            Emit(19);
        }

        // A byte after 0xFF carries seven bits (the stuffed bit keeps a marker out of the data).
        private void Emit(int shift)
        {
            _bytes.Add((byte)(_c >> shift));
            _c &= shift == 20 ? 0xFFFFFu : 0x7FFFFu;
            _ct = shift == 20 ? 7 : 8;
        }

        /// <summary>The coded bytes, flushed and followed by the 0xFF 0xAC terminator.</summary>
        public byte[] Finish()
        {
            var bound = _c + _a;
            _c |= 0xFFFF;
            if (_c >= bound) _c -= 0x8000;
            _c <<= _ct;
            ByteOut();
            _c <<= _ct;
            ByteOut();
            if (_bytes[_bytes.Count - 1] == 0xFF) _bytes.RemoveAt(_bytes.Count - 1);
            _bytes.Add(0xFF);
            _bytes.Add(0xAC);
            _bytes.RemoveAt(0);
            return _bytes.ToArray();
        }
    }

    /// <summary>A signed-integer coder (T.88 §A.2: IADT, IAFS, IADS, ...) with its 512 contexts;
    /// null codes the out-of-band value.</summary>
    private sealed class IntegerEncoder(MqEncoder coder)
    {
        private readonly ContextSet _contexts = new(512);
        private int _previous;

        // The prefix classes of a magnitude: its bound, the prefix bits after the sign, the offset
        // and how many bits the rest takes.
        private static readonly (int Upper, int Prefix, int PrefixBits, int Offset, int Bits)[] Classes =
        [
            (3, 0b0, 1, 0, 2),
            (19, 0b10, 2, 4, 4),
            (83, 0b110, 3, 20, 6),
            (339, 0b1110, 4, 84, 8),
            (4435, 0b11110, 5, 340, 12),
            (int.MaxValue, 0b11111, 5, 4436, 32),
        ];

        public void Encode(int? value)
        {
            _previous = 1;
            if (value is not { } v)
            {
                Bit(1);
                Bits(0b0, 1);
                Bits(0, 2);
                return;
            }
            var magnitude = Math.Abs((long)v);
            Bit(v < 0 ? 1 : 0);
            foreach (var (upper, prefix, prefixBits, offset, bits) in Classes)
            {
                if (magnitude > upper) continue;
                Bits(prefix, prefixBits);
                Bits(magnitude - offset, bits);
                return;
            }
        }

        private void Bits(long value, int count)
        {
            for (var i = count - 1; i >= 0; i--) Bit((int)((value >> i) & 1));
        }

        private void Bit(int bit)
        {
            coder.Encode(_contexts, _previous, bit != 0);
            _previous = _previous < 256 ? (_previous << 1) | bit : (((_previous << 1) | bit) & 511) | 256;
        }
    }

    /// <summary>The symbol-ID coder (T.88 §A.3, IAID): a fixed number of bits, each in the context of
    /// the bits before it.</summary>
    private sealed class IaidEncoder(MqEncoder coder, int bits)
    {
        private readonly ContextSet _contexts = new(1 << (bits + 1));

        public void Encode(int id)
        {
            var previous = 1;
            for (var i = bits - 1; i >= 0; i--)
            {
                var bit = (id >> i) & 1;
                coder.Encode(_contexts, previous, bit != 0);
                previous = (previous << 1) | bit;
            }
        }
    }
}
