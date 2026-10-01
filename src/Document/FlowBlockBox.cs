namespace Aspose.Pdf;

public sealed partial class Document
{
    private sealed partial class FlowLayout
    {
        /// <summary>Set while a block-box fragment's lines are written: the block's
        /// background is painted here over the whole box, so the plain writer must
        /// not paint its own over the inner box.</summary>
        private bool _blockBackgroundTaken;

        /// <summary>A fragment laid as a BLOCK BOX (see <see cref="Text.TextFragment.BlockBorder"/>
        /// and its siblings): from the outside in, the left and right margins, the
        /// border bands, the padding, and the content box the lines fill. The top
        /// margin was taken by the dispatcher; the cursor drops by the top band and
        /// padding, the write region narrows to the content box, the lines flow in
        /// it, the cursor drops by the bottom padding and band, and then the box
        /// gets its background (under everything) and its bands, inserted ahead of
        /// the text so the page reads background, bands, glyphs.
        ///
        /// A fixed <see cref="Text.TextFragment.BoxHeight"/> is the content box's
        /// height whatever the lines take: lines beyond it are dropped, a shorter
        /// content leaves it empty below (or above, seated at the bottom), and the
        /// flow continues under the box. A box that runs onto another page is
        /// bordered on the page it ends on, from that page's top.</summary>
        private bool WriteBlockBoxFragment(Text.TextFragment tf)
        {
            var (bl, bb, br, bt) = tf.BlockBorder is { } border ? BorderBands(border) : (0, 0, 0, 0);
            var pad = tf.BlockPadding;
            var (pl, pb, pr, pt) = (pad?.Left ?? 0, pad?.Bottom ?? 0, pad?.Right ?? 0, pad?.Top ?? 0);
            var (ml, mr) = (tf.Margin?.Left ?? 0, tf.Margin?.Right ?? 0);

            // The box across the region: its content width, then where it stands.
            var regionLeft = CurLeft;
            var regionWidth = CurWidth;
            var chrome = ml + mr + bl + br + pl + pr;
            var contentWidth = tf.BlockWidth > 0 ? tf.BlockWidth : Math.Max(0, regionWidth - chrome);
            if (tf.BlockMaxWidth > 0) contentWidth = Math.Min(contentWidth, tf.BlockMaxWidth);
            if (tf.BlockMinWidth > 0) contentWidth = Math.Max(contentWidth, tf.BlockMinWidth);
            var boxWidth = contentWidth + bl + br + pl + pr;
            var slack = regionWidth - ml - mr - boxWidth;
            var boxLeft = regionLeft + ml + (slack > 0 ? tf.BlockHorizontalAlignment switch
            {
                HorizontalAlignment.Center => slack / 2,
                HorizontalAlignment.Right => slack,
                _ => 0,
            } : 0);

            var startSlot = _currentSlot;
            var boxTop = _curY;
            var insertAt = _overflowBuffer?.Count ?? _startPage.ContentStreamCount;
            AdvanceY(bt + pt);
            var contentTop = _curY;

            // The content box: fixed, floored, or the lines' own.
            var lineCount = CountBlockLines(tf, contentWidth);
            var lineHeight = BlockLineHeight(tf);
            var linesHeight = lineCount * lineHeight;
            // Fixed, else the lines' own; capped, then floored - the floor wins.
            var contentHeight = tf.BoxHeight > 0 ? tf.BoxHeight : linesHeight;
            if (tf.BoxMaxHeight > 0) contentHeight = Math.Min(contentHeight, tf.BoxMaxHeight);
            contentHeight = Math.Max(contentHeight, tf.BoxMinHeight);
            var linesKept = tf.WrapLinesCount;
            if (lineHeight > 0 && linesHeight > contentHeight + 1e-6)
            {
                var fit = Math.Max(1, (int)Math.Floor((contentHeight + 1e-6) / lineHeight));
                tf.WrapLinesCount = linesKept > 0 ? Math.Min(linesKept, fit) : fit;
                linesHeight = Math.Min(lineCount, fit) * lineHeight;
            }
            // A single line seats where the alignment says; several lines stay at
            // the top (the reference either leaves them there or moves them clean
            // out of the box, which is a quirk, not a rule to follow).
            if (lineCount == 1 && contentHeight > linesHeight)
            {
                var drop = BottomSeatDrop(tf, contentHeight, linesHeight, lineHeight);
                if (tf.BlockVerticalAlignment == VerticalAlignment.Bottom) AdvanceY(drop);
                else if (tf.BlockVerticalAlignment == VerticalAlignment.Center) AdvanceY(drop / 2);
            }

            var leftBefore = LeftIndent;
            var rightBefore = RightIndent;
            LeftIndent += boxLeft + bl + pl - regionLeft;
            RightIndent += regionLeft + regionWidth - (boxLeft + bl + pl + contentWidth);
            _blockBackgroundTaken = tf.TextState.BackgroundColor is not null
                && tf.TextState.FormattingOptions is { BlockBackground: true };
            bool wrote;
            try
            {
                wrote = WriteTextFragmentCore(tf);
            }
            finally
            {
                LeftIndent = leftBefore;
                RightIndent = rightBefore;
                _blockBackgroundTaken = false;
                tf.WrapLinesCount = linesKept;
            }
            if (!wrote) return false;

            // The box closes at its content box's bottom, not where the lines
            // stopped, when the height was fixed or floored.
            if (_currentSlot == startSlot && contentHeight > linesHeight + 1e-6)
                _curY = contentTop - contentHeight;
            AdvanceY(pb + bb);
            var box = new Rectangle(boxLeft, _curY, boxLeft + boxWidth, _currentSlot == startSlot ? boxTop : ContentTop);
            var options = tf.TextState.FormattingOptions;
            var fill = options is { BlockBackground: true } ? tf.TextState.BackgroundColor : null;
            var fillAlpha = fill is null ? null
                : options?.BackgroundOpacity is { } opacity
                ? Text.TextParagraph.EnsureAlphaExtGState(_startPage, opacity, strokeToo: false)
                : Text.TextParagraph.EnsureFillAlphaExtGState(_startPage, fill.AByte);
            var pictures = BackgroundPicturePainter.Prepare(tf.BlockBackgroundPictures, _startPage, box,
                (bl, bb, br, bt), tf.BlockPadding);
            var paint = BuildBlockPaint(tf.BlockBorder, box, fill,
                new BlockFill(GrownByOutset(box, options?.BlockBackgroundOutset), fillAlpha,
                    tf.BlockCornerRadii?.Resolve(box.Width, box.Height), pictures));
            if (paint.Length == 0) return true;
            if (_currentSlot != startSlot) WriteContent(paint, tf.TextState);
            else if (_overflowBuffer is not null) _overflowBuffer.Insert(insertAt, paint);
            else _startPage.InsertContentStreamAt(insertAt, paint);
            return true;
        }

