namespace Aspose.Pdf.Text;

/// <summary>
/// Runs the TrueType instructions of a glyph whose shape depends on them: some faces store a
/// composite glyph's strokes at offsets that are not final and let the glyph's program put each
/// stroke in place. The interpreter covers the instructions such programs use - the stack,
/// arithmetic, control flow, the control value table, the storage area and the point moves in
/// the glyph zone - at one pixel per font unit, and stops with <see cref="Unsupported"/> on
/// anything else (the twilight zone, deltas, instruction definitions), so the caller can fall
/// back to the outline as stored.
/// </summary>
internal sealed partial class TrueTypeInterpreter
{
    /// <summary>Raised when a program needs what the interpreter does not implement or
    /// addresses something out of range.</summary>
    internal sealed class Unsupported : Exception
    {
    }

    /// <summary>One pixel, and one font unit at the size the programs run at, in 26.6 units.</summary>
    internal const int One = 64;

    private const int StackSize = 8192;
    private const int MinStorage = 1024;
    private const int MaxCallDepth = 64;
    private const int MaxSteps = 2_000_000;

    private readonly Dictionary<int, (byte[] Code, int Start, int End)> _functions = new();
    private readonly int[] _cvt;
    private readonly int[] _storage;
    private readonly int _pixelsPerEm;

    private TrueTypeInterpreter(int[] cvt, int pixelsPerEm)
    {
        _cvt = cvt;
        _storage = new int[MinStorage];
        _pixelsPerEm = pixelsPerEm;
    }

    /// <summary>Prepare a font: its function definitions and the control values and storage its
    /// control program leaves. Null when either program cannot be run.</summary>
    /// <param name="fontProgram">The fpgm table.</param>
    /// <param name="controlProgram">The prep table, or an empty array.</param>
    /// <param name="controlValues">The cvt table's values in font units.</param>
    /// <param name="unitsPerEm">The em, which is also the size in pixels the programs run at.</param>
    public static TrueTypeInterpreter? TryCreate(byte[] fontProgram, byte[] controlProgram, int[] controlValues, int unitsPerEm)
    {
        var cvt = new int[controlValues.Length];
        for (var i = 0; i < cvt.Length; i++) cvt[i] = controlValues[i] * One;
        var font = new TrueTypeInterpreter(cvt, unitsPerEm);
        try
        {
            new Machine(font, font._cvt, font._storage, null).Run(fontProgram, 0, fontProgram.Length, 0);
            new Machine(font, font._cvt, font._storage, null).Run(controlProgram, 0, controlProgram.Length, 0);
            return font;
        }
        catch (Exception e) when (e is Unsupported or IndexOutOfRangeException or DivideByZeroException or OverflowException)
        {
            return null;
        }
    }

    /// <summary>Run a glyph program over the glyph's points; false when it cannot be run, the
    /// points then being in an undefined state.</summary>
    public bool TryRun(byte[] code, int start, int length, GlyphZone zone)
    {
        try
        {
            new Machine(this, (int[])_cvt.Clone(), (int[])_storage.Clone(), zone).Run(code, start, start + length, 0);
            return true;
        }
        catch (Exception e) when (e is Unsupported or IndexOutOfRangeException or DivideByZeroException or OverflowException)
        {
            return false;
        }
    }

    /// <summary>The points a glyph program works on, in 26.6 units: where each stood before
    /// the program and where it stands now, and which axes the program has set.</summary>
    internal sealed class GlyphZone
    {
        public GlyphZone(int[] x, int[] y, int[] contourEnds)
        {
            OrgX = x;
            OrgY = y;
            CurX = (int[])x.Clone();
            CurY = (int[])y.Clone();
            TouchedX = new bool[x.Length];
            TouchedY = new bool[x.Length];
            ContourEnds = contourEnds;
        }

        public int[] OrgX { get; }
        public int[] OrgY { get; }
        public int[] CurX { get; }
        public int[] CurY { get; }
        public bool[] TouchedX { get; }
        public bool[] TouchedY { get; }

        /// <summary>The index of each contour's last point; the phantom points follow the last.</summary>
        public int[] ContourEnds { get; }
    }

    /// <summary>How many bytes the instruction at <paramref name="ip"/> takes, its pushed data included.</summary>
    private static int InstructionLength(byte[] code, int ip)
    {
        int op = code[ip];
        if (op == OpNPushB) return ip + 1 < code.Length ? 2 + code[ip + 1] : 2;
        if (op == OpNPushW) return ip + 1 < code.Length ? 2 + 2 * code[ip + 1] : 2;
        if (op >= OpPushB && op < OpPushW) return 2 + (op - OpPushB);
        if (op >= OpPushW && op <= OpPushWLast) return 1 + 2 * (op - OpPushW + 1);
        return 1;
    }

