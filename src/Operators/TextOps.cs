// PDF content stream operators — PDF32000_2008 §8–9
using System.Globalization;
using System.Text;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Operators;

/// <summary>BT — Begin text object.</summary>
public sealed class BT : BlockTextOperator
{
    public override string ToPdf() => "BT";
    public override string ToString() => ToPdf();
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>ET — End text object.</summary>
public sealed class ET : BlockTextOperator
{
    public override string ToPdf() => "ET";
    public override string ToString() => ToPdf();
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>Tf — Select font and size.</summary>
public sealed class SelectFont : TextStateOperator
{
    public string FontName { get; }
    public double Size { get; }
    /// <summary>Public-API-shape alias for <see cref="FontName"/>.</summary>
    public string Name => FontName;

    public SelectFont(string resName, double size) { FontName = resName; Size = size; }
    public override string ToPdf() => $"/{FontName} {Fmt(Size)} Tf";
    public override string ToString() => ToPdf();
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>Tm — Set text matrix.</summary>
public sealed class SetTextMatrix : TextPlaceOperator
{
    public double A { get; }
    public double B { get; }
    public double C { get; }
    public double D { get; }
    public double E { get; }
    public double F { get; }

    private Aspose.Pdf.Matrix _matrix;

    /// <summary>The transform as a <see cref="Aspose.Pdf.Matrix"/>. Setting it
    /// replaces the cached matrix only; A..F field values are unchanged because
    /// they're declared get-only at the type level.</summary>
    public Aspose.Pdf.Matrix Matrix
    {
        get => _matrix ?? new Aspose.Pdf.Matrix(A, B, C, D, E, F);
        set => _matrix = value;
    }

    public SetTextMatrix(double a, double b, double c, double d, double e, double f)
    {
        A = a; B = b; C = c; D = d; E = e; F = f;
        _matrix = new Aspose.Pdf.Matrix(a, b, c, d, e, f);
    }

    public SetTextMatrix(Aspose.Pdf.Matrix m)
        : this(m.A, m.B, m.C, m.D, m.E, m.F) { }

    public override string ToPdf() => $"{Fmt(A)} {Fmt(B)} {Fmt(C)} {Fmt(D)} {Fmt(E)} {Fmt(F)} Tm";
    public override string ToString() => ToPdf();
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>Td — Move text position: translate text origin by (X, Y).</summary>
public sealed class MoveTextPosition : TextPlaceOperator
{
    public double X { get; set; }
    public double Y { get; set; }

    public MoveTextPosition(double x, double y) { X = x; Y = y; }

    public override string ToPdf() => $"{Fmt(X)} {Fmt(Y)} Td";
    public override string ToString() => ToPdf();
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>TD — Move text position and set leading: translate by (X, Y) and set leading to -Y.</summary>
public sealed class MoveTextPositionSetLeading : TextPlaceOperator
{
    public double X { get; set; }
    public double Y { get; set; }

    public MoveTextPositionSetLeading(double x, double y) { X = x; Y = y; }

