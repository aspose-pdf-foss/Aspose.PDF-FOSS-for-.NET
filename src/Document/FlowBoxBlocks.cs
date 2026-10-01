namespace Aspose.Pdf;

public sealed partial class Document
{
    private sealed partial class FlowLayout
    {
        /// <summary>One <see cref="BoxBlock"/> the flow is inside, with the part of it
        /// the current page holds: where that part's border box stands and where on the
        /// page's content its paint belongs.</summary>
        private sealed class BoxInFlow
        {
            public BoxBlock Box = null!;
            public double Left, Width;
            public double Top;
            public double StartY;
            public int Slot;
            public int? StartSlot;
            public bool Opened = true;
            public int InsertAt;
            public double LeftIndentBefore, RightIndentBefore;
            public (double Left, double Bottom, double Right, double Top) Bands;
            public (double Left, double Bottom, double Right, double Top) Padding;

            /// <summary>Where the part's paragraphs start: under its top band and padding.</summary>
            public double ContentTop => Top - Bands.Top - Padding.Top;

            /// <summary>What the box needs under its paragraphs: bottom padding, band and margin.</summary>
            public double Floor => Padding.Bottom + Bands.Bottom + Box.Margin.Bottom;
        }

        /// <summary>The boxes the flow is inside, outermost first.</summary>
        private readonly List<BoxInFlow> _openBoxes = new();

        /// <summary>Room the open boxes keep under their paragraphs: every open box's
        /// bottom padding, band and margin. Zero outside any box.</summary>
        private double _boxFloor;

        /// <summary>Where the paragraph that is about to break closes on the page it
        /// leaves, when that is not the cursor: set by the writer just before it breaks,
        /// read by the boxes closing their parts.</summary>
        private double? _partBottom;

        /// <summary>True while the flow is inside a <see cref="BoxBlock"/>.</summary>
        public bool InsideBox => _openBoxes.Count > 0;

        /// <summary>Open a box at the cursor: its top margin, then its top band and padding;
        /// the region narrows to its content box until <see cref="CloseBox"/>. Nothing is
        /// reserved up front: a first paragraph that cannot start here breaks the page, and
        /// the empty part left behind is dropped (see <see cref="ClosePartsAtBreak"/>).</summary>
        public void OpenBox(BoxBlock box)
        {
            var (bl, bb, br, bt) = box.Border is { } border ? BorderBands(border) : (0, 0, 0, 0);
            var pad = box.Padding;
            var open = new BoxInFlow
            {
                Box = box,
                Bands = (bl, bb, br, bt),
                Padding = (pad?.Left ?? 0, pad?.Bottom ?? 0, pad?.Right ?? 0, pad?.Top ?? 0),
                LeftIndentBefore = LeftIndent,
                RightIndentBefore = RightIndent,
            };

            var regionLeft = CurLeft;
            var regionWidth = CurWidth;
            var (ml, mr) = (box.Margin.Left, box.Margin.Right);
            var sides = bl + br + open.Padding.Left + open.Padding.Right;
            var contentWidth = box.Width <= 0 ? Math.Max(0, regionWidth - ml - mr - sides)
                : box.WidthIsFraction ? box.Width * regionWidth
                : box.Width;
            open.Width = contentWidth + sides;
            var slack = regionWidth - ml - mr - open.Width;
            open.Left = regionLeft + ml + (slack > 0 ? box.HorizontalAlignment switch
            {
                HorizontalAlignment.Center => slack / 2,
                HorizontalAlignment.Right => slack,
                _ => 0,
            } : 0);

            open.StartY = _curY;
            if (box.Margin.Top > 0) AdvanceY(box.Margin.Top);
            _openBoxes.Add(open);
            _boxFloor += open.Floor;
            BeginPart(open);
            LeftIndent += open.Left + bl + open.Padding.Left - regionLeft;
            RightIndent += regionLeft + regionWidth - (open.Left + open.Width - br - open.Padding.Right);
        }

        /// <summary>Close the innermost box under its paragraphs: its least height, then its
        /// bottom padding and band, its paint ahead of the paragraphs, then its bottom margin.</summary>
        public void CloseBox()
        {
            if (_openBoxes.Count == 0) return;
            var open = _openBoxes[_openBoxes.Count - 1];
            _openBoxes.RemoveAt(_openBoxes.Count - 1);
            _boxFloor -= open.Floor;
            LeftIndent = open.LeftIndentBefore;
            RightIndent = open.RightIndentBefore;

            // The least height holds on the page the box starts, where its content top is.
            if (open.Box.MinHeight > 0 && open.Slot == open.StartSlot)
                _curY = Math.Min(_curY, open.ContentTop - open.Box.MinHeight);
            var bottom = _curY - open.Padding.Bottom - open.Bands.Bottom;
            PaintPart(open, bottom);
            _curY = bottom;
            _lastBodyBaseline = null;
            if (open.Box.Margin.Bottom > 0) AdvanceY(open.Box.Margin.Bottom);
        }

        /// <summary>Start the part of a box the current page holds, at the cursor.</summary>
        private void BeginPart(BoxInFlow open)
        {
            open.Top = _curY;
            open.Slot = _currentSlot;
            open.StartSlot ??= _currentSlot;
            open.InsertAt = _overflowBuffer?.Count ?? _startPage.ContentStreamCount;
            AdvanceY(open.Bands.Top + open.Padding.Top);
        }