    private const int OpNPushB = 0x40;
    private const int OpNPushW = 0x41;
    private const int OpPushB = 0xB0;
    private const int OpPushW = 0xB8;
    private const int OpPushWLast = 0xBF;
    private const int OpIf = 0x58;
    private const int OpElse = 0x1B;
    private const int OpEndIf = 0x59;
    private const int OpJumpRelative = 0x1C;
    private const int OpJumpOnTrue = 0x78;
    private const int OpJumpOnFalse = 0x79;
    private const int OpCall = 0x2B;
    private const int OpLoopCall = 0x2A;
    private const int OpFunctionDef = 0x2C;
    private const int OpEndFunction = 0x2D;

    /// <summary>One run of one program: the stack and the graphics state.</summary>
    private sealed partial class Machine
    {
        private readonly TrueTypeInterpreter _font;
        private readonly int[] _cvt;
        private readonly int[] _storage;
        private readonly GlyphZone? _zone;
        private readonly int[] _stack = new int[StackSize];
        private int _top;
        private int _steps;

        public Machine(TrueTypeInterpreter font, int[] cvt, int[] storage, GlyphZone? zone)
        {
            _font = font;
            _cvt = cvt;
            _storage = storage;
            _zone = zone;
        }

        private void Push(int value)
        {
            if (_top >= _stack.Length) throw new Unsupported();
            _stack[_top++] = value;
        }

        private int Pop()
        {
            if (_top <= 0) throw new Unsupported();
            return _stack[--_top];
        }

        public void Run(byte[] code, int ip, int end, int depth)
        {
            if (depth > MaxCallDepth) throw new Unsupported();
            end = Math.Min(end, code.Length);
            while (ip < end)
            {
                if (++_steps > MaxSteps) throw new Unsupported();
                int op = code[ip];
                var next = ip + InstructionLength(code, ip);
                if (next > end) throw new Unsupported();
                if (op == OpNPushB || op == OpNPushW || (op >= OpPushB && op <= OpPushWLast)) PushData(code, ip, op);
                else if (op == OpIf) { if (Pop() == 0) next = SkipBranch(code, next, end, stopAtElse: true); }
                else if (op == OpElse) next = SkipBranch(code, next, end, stopAtElse: false);
                else if (op == OpEndIf) { /* the end of a branch that ran */ }
                else if (op == OpJumpRelative) next = ip + Pop();
                else if (op == OpJumpOnTrue || op == OpJumpOnFalse)
                {
                    var condition = Pop() != 0;
                    var offset = Pop();
                    if (condition == (op == OpJumpOnTrue)) next = ip + offset;
                }
                else if (op == OpCall) Call(Pop(), 1, depth);
                else if (op == OpLoopCall) { var function = Pop(); Call(function, Pop(), depth); }
                else if (op == OpFunctionDef) next = DefineFunction(code, next, end);
                else if (op == OpEndFunction) return;
                else if (!ExecuteStack(op) && !ExecuteState(op) && !ExecutePoints(op)) throw new Unsupported();
                if (next < 0) throw new Unsupported();
                ip = next;
            }
        }

        private void Call(int function, int times, int depth)
        {
            if (!_font._functions.TryGetValue(function, out var f)) throw new Unsupported();
            for (var i = 0; i < times; i++) Run(f.Code, f.Start, f.End, depth + 1);
        }

        /// <summary>Record the function that starts at <paramref name="ip"/>; returns where the
        /// program goes on, past its end.</summary>
        private int DefineFunction(byte[] code, int ip, int end)
        {
            var number = Pop();
            for (var p = ip; p < end; p += InstructionLength(code, p))
            {
                if (code[p] != OpEndFunction) continue;
                _font._functions[number] = (code, ip, p);
                return p + 1;
            }
            throw new Unsupported();
        }

        /// <summary>Skip a branch that is not taken: to past its ELSE (when asked) or its EIF.</summary>
        private static int SkipBranch(byte[] code, int ip, int end, bool stopAtElse)
        {
            var nested = 0;
            for (var p = ip; p < end; p += InstructionLength(code, p))
            {
                int op = code[p];
                if (op == OpIf) nested++;
                else if (op == OpEndIf)
                {
                    if (nested == 0) return p + 1;
                    nested--;
                }
                else if (op == OpElse && nested == 0 && stopAtElse) return p + 1;
            }
            throw new Unsupported();
        }

        private void PushData(byte[] code, int ip, int op)
        {
            int count, at;
            bool words;
            if (op == OpNPushB || op == OpNPushW)
            {
                count = code[ip + 1];
                at = ip + 2;
                words = op == OpNPushW;
            }
            else
            {
                words = op >= OpPushW;
                count = op - (words ? OpPushW : OpPushB) + 1;
                at = ip + 1;
            }
            for (var i = 0; i < count; i++)
            {
                if (words) Push((short)((code[at + 2 * i] << 8) | code[at + 2 * i + 1]));
                else Push(code[at + i]);
            }
        }
    }
}
