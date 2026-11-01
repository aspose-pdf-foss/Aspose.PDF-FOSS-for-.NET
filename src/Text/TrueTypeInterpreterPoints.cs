namespace Aspose.Pdf.Text;

internal sealed partial class TrueTypeInterpreter
{
    private sealed partial class Machine
    {
        /// <summary>A unit vector component as the instructions exchange it (2.14 fixed point).</summary>
        private const double VectorUnit = 16384.0;

        /// <summary>Below this the freedom vector is taken as parallel to the projection vector,
        /// so a move never divides by a vanishing dot product (1/16).</summary>
        private const double MinFreedomDotProjection = 1.0 / 16;

        private const int GlyphZoneNumber = 1;
        private const int DefaultCutIn = One * 17 / 16;

        private double _projX = 1, _projY, _freeX = 1, _freeY;
        private int _rp0, _rp1, _rp2;
        private int _loop = 1;
        private int _minimumDistance = One;
        private int _cutIn = DefaultCutIn;
        private bool _autoFlip = true;

        private GlyphZone Zone => _zone ?? throw new Unsupported();

        private int Point(int index)
        {
            if (index < 0 || index >= Zone.CurX.Length) throw new Unsupported();
            return index;
        }

        private static int ToInt(double value) => checked((int)Math.Round(value, MidpointRounding.AwayFromZero));

        private int Project(int dx, int dy) => ToInt(dx * _projX + dy * _projY);

        private int ProjectCurrent(int p) => Project(Zone.CurX[p], Zone.CurY[p]);

        private int CurrentDistance(int p, int from) =>
            Project(Zone.CurX[p] - Zone.CurX[from], Zone.CurY[p] - Zone.CurY[from]);

        private int OriginalDistance(int p, int from) =>
            Project(Zone.OrgX[p] - Zone.OrgX[from], Zone.OrgY[p] - Zone.OrgY[from]);

        /// <summary>Move a point along the freedom vector so that its projection changes by
        /// <paramref name="distance"/>.</summary>
        private void Move(int p, int distance)
        {
            var dot = _freeX * _projX + _freeY * _projY;
            if (Math.Abs(dot) < MinFreedomDotProjection) dot = 1;
            Shift(p, distance * _freeX / dot, distance * _freeY / dot);
        }

        private void Shift(int p, double dx, double dy)
        {
            if (_freeX != 0)
            {
                Zone.CurX[p] += ToInt(dx);
                Zone.TouchedX[p] = true;
            }
            if (_freeY != 0)
            {
                Zone.CurY[p] += ToInt(dy);
                Zone.TouchedY[p] = true;
            }
        }

        private void SetVectors(double x, double y, bool projection, bool freedom)
        {
            var length = Math.Sqrt(x * x + y * y);
            if (length == 0) { x = 1; y = 0; length = 1; }
            if (projection) { _projX = x / length; _projY = y / length; }
            if (freedom) { _freeX = x / length; _freeY = y / length; }
        }

        /// <summary>The graphics state instructions; false for any other.</summary>
        private bool ExecuteState(int op)
        {
            switch (op)
            {
                case 0x00: case 0x01: SetVectors(op, 1 - op, true, true); return true;               // SVTCA
                case 0x02: case 0x03: SetVectors(op - 2, 3 - op, true, false); return true;          // SPVTCA
                case 0x04: case 0x05: SetVectors(op - 4, 5 - op, false, true); return true;          // SFVTCA
                case 0x06: case 0x07: case 0x08: case 0x09: SetVectorToLine(op); return true;        // SPVTL, SFVTL
                case 0x0A: case 0x0B: { var y = Pop(); SetVectors(Pop(), y, op == 0x0A, op == 0x0B); return true; } // SPVFS, SFVFS
                case 0x0C: Push(ToInt(_projX * VectorUnit)); Push(ToInt(_projY * VectorUnit)); return true; // GPV
                case 0x0D: Push(ToInt(_freeX * VectorUnit)); Push(ToInt(_freeY * VectorUnit)); return true; // GFV
                case 0x0E: _freeX = _projX; _freeY = _projY; return true;                            // SFVTPV
                case 0x10: _rp0 = Pop(); return true;                                                // SRP0
                case 0x11: _rp1 = Pop(); return true;                                                // SRP1
                case 0x12: _rp2 = Pop(); return true;                                                // SRP2
                case 0x13: case 0x14: case 0x15: case 0x16:                                          // SZP0-2, SZPS
                    if (Pop() != GlyphZoneNumber) throw new Unsupported();
                    return true;
                case 0x17: _loop = Pop(); return true;                                               // SLOOP
                case 0x18: _rounding = Rounding.ToGrid; return true;                                 // RTG
                case 0x19: _rounding = Rounding.ToHalfGrid; return true;                             // RTHG
                case 0x7A: _rounding = Rounding.Off; return true;                                    // ROFF
                case 0x7C: _rounding = Rounding.UpToGrid; return true;                               // RUTG
                case 0x7D: _rounding = Rounding.DownToGrid; return true;                             // RDTG
                case 0x1A: _minimumDistance = Pop(); return true;                                    // SMD
                case 0x1D: _cutIn = Pop(); return true;                                              // SCVTCI
                case 0x4D: _autoFlip = true; return true;                                            // FLIPON
                case 0x4E: _autoFlip = false; return true;                                           // FLIPOFF
                case 0x85: case 0x8D: Pop(); return true;                                            // SCANCTRL, SCANTYPE
                default: return false;
            }
        }