        /// <summary>The page is about to end with boxes open: close each one's part under
        /// what the page holds of it, innermost first. A part that holds nothing is not
        /// painted, and the box around it closes where that box began instead - the empty
        /// box moves on whole.</summary>
        private void ClosePartsAtBreak()
        {
            if (_openBoxes.Count == 0) { _partBottom = null; return; }
            var closeAt = _partBottom ?? _curY;
            _partBottom = null;
            for (var i = _openBoxes.Count - 1; i >= 0; i--)
            {
                var open = _openBoxes[i];
                open.Opened = closeAt < open.ContentTop - Epsilon;
                if (!open.Opened)
                {
                    closeAt = open.StartY;
                    continue;
                }
                var bottom = closeAt - open.Padding.Bottom - open.Bands.Bottom;
                PaintPart(open, bottom);
                closeAt = bottom - open.Box.Margin.Bottom;
            }
        }

        /// <summary>The new page opens every box again, outermost first: its top margin
        /// when the box asks for it (always, for a box whose first part moved here whole),
        /// then its top band and padding.</summary>
        private void OpenPartsAfterBreak()
        {
            foreach (var open in _openBoxes)
            {
                open.StartY = _curY;
                if (open.Box.Margin.Top > 0 && (open.Box.TopMarginAfterBreak || !open.Opened)) AdvanceY(open.Box.Margin.Top);
                if (!open.Opened) open.StartSlot = null;
                BeginPart(open);
            }
        }

        /// <summary>Paint one part of a box - its background, then its border - ahead of
        /// the paragraphs it holds on the page the part stands on.</summary>
        private void PaintPart(BoxInFlow open, double bottom)
        {
            if (IsDryRun) return;
            if (open.Slot != _currentSlot) return;
            var paint = PartPaint(open, bottom, open.Top);
            if (paint.Length == 0) return;
            if (_overflowBuffer is not null) _overflowBuffer.Insert(open.InsertAt, paint);
            else _startPage.InsertContentStreamAt(open.InsertAt, paint);
        }

        /// <summary>The paint of one part of a box standing from <paramref name="top"/> down
        /// to <paramref name="bottom"/>: its background colour and pictures, then its border,
        /// then its middle rule.</summary>
        private byte[] PartPaint(BoxInFlow open, double bottom, double top)
        {
            var box = open.Box;
            var rect = new Rectangle(open.Left, bottom, open.Left + open.Width, top);
            var fill = box.BackgroundColor;
            var alpha = fill is null ? null
                : box.BackgroundOpacity is { } opacity
                ? Text.TextParagraph.EnsureAlphaExtGState(_startPage, opacity, strokeToo: false)
                : Text.TextParagraph.EnsureFillAlphaExtGState(_startPage, fill.AByte);
            var pictures = BackgroundPicturePainter.Prepare(box.BackgroundPictures, _startPage, rect,
                open.Bands, box.Padding);
            var paint = BuildBlockPaint(box.Border, rect, fill,
                new BlockFill(rect, alpha, box.CornerRadii?.Resolve(rect.Width, rect.Height), pictures));
            return box.MiddleRule is { } rule ? [.. paint, .. MiddleRulePaint(open, rule, bottom)] : paint;
        }

        /// <summary>A box's middle rule across the part's content box, halfway between the
        /// content top and the top of the bottom padding.</summary>
        private byte[] MiddleRulePaint(BoxInFlow open, GraphInfo rule, double bottom)
        {
            var left = open.Left + open.Bands.Left + open.Padding.Left;
            var right = open.Left + open.Width - open.Bands.Right - open.Padding.Right;
            var y = (open.ContentTop + bottom + open.Bands.Bottom + open.Padding.Bottom) / 2;
            var b = new Content.ContentStreamBuilder();
            b.SaveState();
            if (Text.TextParagraph.EnsureAlphaExtGState(_startPage, rule.StrokeOpacity, strokeToo: true) is { } gs)
                b.SetExtGState(gs);
            RulePainter.StrokeRule(b, rule, left, y, right);
            b.RestoreState();
            return b.Build();
        }

        /// <summary>Where a paragraph breaking inside a box closes on the page it leaves:
        /// its last line's box bottom, or that line's baseline less the descent the caller
        /// closes a broken box at (<see cref="Text.TextFormattingOptions.BreakBoxDescentEm"/>);
        /// then, for a paragraph whose parts each keep their margins
        /// (<see cref="Text.TextFormattingOptions.TopMarginAfterBreak"/>), its bottom margin.</summary>
        private double PartBottomAtBreak(FlowTextFragmentState wtf, Text.TextFragment tf)
        {
            var options = tf.TextState.FormattingOptions;
            var close = options?.BreakBoxDescentEm is > 0 and var descentEm
                && (_lastBodyBaseline ?? wtf.nonEmbeddedLastBaseline) is { } baseline
                ? baseline - descentEm * wtf.fontSize
                : _curY;
            return close - (options is { TopMarginAfterBreak: true } ? tf.Margin?.Bottom ?? 0 : 0);
        }

        private const double Epsilon = 1e-6;
    }
}
