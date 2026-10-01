namespace Aspose.Pdf.IO.Filters.Brotli;

internal static partial class BrotliDecoder
{
    /// <summary>One decode: the bits, the output so far and the four last distances.</summary>
    private sealed class State
    {
        private readonly BrotliBitReader _bits;
        private readonly Options _options;
        private readonly byte[] _history;
        private readonly int[] _distances = { 16, 15, 11, 4 };
        private int _distanceIndex = 3;
        private long _maxBackwardDistance;
        private bool _largeWindow;

        internal readonly GrowableBytes Output = new();

        internal State(BrotliBitReader bits, Options options)
        {
            _bits = bits;
            _options = options;
            var history = new System.IO.MemoryStream();
            foreach (var chunk in options.Dictionary) history.Write(chunk, 0, chunk.Length);
            _history = history.ToArray();
        }

        internal void Run()
        {
            var windowBits = WindowBits(_bits, _options.LargeWindow, out _largeWindow);
            if (windowBits < 0) throw new BrotliDecodeException(BrotliErrors.InvalidWindowBits);
            _maxBackwardDistance = (1L << windowBits) - WindowGap;
            while (true)
            {
                var last = _bits.ReadBits(1) == 1;
                if (last && _bits.ReadBits(1) == 1) break;
                var nibbles = _bits.ReadBits(2) + 4;
                if (nibbles == 7)
                {
                    SkipMetadata();
                    if (last) break;
                    continue;
                }
                var length = 0;
                for (var i = 0; i < nibbles; i++)
                {
                    var nibble = _bits.ReadBits(4);
                    if (nibble == 0 && i + 1 == nibbles && nibbles > 4) throw new BrotliDecodeException(BrotliErrors.ExuberantNibble);
                    length |= nibble << (i * 4);
                }
                length++;
                var uncompressed = !last && _bits.ReadBits(1) == 1;
                if (uncompressed) CopyStored(length);
                else DecodeCompressed(length);
                _bits.CheckNotPastEnd();
                if (last) break;
            }
            _bits.JumpToByteBoundary();
            _bits.CheckAtEnd();
        }

        // A metadata block: reserved bit, the length in bytes, then that many bytes skipped.
        private void SkipMetadata()
        {
            if (_bits.ReadBits(1) != 0) throw new BrotliDecodeException(BrotliErrors.CorruptedReservedBit);
            var sizeBytes = _bits.ReadBits(2);
            var length = 0;
            for (var i = 0; i < sizeBytes; i++)
            {
                var b = _bits.ReadBits(8);
                if (b == 0 && i + 1 == sizeBytes && sizeBytes > 1) throw new BrotliDecodeException(BrotliErrors.ExuberantNibble);
                length |= b << (i * 8);
            }
            if (sizeBytes > 0) length++;
            _bits.JumpToByteBoundary();
            for (var i = 0; i < length; i++) _bits.ReadByteAligned();
        }

        private void CopyStored(int length)
        {
            _bits.JumpToByteBoundary();
            for (var i = 0; i < length; i++) Output.Add((byte)_bits.ReadByteAligned());
        }

        /// <summary>A compressed meta-block: its block types, context maps and prefix codes,
        /// then commands until its length is produced.</summary>
        private void DecodeCompressed(int length)
        {
            var blocks = new BlockSwitch[3];
            for (var i = 0; i < 3; i++) blocks[i] = BlockSwitch.Read(_bits);
            var postfixBits = _bits.ReadBits(2);
            var directCodes = _bits.ReadBits(4) << postfixBits;
            var contextModes = new int[blocks[0].Types];
            for (var i = 0; i < contextModes.Length; i++) contextModes[i] = _bits.ReadBits(2);
            var literalMap = ReadContextMap(_bits, blocks[0].Types << LiteralContextBits, out var literalTrees);
            var distanceMap = ReadContextMap(_bits, blocks[2].Types << DistanceContextBits, out var distanceTrees);
            var literalCodes = ReadCodes(literalTrees, NumLiteralCodes, NumLiteralCodes);
            var commandCodes = ReadCodes(blocks[1].Types, NumCommandCodes, NumCommandCodes);
            var distanceAlphabet = 16 + directCodes + ((2 * (_largeWindow ? MaxLargeDistanceBits : MaxDistanceBits)) << postfixBits);
            var distanceCodes = ReadCodes(distanceTrees, distanceAlphabet, distanceAlphabet);

            var remaining = length;
            while (remaining > 0)
            {
                var command = commandCodes[blocks[1].Next(_bits)].Read(_bits);
                var cell = command >> 6;
                var implicitDistance = cell < 2;
                int insertCode, copyCode;
                if (implicitDistance)
                {
                    insertCode = (command >> 3) & 7;
                    copyCode = (cell == 1 ? 8 : 0) + (command & 7);
                }
                else
                {
                    insertCode = ExplicitInsertBase[cell - 2] + ((command >> 3) & 7);
                    copyCode = ExplicitCopyBase[cell - 2] + (command & 7);
                }
                var insertLength = InsertLengthOffset[insertCode] + _bits.ReadBits(InsertLengthBits[insertCode]);
                var copyLength = CopyLengthOffset[copyCode] + _bits.ReadBits(CopyLengthBits[copyCode]);

                for (var j = 0; j < insertLength; j++)
                {
                    var type = blocks[0].Next(_bits);
                    var mode = contextModes[type];
                    var p1 = Output.Count > 0 ? Output[Output.Count - 1] : 0;
                    var p2 = Output.Count > 1 ? Output[Output.Count - 2] : 0;
                    var context = BrotliData.ContextLookup[mode * 512 + p1] | BrotliData.ContextLookup[mode * 512 + 256 + p2];
                    var tree = literalMap[(type << LiteralContextBits) + context];
                    Output.Add((byte)literalCodes[tree].Read(_bits));
                    if (--remaining == 0) return;
                }

                var pushes = false;
                var distance = implicitDistance
                    ? _distances[_distanceIndex & 3]
                    : ReadDistance(blocks[2], distanceMap, distanceCodes, copyLength, postfixBits, directCodes, out pushes);
                var maxDistance = System.Math.Min(_maxBackwardDistance, Output.Count + (long)_history.Length);
                if (distance > maxDistance)
                {
                    var word = BrotliDictionary.Word(copyLength, (int)(distance - maxDistance - 1));
                    if (word == null || word.Length > remaining) throw new BrotliDecodeException(BrotliErrors.InvalidBackwardReference);
                    foreach (var b in word) Output.Add(b);
                    remaining -= word.Length;
                    continue;
                }
                if (pushes)
                {
                    _distanceIndex++;
                    _distances[_distanceIndex & 3] = (int)distance;
                }
                if (copyLength > remaining) throw new BrotliDecodeException(BrotliErrors.InvalidBackwardReference);
                for (var j = 0; j < copyLength; j++)
                {
                    var from = Output.Count - distance;
                    Output.Add(from >= 0 ? Output[(int)from] : _history[_history.Length + (int)from]);
                }
                remaining -= copyLength;
            }
        }