    public override string ToPdf() => $"{Fmt(X)} {Fmt(Y)} TD";
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>T* — Move to next line using current leading.</summary>
public sealed class MoveToNextLine : TextPlaceOperator
{
    public override string ToPdf() => "T*";
    public override string ToString() => ToPdf();
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>Common base for text-showing operators (Tj, TJ, ', ").
/// Lets callers pattern-match on a single type when iterating an
/// <see cref="OperatorCollection"/>:
/// <code>foreach (Operator op in ops) if (op is TextShowOperator t) total += t.Text;</code>
/// </summary>
public abstract class TextShowOperator : TextOperator
{
    /// <summary>The text content shown by this operator (best-effort —
    /// for TJ the array's string parts are concatenated).</summary>
    public virtual string Text { get; set; } = string.Empty;

    public TextShowOperator() { }
    public TextShowOperator(Aspose.Pdf.Facades.TextProperties textProperties) : base(textProperties) { }
}

/// <summary>' — Move to next line and show text.</summary>
public sealed class MoveToNextLineShowText : TextShowOperator
{
    // Store in the base Text so polymorphic access through TextShowOperator
    // returns the shown text (was a `new` shadow that read empty via the base).
    public MoveToNextLineShowText() { }
    public MoveToNextLineShowText(string text) { base.Text = text ?? string.Empty; }
    public override string ToPdf() => $"({EscapeText(Text)}) '";
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
    private static string EscapeText(string s) =>
        s.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
}

/// <summary>" — Set word/char spacing, move to next line, and show text.</summary>
public sealed class SetSpacingMoveToNextLineShowText : TextShowOperator
{
    public double WordSpacing { get; }
    public double CharSpacing { get; }
    /// <summary>Public-API-shape alias for <see cref="WordSpacing"/>.</summary>
    public double Aw => WordSpacing;
    /// <summary>Public-API-shape alias for <see cref="CharSpacing"/>.</summary>
    public double Ac => CharSpacing;
    public SetSpacingMoveToNextLineShowText(double aw, double ac, string text)
    { WordSpacing = aw; CharSpacing = ac; base.Text = text ?? string.Empty; }
    public override string ToPdf() => $"{Fmt(WordSpacing)} {Fmt(CharSpacing)} ({EscapeText(Text)}) \"";
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
    private static string EscapeText(string s) =>
        s.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
}

/// <summary>TJ — Show text with individual glyph positioning (array of strings and numeric adjustments).</summary>
public sealed class SetGlyphsPositionShowText : TextShowOperator
{
    /// <summary>Mixed array of strings (text runs) and doubles (position adjustments in 1/1000 text units).</summary>
    public object[] Items { get; }

    /// <summary>Public-API-shape projection over <see cref="Items"/>: paired text-run / numeric-position
    /// entries surfaced as <see cref="GlyphPosition"/> instances.</summary>
    public System.Collections.Generic.IEnumerable<GlyphPosition> GlyphPositions
    {
        get
        {
            for (int i = 0; i < Items.Length; i++)
            {
                if (Items[i] is string s)
                {
                    if (i + 1 < Items.Length && Items[i + 1] is double d)
                    {
                        yield return new GlyphPosition(s, d);
                        i++;
                    }
                    else if (i + 1 < Items.Length && Items[i + 1] is int n)
                    {
                        yield return new GlyphPosition(s, n);
                        i++;
                    }
                    else
                    {
                        yield return new GlyphPosition(s);
                    }
                }
            }
        }
    }

    /// <summary>The operator's operand view of the TJ array — one entry per array
    /// element (string run or numeric adjustment), the shape exposed as the
    /// command operand list. An empty TJ (<c>[] TJ</c>) has an empty
    /// list; tests assert a PDF/A conversion never writes one.</summary>
    internal System.Collections.Generic.List<object> args
    {
        get
        {
            var list = new System.Collections.Generic.List<object>(Items.Length);
            foreach (var it in Items) list.Add(it);
            return list;
        }
    }

    public SetGlyphsPositionShowText(object[] items) { Items = items ?? Array.Empty<object>(); }

    public SetGlyphsPositionShowText(System.Collections.Generic.IEnumerable<GlyphPosition> glyphPositions)
    {
        var list = new System.Collections.Generic.List<object>();
        if (glyphPositions is not null)
        {
            foreach (var gp in glyphPositions)
            {
                list.Add(gp.Text);
                if (gp.HasPosition) list.Add(gp.Position);
            }
        }
        Items = list.ToArray();
    }

    /// <summary>Concatenated string parts (numeric adjustments dropped).
    /// Overrides the base so polymorphic access through TextShowOperator
    /// returns the TJ text (was a `new` shadow that read empty via the base).</summary>
    public override string Text
    {
        get
        {
            var sb = new System.Text.StringBuilder();
            foreach (var it in Items)
                if (it is string s) sb.Append(s);
            return sb.ToString();
        }
    }

    public override string ToPdf()
    {
        // Elements are joined by single spaces with none before the closing
        // bracket — "[(a) -5.3 (b)] TJ" — the exact form asserted
        // verbatim by operator-comparing tests.
        var sb = new System.Text.StringBuilder();
        sb.Append('[');
        var first = true;
        foreach (var it in Items)
        {
            if (!first) sb.Append(' ');
            if (it is string s) sb.Append('(').Append(EscapeText(s)).Append(')');
            else if (it is double d) sb.Append(Fmt(d));
            else if (it is int i) sb.Append(Fmt(i));
            first = false;
        }
        sb.Append("] TJ");
        return sb.ToString();
    }
    public override string ToString() => ToPdf();
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
    private static string EscapeText(string s) =>
        s.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
}

/// <summary>Tj — Show text string.</summary>
public sealed class ShowText : TextShowOperator
{
    private string _text;
    private readonly FontInfo? _font;

    public override string Text
    {
        get => _text;
        set => _text = value ?? string.Empty;
    }

    public ShowText() { _text = string.Empty; }
    public ShowText(string text) { _text = text ?? string.Empty; }
    public ShowText(string text, Aspose.Pdf.Text.Font font) { _text = text ?? string.Empty; _font = font; }
    public ShowText(int index, string text) { _text = text ?? string.Empty; _ = index; }

    /// <summary>Optional font hint kept for back-compat — does not surface in
    /// the public reflection surface.</summary>
    internal ShowText(string text, FontInfo? font) { _text = text ?? string.Empty; _font = font; }

    public override string ToPdf()
    {
        var escaped = _text.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
        return $"({escaped}) Tj";
    }
    public override string ToString() => ToPdf();
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

// =====================================================================
// Abstract bases — for tests pattern-matching on operator categories.
// =====================================================================

/// <summary>Common base for all text-related operators (BT/ET, Tx state, text-show, text-place).</summary>
public abstract class TextOperator : Operator
{
    public TextOperator() { }
    public TextOperator(Aspose.Pdf.Facades.TextProperties textProperties) { TextProperties = textProperties; }

    /// <summary>Optional appearance metadata.</summary>
    public Aspose.Pdf.Facades.TextProperties? TextProperties { get; }

    /// <summary>Default IOperatorSelector dispatch — concrete subclasses generally
    /// shadow this with a typed Visit call.</summary>
    public override void Accept(IOperatorSelector visitor) { _ = visitor; }
}

/// <summary>Common base for text-state operators (Tc, Tw, Tz, TL, Tf, Tr, Ts).</summary>
public abstract class TextStateOperator : TextOperator
{
    public TextStateOperator() { }
    public TextStateOperator(Aspose.Pdf.Facades.TextProperties textProperties) : base(textProperties) { }
}

/// <summary>Common base for text-positioning operators (Td, TD, T*, Tm).</summary>
public abstract class TextPlaceOperator : TextOperator
{
    public TextPlaceOperator() { }
    public TextPlaceOperator(Aspose.Pdf.Facades.TextProperties textProperties) : base(textProperties) { }
}

/// <summary>Common base for the BT / ET delimiters.</summary>
public abstract class BlockTextOperator : TextOperator
{
    public BlockTextOperator() { }
    public BlockTextOperator(Aspose.Pdf.Facades.TextProperties textProperties) : base(textProperties) { }
}

/// <summary>Tc — Set character spacing.</summary>
public sealed class SetCharacterSpacing : TextStateOperator
{
    public double CharSpace { get; set; }
    /// <summary>Public-API-shape alias for <see cref="CharSpace"/>.</summary>
    public double CharSpacing { get => CharSpace; set => CharSpace = value; }
    public SetCharacterSpacing(double charSpacing) { CharSpace = charSpacing; }
    public override string ToPdf() => $"{Fmt(CharSpace)} Tc";
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>Tw — Set word spacing.</summary>
public sealed class SetWordSpacing : TextStateOperator
{
    public double WordSpace { get; set; }
    /// <summary>Public-API-shape alias for <see cref="WordSpace"/>.</summary>
    public double WordSpacing { get => WordSpace; set => WordSpace = value; }
    public SetWordSpacing(double wordSpacing) { WordSpace = wordSpacing; }
    public override string ToPdf() => $"{Fmt(WordSpace)} Tw";
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>Tz — Set horizontal text scaling.</summary>
public sealed class SetHorizontalTextScaling : TextStateOperator
{
    public double Scale { get; set; }
    /// <summary>Public-API-shape alias for <see cref="Scale"/>.</summary>
    public double HorizontalScaling { get => Scale; set => Scale = value; }
    public SetHorizontalTextScaling(double horizintalScaling) { Scale = horizintalScaling; }
    public override string ToPdf() => $"{Fmt(Scale)} Tz";
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>TL — Set text leading.</summary>
public sealed class SetTextLeading : TextStateOperator
{
    public double Leading { get; set; }
    public SetTextLeading(double leading) { Leading = leading; }
    public override string ToPdf() => $"{Fmt(Leading)} TL";
    public override string ToString() => ToPdf();
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>Tr — Set text rendering mode.</summary>
public sealed class SetTextRenderingMode : TextStateOperator
{
    public int RenderingMode { get; }
    public SetTextRenderingMode() { RenderingMode = 0; }
    public SetTextRenderingMode(int renderingMode) { RenderingMode = renderingMode; }
    public override string ToPdf() => $"{RenderingMode.ToString(CultureInfo.InvariantCulture)} Tr";
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>Ts — Set text rise.</summary>
public sealed class SetTextRise : TextStateOperator
{
    public double Rise { get; set; }
    /// <summary>Public-API-shape alias for <see cref="Rise"/>.</summary>
    public double TextRise { get => Rise; set => Rise = value; }
    public SetTextRise(double textRise) { Rise = textRise; }
    public override string ToPdf() => $"{Fmt(Rise)} Ts";
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

// =====================================================================
// Type 3 font operators (PDF 32000-1 §9.6.5).
// =====================================================================

/// <summary>d0 — Set glyph width in a Type 3 font.</summary>
public sealed class SetCharWidth : Operator
{
    public double Wx { get; }
    public double Wy { get; }
    public SetCharWidth(double wx, double wy) { Wx = wx; Wy = wy; }
    public override string ToPdf() => $"{Fmt(Wx)} {Fmt(Wy)} d0";
    public override string ToString() => ToPdf();
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

/// <summary>d1 — Set glyph width and bounding box in a Type 3 font.</summary>
public sealed class SetCharWidthBoundingBox : Operator
{
    public double Wx { get; }
    public double Wy { get; }
    public double LLx { get; }
    public double LLy { get; }
    public double URx { get; }
    public double URy { get; }
    /// <summary>Public-API-shape camel-cased alias for <see cref="LLx"/>.</summary>
    public double Llx => LLx;
    /// <summary>Public-API-shape camel-cased alias for <see cref="LLy"/>.</summary>
    public double Lly => LLy;
    /// <summary>Public-API-shape camel-cased alias for <see cref="URx"/>.</summary>
    public double Urx => URx;
    /// <summary>Public-API-shape camel-cased alias for <see cref="URy"/>.</summary>
    public double Ury => URy;
    public SetCharWidthBoundingBox(double wx, double wy, double llx, double lly, double urx, double ury)
    { Wx = wx; Wy = wy; LLx = llx; LLy = lly; URx = urx; URy = ury; }
    public override string ToPdf() =>
        $"{Fmt(Wx)} {Fmt(Wy)} {Fmt(LLx)} {Fmt(LLy)} {Fmt(URx)} {Fmt(URy)} d1";
    public override string ToString() => ToPdf();
    public override void Accept(IOperatorSelector visitor) => visitor.Visit(this);
}

// =====================================================================
// Marked-content operators (PDF 32000-1 §14.6).
// =====================================================================

/// <summary>One element of a TJ-style glyph-position array: a string with
/// an optional preceding/following position adjustment (in 1/1000 text units).</summary>
public sealed class GlyphPosition
{
    public string Text { get; }
    public double Position { get; }
    public bool HasPosition { get; }
    public GlyphPosition(string text) { Text = text; HasPosition = false; }
    public GlyphPosition(string text, double position)
    { Text = text; Position = position; HasPosition = true; }
}