        /// <summary>The height of the fragment's lines wrapped to the current region,
        /// as the block writer prices them - what a keep rule weighs against the room.</summary>
        internal double LinesHeightOf(Text.TextFragment tf) => CountBlockLines(tf, CurWidth) * BlockLineHeight(tf);

        /// <summary>How many lines the fragment's text wraps to in <paramref name="width"/>,
        /// measured the way the plain writer will wrap it. A fragment whose segments
        /// flow as runs counts its own lines.</summary>
        private static int CountBlockLines(Text.TextFragment tf, double width)
        {
            if (width <= 0) return 0;
            if (tf.TextState.FormattingOptions is { SegmentsFlowAsRuns: true }
                && CollectSegmentRuns(tf) is { Count: > 0 } runs)
                return WrapSegmentRuns(runs, width, tf.TextState.FormattingOptions?.FirstLineIndent ?? 0, tf.TextState.FormattingOptions).Count;
            var baseFont = Text.TextBuilder.MapToStandard14Public(tf.TextState);
            var fontSize = tf.TextState.FontSize > 0 ? tf.TextState.FontSize : 12;
            var lines = Text.TextPaginator.WrapToWidth(tf.Text ?? string.Empty, baseFont, fontSize, width,
                tf.TextState.FontData ?? tf.TextState.Font?.SourceFontData,
                tf.TextState.FormattingOptions?.FirstLineIndent ?? 0, tf.TextState.CharacterSpacing,
                tf.TextState.FormattingOptions?.HangingBreakSpace ?? false, tf.TextState.WordSpacing);
            return lines.Count;
        }

        /// <summary>The pitch one of the fragment's lines takes: its size plus the
        /// caller's leading, as the plain writer prices it.</summary>
        private static double BlockLineHeight(Text.TextFragment tf)
        {
            var fontSize = tf.TextState.FontSize > 0 ? (double)tf.TextState.FontSize : 12;
            return tf.TextState.LineSpacing > 0 ? fontSize + tf.TextState.LineSpacing : fontSize;
        }