        // The command cells after the two with an implied distance: insert and copy code bases.
        private static readonly int[] ExplicitInsertBase = { 0, 0, 8, 8, 0, 16, 8, 16, 16 };
        private static readonly int[] ExplicitCopyBase = { 0, 8, 0, 8, 16, 0, 16, 8, 16 };

        // A coded distance: the distance code, then a recent distance adjusted, a direct
        // distance, or one built from extra bits. Every code but 0 enters the ring of recent ones.
        private long ReadDistance(BlockSwitch block, byte[] map, BrotliPrefixCode[] codes, int copyLength, int postfixBits, int directCodes, out bool pushes)
        {
            var type = block.Next(_bits);
            var context = copyLength > 4 ? 3 : copyLength - 2;
            var code = codes[map[(type << DistanceContextBits) + context]].Read(_bits);
            pushes = code != 0;
            if (code < 16)
            {
                var distance = _distances[(_distanceIndex - DistanceShortIndex[code]) & 3] + DistanceShortDelta[code];
                if (distance <= 0) throw new BrotliDecodeException(BrotliErrors.NegativeDistance);
                return distance;
            }
            if (code < 16 + directCodes) return code - 15;
            var postfixMask = (1 << postfixBits) - 1;
            var d = code - directCodes - 16;
            var extraBits = 1 + (d >> (postfixBits + 1));
            var high = d >> postfixBits;
            var low = d & postfixMask;
            var offset = ((2L + (high & 1)) << extraBits) - 4;
            return ((offset + _bits.ReadBits(extraBits)) << postfixBits) + low + directCodes + 1;
        }

        private BrotliPrefixCode[] ReadCodes(int count, int alphabet, int limit)
        {
            var codes = new BrotliPrefixCode[count];
            for (var i = 0; i < count; i++) codes[i] = BrotliPrefixCode.ReadFrom(_bits, alphabet, limit);
            return codes;
        }
    }

    /// <summary>
    /// The block types of one category (literals, commands, distances): how many, the codes for
    /// the next type and the next block's length, and the ring of the last two types.
    /// </summary>
    private sealed class BlockSwitch
    {
        internal int Types;
        private BrotliPrefixCode? _typeCode;
        private BrotliPrefixCode? _lengthCode;
        private int _remaining = 1 << 28;
        private int _previous = 1;
        private int _current;

        internal static BlockSwitch Read(BrotliBitReader bits)
        {
            var block = new BlockSwitch { Types = ReadVarLenUint8(bits) + 1 };
            if (block.Types < 2) return block;
            block._typeCode = BrotliPrefixCode.ReadFrom(bits, block.Types + 2, block.Types + 2);
            block._lengthCode = BrotliPrefixCode.ReadFrom(bits, NumBlockLengthCodes, NumBlockLengthCodes);
            block._remaining = ReadBlockLength(bits, block._lengthCode);
            return block;
        }

        /// <summary>The type the next item belongs to, switching blocks when one runs out.</summary>
        internal int Next(BrotliBitReader bits)
        {
            if (_remaining == 0)
            {
                var type = _typeCode!.Read(bits);
                _remaining = ReadBlockLength(bits, _lengthCode!);
                type = type switch
                {
                    0 => _previous,
                    1 => _current + 1,
                    _ => type - 2,
                };
                if (type >= Types) type -= Types;
                _previous = _current;
                _current = type;
            }
            _remaining--;
            return _current;
        }
    }

    /// <summary>A byte list that indexes cheaply.</summary>
    internal sealed class GrowableBytes
    {
        private byte[] _data = new byte[4096];

        internal int Count { get; private set; }

        internal byte this[int index] => _data[index];

        internal byte this[long index] => _data[index];

        internal void Add(byte value)
        {
            if (Count == _data.Length) System.Array.Resize(ref _data, _data.Length * 2);
            _data[Count++] = value;
        }

        internal byte[] ToArray()
        {
            var result = new byte[Count];
            System.Array.Copy(_data, result, Count);
            return result;
        }
    }
}
