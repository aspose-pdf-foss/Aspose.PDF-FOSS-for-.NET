using Aspose.Pdf.Core;

namespace Aspose.Pdf;

public sealed partial class Page
{
    /// <summary>The frame of a position of a page's text: how many redraws of the page's content - a turn baked
    /// into it, a resize that prefixed it with a matrix - had been made by then; for text read off a page turned
    /// by /Rotate - which the reader reports in the turned view - the matrix carrying a position of that view
    /// back to the page's unrotated space, where the content draws (null for text read upright, or written); and
    /// whether a writer drew the text there.</summary>
    internal sealed class TextFrame
    {
        internal TextFrame(int transforms, int rotationQuarter, PdfObject? box, Matrix? unrotate, bool written = false)
        {
            Transforms = transforms;
            RotationQuarter = rotationQuarter;
            Box = box;
            Unrotate = unrotate;
            Written = written;
        }

        internal int Transforms { get; }

        internal int RotationQuarter { get; }

        /// <summary>The page's own /MediaBox entry the rotation turned positions within.</summary>
        internal PdfObject? Box { get; }

        internal Matrix? Unrotate { get; }

        /// <summary>Drawn by a writer, which draws a run under whatever the content leaves in force at its end.</summary>
        internal bool Written { get; }
    }

    /// <summary>The matrices the page's whole content was redrawn through, in the order they were made - a turn's
    /// wrap, a resize's leading cm - each as written into the content, with the matrix the content left in force at
    /// its end before it when text had been written onto the page since the redraw before (null otherwise).</summary>
    private List<(Matrix Matrix, Matrix? WrittenUnder)>? _contentTransforms;

    /// <summary>A resize's matrix held with the operators it was inserted among: it reaches the content, and is
    /// recorded, when they are written to the page, and goes with them when they are let go unwritten.</summary>
    private List<(Matrix Matrix, Matrix? WrittenUnder)>? _heldTransforms;

    /// <summary>Text was written onto the page since its content was last redrawn.</summary>
    private bool _writtenSinceRedraw;

    private TextFrame? _readingFrame;

    /// <summary>The frame the page's text is read in now: the same object while the content is not redrawn and
    /// neither the rotation nor the media box it turns within changes, so a fragment records it for free.</summary>
    internal TextFrame ReadingFrame
    {
        get
        {
            var transforms = _contentTransforms?.Count ?? 0;
            var rotation = RotationQuarter;
            var box = rotation == 0 ? null : _dict.Get("MediaBox");
            if (_readingFrame is not { } frame || frame.Transforms != transforms || frame.RotationQuarter != rotation || !ReferenceEquals(frame.Box, box))
                _readingFrame = frame = new TextFrame(transforms, rotation, box, rotation is 90 or 180 or 270 ? RotationMatrix.Inverse() : null);
            return frame;
        }
    }

    /// <summary>The frame of text a writer draws onto the page now, under the matrix the content leaves in force at
    /// its end, which the next redraw of the content records: in the page's own space, where nothing is unrotated,
    /// or - <paramref name="inView"/> - in the view of the page turned, as the reader reports text there, which the
    /// rotation it is viewed under carries back to the page's space.</summary>
    internal TextFrame TakeWritingFrame(bool inView = false)
    {
        _writtenSinceRedraw = true;
        var read = ReadingFrame;
        return inView
            ? new TextFrame(read.Transforms, read.RotationQuarter, read.Box, read.Unrotate, written: true)
            : new TextFrame(read.Transforms, 0, null, null, written: true);
    }

    /// <summary>Record a matrix the page's whole content was redrawn through, as written into it;
    /// <paramref name="writtenUnder"/> is what the content left in force at its end before it (see WrittenUnder).</summary>
    private void RecordContentTransform(Matrix matrix, Matrix? writtenUnder)
    {
        (_contentTransforms ??= new()).Add((matrix, writtenUnder));
        _writtenSinceRedraw = false;
    }

