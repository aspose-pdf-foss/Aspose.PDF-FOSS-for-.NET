namespace Aspose.Pdf.Text;

/// <summary>
/// Interprets a single Type 2 CharString to produce a flattened polygonal
/// glyph outline. All cubic Béziers are sampled to line segments; at typical
/// text-rendering sizes the difference from a true curve rasterizer is below
/// the AA threshold. Implements the operators from Adobe Technical Note #5177
/// used by real-world CFF-embedded PDF fonts — geometry, move/line/curve-to
/// variants, subroutine calls (local and global), flex, and endchar.
///
/// Hint operators (<c>hstem</c>, <c>vstem</c>, <c>hintmask</c>, etc.) are
/// skipped for width data but otherwise ignored: hints only drive display
/// refinement, not the geometric outline, so our grid-fitting-free rasterizer
/// has nothing to do with them.
/// </summary>
internal sealed class CffType2Interpreter
{
    private readonly byte[] _cffData;
    private readonly CffParser.IndexInfo _globalSubrs;
    private readonly CffParser.IndexInfo _localSubrs;
    private readonly int _globalSubrBias;
    private readonly int _localSubrBias;

    // Path build-up state.
    private readonly List<List<ContourPoint>> _contours = new();
    private List<ContourPoint>? _current;
    private double _x, _y;
    private double _xMin = double.MaxValue, _yMin = double.MaxValue;
    private double _xMax = double.MinValue, _yMax = double.MinValue;

    // Type 2 operand stack (Spec §3.1: up to 48 entries).
    private readonly double[] _stack = new double[96];
    private int _sp;

    // Hint bookkeeping: every hstem/vstem* adds pairs; hintmask & cntrmask each
    // consume ceil(hint_count / 8) bytes right after the operator token.
    private int _hintCount;
    private bool _widthSeen;

    // Runaway subroutine recursion guard — real charstrings stay under ~20 deep;
    // anything beyond that is almost certainly a malformed font.
    private int _subrDepth;

    public CffType2Interpreter(byte[] cffData, CffParser.IndexInfo globalSubrs,
        CffParser.IndexInfo localSubrs)
    {
        _cffData = cffData;
        _globalSubrs = globalSubrs;
        _localSubrs = localSubrs;
        _globalSubrBias = SubrBias(globalSubrs.count);
        _localSubrBias = SubrBias(localSubrs.count);
    }

    public GlyphOutline? Run(byte[] charString)
    {
        Interpret(charString);
        // endchar implicitly closes the last contour, but some malformed glyphs
        // are missing endchar — flush whatever's left so they still render.
        FlushCurrent();

        if (_contours.Count == 0) return null;
        var arr = new ContourPoint[_contours.Count][];
        for (var i = 0; i < _contours.Count; i++)
            arr[i] = _contours[i].ToArray();
        if (_xMin == double.MaxValue) { _xMin = _yMin = 0; _xMax = _yMax = 0; }
        return new GlyphOutline(arr, _xMin, _yMin, _xMax, _yMax);
    }

    // Type 2 subroutine-call bias: negative indices are normal; the actual index
    // is operand + bias. Bias depends on the number of subroutines in the INDEX.
    private static int SubrBias(int count) =>
        count < 1240 ? 107 : count < 33900 ? 1131 : 32768;