        /// <summary>Set the projection (0x06, 0x07) or freedom (0x08, 0x09) vector along the line
        /// through two points, or across it for the odd opcode.</summary>
        private void SetVectorToLine(int op)
        {
            var p2 = Point(Pop());
            var p1 = Point(Pop());
            double x = Zone.CurX[p1] - Zone.CurX[p2];
            double y = Zone.CurY[p1] - Zone.CurY[p2];
            if (x == 0 && y == 0) { x = 1; op &= ~1; }
            if ((op & 1) != 0) (x, y) = (-y, x);
            SetVectors(x, y, op < 0x08, op >= 0x08);
        }

        /// <summary>The instructions that read or move points; false for any other.</summary>
        private bool ExecutePoints(int op)
        {
            switch (op)
            {
                case 0x2E: case 0x2F: MoveDirectAbsolute(op == 0x2F); return true;                   // MDAP
                case 0x3E: case 0x3F: MoveIndirectAbsolute(op == 0x3F); return true;                 // MIAP
                case 0x3A: case 0x3B: MoveStackIndirectRelative(op == 0x3B); return true;            // MSIRP
                case 0x3C: ForEachLoopPoint(p => Move(p, -CurrentDistance(p, Point(_rp0)))); return true; // ALIGNRP
                case 0x39: InterpolatePoints(); return true;                                         // IP
                case 0x32: case 0x33: ShiftByReference(op == 0x33 ? _rp1 : _rp2); return true;       // SHP
                case 0x38: { var amount = Pop(); ForEachLoopPoint(p => Shift(p, amount * _freeX, amount * _freeY)); return true; } // SHPIX
                case 0x48: { var value = Pop(); var p = Point(Pop()); Move(p, value - ProjectCurrent(p)); return true; } // SCFS
                case 0x46: Push(ProjectCurrent(Point(Pop()))); return true;                          // GC, current
                case 0x47: { var p = Point(Pop()); Push(Project(Zone.OrgX[p], Zone.OrgY[p])); return true; } // GC, original
                case 0x49: case 0x4A:                                                                // MD
                {
                    var k = Point(Pop());
                    var l = Point(Pop());
                    Push(op == 0x49 ? CurrentDistance(l, k) : OriginalDistance(l, k));
                    return true;
                }
                case 0x30: Interpolate(Zone.CurY, Zone.OrgY, Zone.TouchedY); return true;            // IUP, y
                case 0x31: Interpolate(Zone.CurX, Zone.OrgX, Zone.TouchedX); return true;            // IUP, x
                default:
                    if (op < 0xC0) return false;
                    MoveRelative(op);                                                                // MDRP, MIRP
                    return true;
            }
        }

        private void ForEachLoopPoint(Action<int> move)
        {
            for (; _loop > 0; _loop--) move(Point(Pop()));
            _loop = 1;
        }

        private void MoveDirectAbsolute(bool round)
        {
            var p = Point(Pop());
            var at = ProjectCurrent(p);
            Move(p, round ? Round(at) - at : 0);
            _rp0 = _rp1 = p;
        }

        private void MoveIndirectAbsolute(bool round)
        {
            var distance = _cvt[Pop()];
            var p = Point(Pop());
            var at = ProjectCurrent(p);
            if (round)
            {
                if (Math.Abs(distance - at) > _cutIn) distance = at;
                distance = Round(distance);
            }
            Move(p, distance - at);
            _rp0 = _rp1 = p;
        }