    /// <summary>Hold a resize's matrix inserted among the operators held in Contents until they are written.</summary>
    private void HoldContentTransform(Matrix matrix)
    {
        (_heldTransforms ??= new()).Add((matrix, _writtenSinceRedraw ? EndMatrixOf(CollectContentBytes()) : null));
        _writtenSinceRedraw = false;
    }

    /// <summary>The operators held in Contents were written to the page: the matrices held with them are in the content now.</summary>
    private void CommitHeldTransforms()
    {
        if (_heldTransforms is null) return;
        (_contentTransforms ??= new()).AddRange(_heldTransforms);
        _heldTransforms = null;
    }

    /// <summary>What <paramref name="content"/> leaves in force at its end, wanted only when text was written onto
    /// the page since its last redraw - a writer drew that text under it - and null otherwise, or for the identity.</summary>
    private Matrix? WrittenUnder(byte[] content) => _writtenSinceRedraw ? EndMatrixOf(content) : null;

    private static Matrix? EndMatrixOf(byte[] content)
    {
        var (a, b, c, d, e, f) = ImageStamp.ScanGraphicsState(content).Ctm;
        var end = new Matrix(a, b, c, d, e, f);
        return IsIdentity(end) ? null : end;
    }

    /// <summary>A matrix as the content stream spells it: each entry as <see cref="Format"/> writes it, so what is
    /// recorded is what a reader of the content reads back.</summary>
    private static Matrix WrittenMatrix(double a, double b, double c, double d, double e, double f)
    {
        static double Written(double v) => double.Parse(Format(v), System.Globalization.CultureInfo.InvariantCulture);
        return new Matrix(Written(a), Written(b), Written(c), Written(d), Written(e), Written(f));
    }

    /// <summary>The redraws of the content since <paramref name="frame"/>, composed in the order they were made (a
    /// later one wraps or prefixes what the earlier drew): what carries a place on the page as it was then to the
    /// page now. Null when none was made since.</summary>
    private Matrix? TransformsSince(TextFrame? frame)
    {
        if (frame is null || _contentTransforms is null || frame.Transforms >= _contentTransforms.Count) return null;
        var since = _contentTransforms[frame.Transforms].Matrix;
        for (var i = frame.Transforms + 1; i < _contentTransforms.Count; i++) since = since.Multiply(_contentTransforms[i].Matrix);
        return since;
    }

    /// <summary>What carries a place in the frame <paramref name="frame"/> was read in - the turned view of a page
    /// viewed turned, or the page's own space - to the page's space now: the rotation it was read under undone,
    /// then the redraws since. Null when neither applies.</summary>
    private Matrix? FromReadFrame(TextFrame? frame)
    {
        var since = TransformsSince(frame);
        if (frame?.Unrotate is not { } unrotate) return since;
        return since is null ? unrotate : unrotate.Multiply(since);
    }

    /// <summary>What carries a place in the space of the run <paramref name="frag"/> was read from - where a rule
    /// measured back through the run's matrix is written - to the page's space now: the run's matrix, which
    /// carries it to the frame the text was read in, then <see cref="FromReadFrame"/>.</summary>
    private Matrix? FromRunSpace(Text.TextFragment frag)
    {
        var fromRead = FromReadFrame(frag.PositionFrame);
        if (frag.ExtractionCtm is not { } run || frag.PositionFrame is { Written: true }) return fromRead;
        return fromRead is null ? run : run.Multiply(fromRead);
    }

    /// <summary>How far an entry may stand off the identity's and the matrix still write nothing: the rounding a
    /// matrix times the inverse of the same one read back from the content leaves.</summary>
    private const double IdentityTolerance = 1e-6;

    private static bool IsIdentity(Matrix m)
        => Math.Abs(m.A - 1) <= IdentityTolerance && Math.Abs(m.B) <= IdentityTolerance && Math.Abs(m.C) <= IdentityTolerance
            && Math.Abs(m.D - 1) <= IdentityTolerance && Math.Abs(m.E) <= IdentityTolerance && Math.Abs(m.F) <= IdentityTolerance;