    private void Interpret(byte[] bytes)
    {
        var pos = 0;
        while (pos < bytes.Length)
        {
            var b0 = bytes[pos];

            // Operands: integer encodings (§3.2) and the 16.16 fixed-point form.
            if (b0 == 28)
            {
                if (pos + 2 >= bytes.Length) return;
                Push((short)((bytes[pos + 1] << 8) | bytes[pos + 2]));
                pos += 3; continue;
            }
            if (b0 >= 32 && b0 <= 246) { Push(b0 - 139); pos++; continue; }
            if (b0 >= 247 && b0 <= 250)
            {
                if (pos + 1 >= bytes.Length) return;
                Push((b0 - 247) * 256 + bytes[pos + 1] + 108);
                pos += 2; continue;
            }
            if (b0 >= 251 && b0 <= 254)
            {
                if (pos + 1 >= bytes.Length) return;
                Push(-(b0 - 251) * 256 - bytes[pos + 1] - 108);
                pos += 2; continue;
            }
            if (b0 == 255)
            {
                if (pos + 4 >= bytes.Length) return;
                var intPart = (short)((bytes[pos + 1] << 8) | bytes[pos + 2]);
                var fracPart = (bytes[pos + 3] << 8) | bytes[pos + 4];
                Push(intPart + fracPart / 65536.0);
                pos += 5; continue;
            }

            // Operators. b0 == 12 is the 2-byte escape prefix.
            if (b0 == 12)
            {
                if (pos + 1 >= bytes.Length) return;
                var op2 = bytes[pos + 1];
                pos += 2;
                RunEscapeOperator(op2);
                continue;
            }

            pos++; // single-byte operator

            switch (b0)
            {
                case 1:  // hstem
                case 3:  // vstem
                case 18: // hstemhm
                case 23: // vstemhm
                    CheckWidthStem();
                    _hintCount += _sp / 2;
                    _sp = 0;
                    break;

                case 19: // hintmask
                case 20: // cntrmask
                    CheckWidthStem();
                    _hintCount += _sp / 2; // trailing stems allowed before the mask
                    _sp = 0;
                    // mask is ceil(hintCount / 8) bytes immediately following the op
                    var maskBytes = (_hintCount + 7) / 8;
                    pos = Math.Min(bytes.Length, pos + maskBytes);
                    break;

                case 4:  // vmoveto (dy [optional width])
                    CheckWidthMove(1);
                    MoveTo(0, _stack[_sp - 1]);
                    _sp = 0;
                    break;

                case 22: // hmoveto (dx [optional width])
                    CheckWidthMove(1);
                    MoveTo(_stack[_sp - 1], 0);
                    _sp = 0;
                    break;

                case 21: // rmoveto (dx dy [optional width])
                    CheckWidthMove(2);
                    MoveTo(_stack[_sp - 2], _stack[_sp - 1]);
                    _sp = 0;
                    break;

                case 5:  // rlineto (dx1 dy1 dx2 dy2 … pairs)
                    for (var i = 0; i + 1 < _sp; i += 2)
                        LineTo(_stack[i], _stack[i + 1]);
                    _sp = 0;
                    break;

                case 6:  // hlineto (alternating h,v starting with h)
                    for (var i = 0; i < _sp; i++)
                    {
                        if ((i & 1) == 0) LineTo(_stack[i], 0);
                        else LineTo(0, _stack[i]);
                    }
                    _sp = 0;
                    break;

                case 7:  // vlineto (alternating v,h starting with v)
                    for (var i = 0; i < _sp; i++)
                    {
                        if ((i & 1) == 0) LineTo(0, _stack[i]);
                        else LineTo(_stack[i], 0);
                    }
                    _sp = 0;
                    break;

                case 8:  // rrcurveto (dxa dya dxb dyb dxc dyc … triples of points)
                    for (var i = 0; i + 5 < _sp; i += 6)
                        CurveTo(_stack[i], _stack[i + 1], _stack[i + 2], _stack[i + 3],
                                _stack[i + 4], _stack[i + 5]);
                    _sp = 0;
                    break;

                case 24: // rcurveline: N×rrcurveto followed by one rlineto
                    {
                        var curves = (_sp - 2) / 6;
                        var idx = 0;
                        for (var c = 0; c < curves; c++, idx += 6)
                            CurveTo(_stack[idx], _stack[idx + 1], _stack[idx + 2],
                                    _stack[idx + 3], _stack[idx + 4], _stack[idx + 5]);
                        if (idx + 1 < _sp) LineTo(_stack[idx], _stack[idx + 1]);
                        _sp = 0;
                    }
                    break;

                case 25: // rlinecurve: N×rlineto followed by one rrcurveto
                    {
                        var lines = (_sp - 6) / 2;
                        var idx = 0;
                        for (var l = 0; l < lines; l++, idx += 2)
                            LineTo(_stack[idx], _stack[idx + 1]);
                        if (idx + 5 < _sp)
                            CurveTo(_stack[idx], _stack[idx + 1], _stack[idx + 2],
                                    _stack[idx + 3], _stack[idx + 4], _stack[idx + 5]);
                        _sp = 0;
                    }
                    break;

                case 26: // vvcurveto (dx1? {dya dxb dyb dyc}+)
                    {
                        var i = 0;
                        var firstDx = 0.0;
                        if (_sp % 4 != 0) { firstDx = _stack[0]; i = 1; }
                        for (; i + 3 < _sp; i += 4)
                        {
                            var dx1 = firstDx; firstDx = 0;
                            CurveTo(dx1, _stack[i], _stack[i + 1], _stack[i + 2],
                                    0, _stack[i + 3]);
                        }
                        _sp = 0;
                    }
                    break;

                case 27: // hhcurveto (dy1? {dxa dxb dyb dxc}+)
                    {
                        var i = 0;
                        var firstDy = 0.0;
                        if (_sp % 4 != 0) { firstDy = _stack[0]; i = 1; }
                        for (; i + 3 < _sp; i += 4)
                        {
                            var dy1 = firstDy; firstDy = 0;
                            CurveTo(_stack[i], dy1, _stack[i + 1], _stack[i + 2],
                                    _stack[i + 3], 0);
                        }
                        _sp = 0;
                    }
                    break;

                case 30: // vhcurveto (alternating v-h curves)
                case 31: // hvcurveto (alternating h-v curves)
                    InterpretInterleavedCurves(b0 == 31);
                    _sp = 0;
                    break;

                case 10: // callsubr
                case 29: // callgsubr
                    {
                        if (_sp == 0) break;
                        var idx = (int)_stack[--_sp];
                        idx += (b0 == 10) ? _localSubrBias : _globalSubrBias;
                        var subrs = (b0 == 10) ? _localSubrs : _globalSubrs;
                        if (_subrDepth++ > 20) return;
                        var body = CffParser.ReadIndexEntry(_cffData, subrs, idx);
                        if (body.Length > 0) Interpret(body);
                        _subrDepth--;
                    }
                    break;

                case 11: // return — end subr, back to caller
                    return;

                case 14: // endchar — close last open contour, done with glyph
                    CheckWidthEndchar();
                    FlushCurrent();
                    return;

                default:
                    // Unknown / reserved opcode — drop operands and keep going
                    // rather than abort the glyph. Malformed CFF shouldn't crash.
                    _sp = 0;
                    break;
            }
        }
    }