        private void MoveStackIndirectRelative(bool setReference)
        {
            var distance = Pop();
            var p = Point(Pop());
            Move(p, distance - CurrentDistance(p, Point(_rp0)));
            _rp1 = _rp0;
            _rp2 = p;
            if (setReference) _rp0 = p;
        }

        private const int RelativeIndirect = 0x20;
        private const int RelativeSetsReference = 0x10;
        private const int RelativeKeepsMinimum = 0x08;
        private const int RelativeRounds = 0x04;

        /// <summary>MDRP and MIRP: put a point at a distance from rp0 - the original one, or a
        /// control value - rounded and held to the minimum distance as the opcode's bits ask.</summary>
        private void MoveRelative(int op)
        {
            var indirect = (op & RelativeIndirect) != 0;
            var entry = indirect ? Pop() : 0;
            var p = Point(Pop());
            var reference = Point(_rp0);
            var original = OriginalDistance(p, reference);
            var distance = original;
            if (indirect)
            {
                distance = entry == -1 ? 0 : _cvt[entry];
                if (_autoFlip && (original ^ distance) < 0) distance = -distance;
                if ((op & RelativeRounds) != 0 && Math.Abs(distance - original) > _cutIn) distance = original;
            }
            if ((op & RelativeRounds) != 0) distance = Round(distance);
            if ((op & RelativeKeepsMinimum) != 0)
            {
                if (original >= 0) distance = Math.Max(distance, _minimumDistance);
                else distance = Math.Min(distance, -_minimumDistance);
            }
            Move(p, distance - CurrentDistance(p, reference));
            _rp1 = _rp0;
            _rp2 = p;
            if ((op & RelativeSetsReference) != 0) _rp0 = p;
        }

        private void ShiftByReference(int reference)
        {
            reference = Point(reference);
            var moved = Project(Zone.CurX[reference] - Zone.OrgX[reference], Zone.CurY[reference] - Zone.OrgY[reference]);
            var dot = _freeX * _projX + _freeY * _projY;
            if (Math.Abs(dot) < MinFreedomDotProjection) dot = 1;
            double dx = moved * _freeX / dot, dy = moved * _freeY / dot;
            ForEachLoopPoint(p => Shift(p, dx, dy));
        }

        /// <summary>IP: keep each point's place between rp1 and rp2 as it was in the original outline.</summary>
        private void InterpolatePoints()
        {
            var from = Point(_rp1);
            var to = Point(_rp2);
            var oldRange = OriginalDistance(to, from);
            var newRange = CurrentDistance(to, from);
            ForEachLoopPoint(p =>
            {
                var original = OriginalDistance(p, from);
                var wanted = oldRange != 0 ? (int)((long)original * newRange / oldRange) : original;
                Move(p, wanted - CurrentDistance(p, from));
            });
        }

        /// <summary>IUP: move the points of each contour the program did not set on this axis
        /// along with the set ones around them.</summary>
        private void Interpolate(int[] cur, int[] org, bool[] touched)
        {
            var start = 0;
            foreach (var end in Zone.ContourEnds)
            {
                int first = -1, previous = -1;
                for (var i = start; i <= end; i++)
                {
                    if (!touched[i]) continue;
                    if (first < 0) first = i;
                    else InterpolateRun(cur, org, previous + 1, i - 1, previous, i);
                    previous = i;
                }
                if (first >= 0 && previous == first)
                {
                    var delta = cur[first] - org[first];
                    for (var i = start; i <= end; i++)
                        if (i != first) cur[i] = org[i] + delta;
                }
                else if (first >= 0)
                {
                    InterpolateRun(cur, org, previous + 1, end, previous, first);
                    InterpolateRun(cur, org, start, first - 1, previous, first);
                }
                start = end + 1;
            }
        }

        private static void InterpolateRun(int[] cur, int[] org, int from, int to, int ref1, int ref2)
        {
            if (from > to) return;
            if (org[ref1] > org[ref2]) (ref1, ref2) = (ref2, ref1);
            int o1 = org[ref1], o2 = org[ref2], c1 = cur[ref1], c2 = cur[ref2];
            for (var i = from; i <= to; i++)
            {
                var o = org[i];
                if (o <= o1) cur[i] = o + (c1 - o1);
                else if (o >= o2) cur[i] = o + (c2 - o2);
                else cur[i] = c1 + (int)((long)(o - o1) * (c2 - c1) / (o2 - o1));
            }
        }
    }
}
