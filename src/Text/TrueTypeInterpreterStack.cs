namespace Aspose.Pdf.Text;

internal sealed partial class TrueTypeInterpreter
{
    private sealed partial class Machine
    {
        private enum Rounding
        {
            ToGrid,
            ToHalfGrid,
            UpToGrid,
            DownToGrid,
            Off,
        }

        private const int GridMask = ~(One - 1);
        private const int HalfPixel = One / 2;

        private Rounding _rounding = Rounding.ToGrid;

        /// <summary>A distance rounded by the round state, its sign kept.</summary>
        private int Round(int distance)
        {
            var magnitude = Math.Abs(distance);
            var rounded = _rounding switch
            {
                Rounding.ToGrid => (magnitude + HalfPixel) & GridMask,
                Rounding.ToHalfGrid => (magnitude & GridMask) + HalfPixel,
                Rounding.UpToGrid => (magnitude + One - 1) & GridMask,
                Rounding.DownToGrid => magnitude & GridMask,
                _ => magnitude,
            };
            return distance < 0 ? -rounded : rounded;
        }

        /// <summary>The stack, arithmetic, comparison, storage and control value instructions;
        /// false for any other.</summary>
        private bool ExecuteStack(int op)
        {
            switch (op)
            {
                case 0x20: { var v = Pop(); Push(v); Push(v); return true; }                       // DUP
                case 0x21: Pop(); return true;                                                       // POP
                case 0x22: _top = 0; return true;                                                    // CLEAR
                case 0x23: { var a = Pop(); var b = Pop(); Push(a); Push(b); return true; }          // SWAP
                case 0x24: Push(_top); return true;                                                  // DEPTH
                case 0x25: { var k = Pop(); Push(_stack[Slot(k)]); return true; }                    // CINDEX
                case 0x26: MoveToTop(Pop()); return true;                                            // MINDEX
                case 0x8A: MoveToTop(3); return true;                                                // ROLL
                case 0x42: { var v = Pop(); _storage[Pop()] = v; return true; }                      // WS
                case 0x43: Push(_storage[Pop()]); return true;                                       // RS
                case 0x44: { var v = Pop(); _cvt[Pop()] = v; return true; }                          // WCVTP
                case 0x45: Push(_cvt[Pop()]); return true;                                           // RCVT
                case 0x70: { var v = Pop(); _cvt[Pop()] = v * One; return true; }                    // WCVTF
                case 0x4B: case 0x4C: Push(_font._pixelsPerEm); return true;                         // MPPEM, MPS
                case 0x56: Push((Round(Pop()) & (2 * One - 1)) == One ? 1 : 0); return true;         // ODD
                case 0x57: Push((Round(Pop()) & (2 * One - 1)) == 0 ? 1 : 0); return true;           // EVEN
                case 0x5C: Push(Pop() == 0 ? 1 : 0); return true;                                    // NOT
                case 0x64: Push(Math.Abs(Pop())); return true;                                       // ABS
                case 0x65: Push(-Pop()); return true;                                                // NEG
                case 0x66: Push(Pop() & GridMask); return true;                                      // FLOOR
                case 0x67: Push((Pop() + One - 1) & GridMask); return true;                          // CEILING
                case 0x68: case 0x69: case 0x6A: case 0x6B: Push(Round(Pop())); return true;         // ROUND
                case 0x6C: case 0x6D: case 0x6E: case 0x6F: return true;                             // NROUND
                default: return ExecuteBinary(op);
            }
        }

        /// <summary>The instructions that replace the two topmost values with one.</summary>
        private bool ExecuteBinary(int op)
        {
            if (op is not ((>= 0x50 and <= 0x55) or 0x5A or 0x5B or (>= 0x60 and <= 0x63) or 0x8B or 0x8C)) return false;
            var b = Pop();
            var a = Pop();
            Push(op switch
            {
                0x50 => a < b ? 1 : 0,                    // LT
                0x51 => a <= b ? 1 : 0,                   // LTEQ
                0x52 => a > b ? 1 : 0,                    // GT
                0x53 => a >= b ? 1 : 0,                   // GTEQ
                0x54 => a == b ? 1 : 0,                   // EQ
                0x55 => a != b ? 1 : 0,                   // NEQ
                0x5A => a != 0 && b != 0 ? 1 : 0,         // AND
                0x5B => a != 0 || b != 0 ? 1 : 0,         // OR
                0x60 => a + b,                            // ADD
                0x61 => a - b,                            // SUB
                0x62 => checked((int)((long)a * One / b)), // DIV
                0x63 => checked((int)((long)a * b / One)), // MUL
                0x8B => Math.Max(a, b),                   // MAX
                _ => Math.Min(a, b),                      // MIN
            });
            return true;
        }

        /// <summary>The stack slot of the k-th value from the top (1 is the top).</summary>
        private int Slot(int k)
        {
            if (k < 1 || k > _top) throw new Unsupported();
            return _top - k;
        }

        private void MoveToTop(int k)
        {
            var slot = Slot(k);
            var value = _stack[slot];
            Array.Copy(_stack, slot + 1, _stack, slot, _top - slot - 1);
            _stack[_top - 1] = value;
        }
    }
}