    private void RunEscapeOperator(byte op)
    {
        switch (op)
        {
            case 34: // hflex
                // 7 operands: dx1 dx2 dy2 dx3 dx4 dx5 dx6 (flex is two cubic curves)
                if (_sp >= 7) EmitFlex(
                    _stack[0], 0, _stack[1], _stack[2], _stack[3], 0,
                    _stack[4], 0, _stack[5], -_stack[2], _stack[6], 0);
                _sp = 0;
                break;
            case 35: // flex — 12 coords + flex depth (ignored)
                if (_sp >= 12) EmitFlex(
                    _stack[0], _stack[1], _stack[2], _stack[3], _stack[4], _stack[5],
                    _stack[6], _stack[7], _stack[8], _stack[9], _stack[10], _stack[11]);
                _sp = 0;
                break;
            case 36: // hflex1
                if (_sp >= 9)
                {
                    var dy = _stack[1] + _stack[4];  // preserves y-alignment
                    var dyBack = -dy;
                    EmitFlex(
                        _stack[0], _stack[1], _stack[2], _stack[3], _stack[4], 0,
                        _stack[5], 0, _stack[6], _stack[7], _stack[8], dyBack);
                }
                _sp = 0;
                break;
            case 37: // flex1
                if (_sp >= 11)
                {
                    // Compute total dx, dy of the first 5 points to decide the
                    // closing coord (§4.5 — flex1 closes the loop axis-parallel).
                    double dx = _stack[0] + _stack[2] + _stack[4] + _stack[6] + _stack[8];
                    double dy = _stack[1] + _stack[3] + _stack[5] + _stack[7] + _stack[9];
                    double dx6, dy6;
                    if (Math.Abs(dx) > Math.Abs(dy)) { dx6 = _stack[10]; dy6 = -dy; }
                    else                             { dx6 = -dx; dy6 = _stack[10]; }
                    EmitFlex(
                        _stack[0], _stack[1], _stack[2], _stack[3], _stack[4], _stack[5],
                        _stack[6], _stack[7], _stack[8], _stack[9], dx6, dy6);
                }
                _sp = 0;
                break;
            default:
                _sp = 0; // other two-byte ops (math/storage) drop operands
                break;
        }
    }