        /// <summary>How far the lines drop from the content box's top so the last
        /// line's font descent sits flush with the box's bottom: the room the box has
        /// over the lines, less the difference between the line box's descent below
        /// the baseline and the face's own.</summary>
        private static double BottomSeatDrop(Text.TextFragment tf, double contentHeight, double linesHeight, double lineHeight)
        {
            var state = tf.TextState;
            var fontSize = state.FontSize > 0 ? (double)state.FontSize : 12;
            var (ascentEm, descentEm) = state is { LineBoxAscentEm: { } a, LineBoxDescentEm: { } d }
                ? (a, d) : (GenericAscentEm, GenericDescentEm);
            // The plain writer seats a declared line box half the surplus leading
            // plus the ascent below the line top, so the baseline is that far down
            // and the line box's bottom is lineHeight further.
            var seat = (lineHeight - (ascentEm + descentEm) * fontSize) / 2 + ascentEm * fontSize;
            var boxDescent = lineHeight - seat;
            // The last line box may overhang the content box's bottom by the
            // difference: the face's descent, not the line box's, is what sits flush.
            return contentHeight - linesHeight + (boxDescent - descentEm * fontSize);
        }

        /// <summary>The width each side of a border occupies (left, bottom, right, top):
        /// its own stroke width when the side draws, else nothing.</summary>
        internal static (double Left, double Bottom, double Right, double Top) BorderBands(BorderInfo border)
        {
            double Band(BorderSide side, bool assigned, GraphInfo? gi) =>
                border.Side.HasFlag(side) || assigned ? Math.Max(0, gi?.LineWidth > 0 ? gi.LineWidth : border.Width) : 0;
            return (Band(BorderSide.Left, border.LeftAssigned, border.RawLeft),
                Band(BorderSide.Bottom, border.BottomAssigned, border.RawBottom),
                Band(BorderSide.Right, border.RightAssigned, border.RawRight),
                Band(BorderSide.Top, border.TopAssigned, border.RawTop));
        }

        /// <summary>Where a block's background goes: the rectangle it fills (the box,
        /// grown or shrunk by its outset), the alpha state it is painted under, the
        /// box's rounded corners when it has them, and the pictures painted over the
        /// colour (see <see cref="BackgroundPicturePainter"/>).</summary>
        internal readonly record struct BlockFill(Rectangle Area, string? Alpha, ResolvedCorners? Corners,
            IReadOnlyList<BackgroundPictureLayer>? Pictures = null);

        /// <summary>The box's background, when it has one, then the border's rules
        /// inside the box in their own styles (see <see cref="RulePainter"/>): a solid
        /// side one filled band running the box's full extent so the corners are
        /// covered. A box with rounded corners paints both inside its rounded outline
        /// (<see cref="RulePainter.PaintRoundedBox"/>). Empty when there is nothing to paint.</summary>
        internal static byte[] BuildBlockPaint(BorderInfo? border, Rectangle box, Color? fill, BlockFill paint)
        {
            if (fill is null && border is null && paint.Pictures is not { Count: > 0 }) return Array.Empty<byte>();
            var b = new Content.ContentStreamBuilder();
            PaintBlock(b, border, box, fill, paint);
            return b.Build();
        }

        /// <summary><see cref="BuildBlockPaint"/> into a stream being built, in a graphics
        /// state of its own.</summary>
        internal static void PaintBlock(Content.ContentStreamBuilder b, BorderInfo? border, Rectangle box, Color? fill, BlockFill paint)
        {
            b.SaveState();
            if (paint.Corners is { Any: true } corners)
                RulePainter.PaintRoundedBox(b, border, box.LLX, box.LLY, box.Width, box.Height, corners, fill,
                    (paint.Area.LLX, paint.Area.LLY, paint.Area.Width, paint.Area.Height), paint.Alpha, paint.Pictures);
            else
            {
                if (fill is { } bg)
                {
                    b.SaveState();
                    if (paint.Alpha is not null) b.SetExtGState(paint.Alpha);
                    b.SetFillColor(bg.R / 255.0, bg.G / 255.0, bg.B / 255.0);
                    b.Rectangle(paint.Area.LLX, paint.Area.LLY, paint.Area.Width, paint.Area.Height).Fill();
                    b.RestoreState();
                }
                if (paint.Pictures is { Count: > 0 } pictures) BackgroundPicturePainter.Paint(b, pictures);
                if (border is not null) RulePainter.PaintBox(b, border, box.LLX, box.LLY, box.Width, box.Height);
            }
            b.RestoreState();
        }
    }
}