    /// <summary>The matrix a decoration's own q opens with: <paramref name="desired"/> - what carries the place it
    /// was measured in to the page now - with <paramref name="under"/>, the matrix its stream is drawn under, divided
    /// out; null when that leaves the identity, as where the content leaves the run's own matrix in force. Text a
    /// writer drew is under what the content left in force then, which a decoration appended after it shares until
    /// the content is redrawn.</summary>
    private Matrix? DecorationMatrix(TextFrame? frame, Matrix? desired, Matrix? under)
    {
        if (frame is { Written: true })
        {
            if (frame.Transforms >= (_contentTransforms?.Count ?? 0)) return null;
            if (_contentTransforms![frame.Transforms].WrittenUnder is { } written) desired = desired is null ? written : written.Multiply(desired);
        }
        var matrix = desired;
        Matrix? back = null;
        // A matrix that collapses the page shows nothing drawn under it, whatever comes before.
        try { back = under?.Inverse(); }
        catch (InvalidOperationException) { back = null; }
        if (back is not null) matrix = matrix is null ? back : matrix.Multiply(back);
        return matrix is null || IsIdentity(matrix) ? null : matrix;
    }

    /// <summary>The matrix a decoration appended to the content opens with. Once the page's content was redrawn - a
    /// turn baked into it, a resize recorded - the place is computed (see DecorationMatrix). On a page never redrawn
    /// the decoration takes what the content leaves in force at its end for what its text was drawn under: numbers
    /// taken back through the run's matrix (<paramref name="inRunSpace"/>) take no matrix of their own, the reader's
    /// only the rotation the text was read under undone - so a matrix a caller put ahead of the operators, or one an
    /// earlier stream leaves to a search reading each stream alone, reaches the decoration as it reaches the word.</summary>
    private Matrix? AppendedDecorationMatrix(TextFrame? frame, Matrix? desired, bool inRunSpace)
    {
        if (_contentTransforms is { Count: > 0 }) return DecorationMatrix(frame, desired, AppendedBase());
        return inRunSpace ? null : DecorationMatrix(frame, desired, null);
    }

    /// <summary>What a stream appended to the content now is drawn under: the matrix the content leaves in force at
    /// its end (null for the identity), read once per save flush of the page's decorations.</summary>
    private Matrix? AppendedBase()
    {
        if (!_appendedBaseRead)
        {
            _appendedBase = EndMatrixOf(CollectContentBytes());
            _appendedBaseRead = true;
        }
        return _appendedBase;
    }

    private Matrix? _appendedBase;
    private bool _appendedBaseRead;

    /// <summary>A decoration block over the box (x, y, w, h) of a place read in <paramref name="frame"/>,
    /// laid in the page's unrotated space, where the content the block goes into draws.</summary>
    private static List<Operator> DecorationBlockIn(TextFrame? frame, Color? colour, double x, double y, double w, double h)
    {
        if (frame?.Unrotate is not { } unrotate) return DecorationBlock(colour, x, y, w, h);
        var box = unrotate.Transform(new Rectangle(x, y, x + w, y + h));
        return DecorationBlock(colour, box.LLX, box.LLY, box.Width, box.Height);
    }

    /// <summary>Open a decoration's graphics state, drawn through <paramref name="matrix"/> (see DecorationMatrix).</summary>
    private static void SaveDecorationState(Content.ContentStreamBuilder builder, Matrix? matrix)
    {
        builder.SaveState();
        if (matrix is not null) builder.SetMatrix(matrix.A, matrix.B, matrix.C, matrix.D, matrix.E, matrix.F);
    }

    /// <summary>Set the matrix laying a decoration along a run read in the direction (ux, uy) from
    /// (x, y): in the page's unrotated space, where the content draws, when the page was viewed turned
    /// as the run was read.</summary>
    private static void SetAlongRun(Content.ContentStreamBuilder builder, Text.TextFragment frag, double ux, double uy, double x, double y)
    {
        var along = new Matrix(ux, uy, -uy, ux, x, y);
        if (frag.PositionFrame?.Unrotate is { } unrotate) along = along.Multiply(unrotate);
        builder.SetMatrix(along.A, along.B, along.C, along.D, along.E, along.F);
    }
}