    // Two cubic curves sharing an intermediate on-curve point.
    private void EmitFlex(double dx1, double dy1, double dx2, double dy2,
        double dx3, double dy3, double dx4, double dy4,
        double dx5, double dy5, double dx6, double dy6)
    {
        CurveTo(dx1, dy1, dx2, dy2, dx3, dy3);
        CurveTo(dx4, dy4, dx5, dy5, dx6, dy6);
    }

    // hvcurveto / vhcurveto — §4.4. The sequence alternates between two cubic
    // curve templates. `startH` picks which template goes first.
    private void InterpretInterleavedCurves(bool startH)
    {
        var i = 0;
        var h = startH;
        while (i + 3 < _sp)
        {
            var remain = _sp - i;
            var last = (remain == 5);  // odd tail => final curve gets an extra dyf/dxf

            if (h)
            {
                var dx1 = _stack[i];
                var dxb = _stack[i + 1]; var dyb = _stack[i + 2];
                var dyc = _stack[i + 3];
                var dxf = 0.0; var dyf = 0.0;
                if (last)
                {
                    // The 5th operand is the axis-offset of the curve endpoint.
                    dxf = _stack[i + 4]; // horizontal nudge on final vertical curve
                }
                CurveTo(dx1, 0, dxb, dyb, dxf, dyc + dyf);
                i += last ? 5 : 4;
            }
            else
            {
                var dy1 = _stack[i];
                var dxb = _stack[i + 1]; var dyb = _stack[i + 2];
                var dxc = _stack[i + 3];
                var dyf = 0.0;
                if (last)
                    dyf = _stack[i + 4];
                CurveTo(0, dy1, dxb, dyb, dxc + 0, dyf);
                i += last ? 5 : 4;
            }
            h = !h;
        }
    }

    // ── Path building ───────────────────────────────────────────────────

    private void MoveTo(double dx, double dy)
    {
        FlushCurrent();
        _x += dx; _y += dy;
        _current = new List<ContourPoint>();
        _current.Add(new ContourPoint(_x, _y, true));
        UpdateBbox(_x, _y);
    }

    private void LineTo(double dx, double dy)
    {
        _current ??= new List<ContourPoint> { new ContourPoint(_x, _y, true) };
        _x += dx; _y += dy;
        _current.Add(new ContourPoint(_x, _y, true));
        UpdateBbox(_x, _y);
    }

    // Cubic Bézier from current point through two off-curve control points to
    // an on-curve endpoint. Flattened to line segments here so downstream code
    // can treat the outline as a straight-edge polygon.
    private void CurveTo(double dx1, double dy1, double dx2, double dy2, double dx3, double dy3)
    {
        var p0x = _x; var p0y = _y;
        var p1x = p0x + dx1; var p1y = p0y + dy1;
        var p2x = p1x + dx2; var p2y = p1y + dy2;
        var p3x = p2x + dx3; var p3y = p2y + dy3;

        _current ??= new List<ContourPoint> { new ContourPoint(p0x, p0y, true) };
        FlattenCubic(p0x, p0y, p1x, p1y, p2x, p2y, p3x, p3y, 0);

        _x = p3x; _y = p3y;
    }

    // Adaptive De Casteljau flattening — stop once the control polygon is close
    // enough to a straight line. Tolerance is in font design units; at 1000-upem
    // glyph sizes, 1.0 means roughly 1/upem of em, well below rasterisation noise.
    private void FlattenCubic(double x0, double y0, double x1, double y1,
        double x2, double y2, double x3, double y3, int depth)
    {
        const double Flatness = 0.5;
        if (depth > 16)
        {
            AppendOnCurve(x3, y3);
            return;
        }

        var ax = x3 - x0; var ay = y3 - y0;
        var len2 = ax * ax + ay * ay;
        // Distance of each control point from the p0→p3 line, squared & area-scaled.
        var d1 = (x1 - x3) * ay - (y1 - y3) * ax;
        var d2 = (x2 - x3) * ay - (y2 - y3) * ax;
        if ((d1 * d1 + d2 * d2) <= Flatness * Flatness * len2 || len2 < 1e-6)
        {
            AppendOnCurve(x3, y3);
            return;
        }

        var m01x = (x0 + x1) * 0.5; var m01y = (y0 + y1) * 0.5;
        var m12x = (x1 + x2) * 0.5; var m12y = (y1 + y2) * 0.5;
        var m23x = (x2 + x3) * 0.5; var m23y = (y2 + y3) * 0.5;
        var m012x = (m01x + m12x) * 0.5; var m012y = (m01y + m12y) * 0.5;
        var m123x = (m12x + m23x) * 0.5; var m123y = (m12y + m23y) * 0.5;
        var mx = (m012x + m123x) * 0.5; var my = (m012y + m123y) * 0.5;

        FlattenCubic(x0, y0, m01x, m01y, m012x, m012y, mx, my, depth + 1);
        FlattenCubic(mx, my, m123x, m123y, m23x, m23y, x3, y3, depth + 1);
    }

    private void AppendOnCurve(double x, double y)
    {
        _current ??= new List<ContourPoint>();
        _current.Add(new ContourPoint(x, y, true));
        UpdateBbox(x, y);
    }

    private void FlushCurrent()
    {
        if (_current is not null && _current.Count >= 2)
            _contours.Add(_current);
        _current = null;
    }

    private void UpdateBbox(double x, double y)
    {
        if (x < _xMin) _xMin = x;
        if (y < _yMin) _yMin = y;
        if (x > _xMax) _xMax = x;
        if (y > _yMax) _yMax = y;
    }

    // ── Stack & width helpers ───────────────────────────────────────────

    private void Push(double v)
    {
        if (_sp < _stack.Length) _stack[_sp++] = v;
    }

    // stem operators: odd operand count means a leading width operand.
    private void CheckWidthStem()
    {
        if (_widthSeen) return;
        _widthSeen = true;
        if ((_sp & 1) != 0)
        {
            // Leading operand is the width — drop it; nothing to emit for geometry.
            for (var i = 0; i < _sp - 1; i++) _stack[i] = _stack[i + 1];
            _sp--;
        }
    }

    // move operators: expected takes `expected` operands; count == expected+1 means
    // the first operand is the optional width.
    private void CheckWidthMove(int expected)
    {
        if (_widthSeen) return;
        _widthSeen = true;
        if (_sp == expected + 1)
        {
            for (var i = 0; i < _sp - 1; i++) _stack[i] = _stack[i + 1];
            _sp--;
        }
    }

    // endchar: 0 operands normally, 1 means width, 4 means seac (deprecated; ignore).
    private void CheckWidthEndchar()
    {
        if (_widthSeen) return;
        _widthSeen = true;
        if (_sp == 1) _sp = 0;
    }
}
