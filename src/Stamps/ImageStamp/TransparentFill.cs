namespace Aspose.Pdf;

public partial class ImageStamp
{
    private const int RgbBytes = 3;

    // How far, in pixels, the exact nearest colour reaches out from the visible pixels: a resampler that shrinks
    // an image sixteen times averages no further than this across an edge.
    private const int ExactFillReach = 8;

    /// <summary>Give the fully transparent pixels of an image the colours of the visible pixels near them. The
    /// colour a PNG keeps under alpha 0 is invisible at full size, but a viewer that resamples the image averages it
    /// into the visible edge - black under a red mark draws as a brown fringe, under a white box as a grey border.
    /// Within <see cref="ExactFillReach"/> of a visible pixel each one takes the colour of its nearest visible pixel
    /// (in steps across the grid, breadth-first from the visible edge); the rest, which no resampler mixes with the
    /// picture, take the colour last met along their row, or the nearest row's. Every pixel is visited a fixed
    /// number of times, so the fill is linear in the image. An image with no visible pixel is left as it is.</summary>
    private static void FillTransparentFromNearestOpaque(byte[] rgb, byte[] alpha, int width, int height)
    {
        var count = width * height;
        // A pixel has a colour once it is visible or the fill has reached it.
        var coloured = new bool[count];
        var queue = new int[count];
        int head = 0, tail = 0;
        for (int y = 0, i = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++, i++)
            {
                if (alpha[i] == 0) continue;
                coloured[i] = true;
                if (TouchesTransparent(alpha, i, x, y, width, height)) queue[tail++] = i;
            }
        }
        if (tail == 0) return;

        for (var reach = 0; reach < ExactFillReach && head < tail; reach++)
        {
            var levelEnd = tail;
            while (head < levelEnd)
            {
                var from = queue[head++];
                var y = from / width;
                var x = from - y * width;
                var at = from * RgbBytes;
                if (x > 0 && !coloured[from - 1]) tail = Take(rgb, coloured, queue, tail, at, from - 1);
                if (x < width - 1 && !coloured[from + 1]) tail = Take(rgb, coloured, queue, tail, at, from + 1);
                if (y > 0 && !coloured[from - width]) tail = Take(rgb, coloured, queue, tail, at, from - width);
                if (y < height - 1 && !coloured[from + width]) tail = Take(rgb, coloured, queue, tail, at, from + width);
            }
        }
        if (head < tail) FillTheRestAlongRows(rgb, coloured, width, height);
    }

    private static bool TouchesTransparent(byte[] alpha, int i, int x, int y, int width, int height) =>
        (x > 0 && alpha[i - 1] == 0) || (x < width - 1 && alpha[i + 1] == 0)
        || (y > 0 && alpha[i - width] == 0) || (y < height - 1 && alpha[i + width] == 0);

    // The transparent pixel takes the colour at byte offset `from` and joins the queue.
    private static int Take(byte[] rgb, bool[] coloured, int[] queue, int tail, int from, int to)
    {
        coloured[to] = true;
        var at = to * RgbBytes;
        rgb[at] = rgb[from];
        rgb[at + 1] = rgb[from + 1];
        rgb[at + 2] = rgb[from + 2];
        queue[tail] = to;
        return tail + 1;
    }

    /// <summary>The pixels the exact fill did not reach: each takes the colour last met along its row (the first
    /// one met, before it), and a row with no colour at all copies the nearest row above or below that has one.</summary>
    private static void FillTheRestAlongRows(byte[] rgb, bool[] coloured, int width, int height)
    {
        var rowColoured = new bool[height];
        var rowBytes = width * RgbBytes;
        for (var y = 0; y < height; y++)
        {
            var row = y * width;
            var last = -1;
            for (var x = 0; x < width; x++)
            {
                var i = row + x;
                if (coloured[i]) last = i;
                else if (last >= 0) Buffer.BlockCopy(rgb, last * RgbBytes, rgb, i * RgbBytes, RgbBytes);
            }
            if (last < 0) continue;
            rowColoured[y] = true;
            var first = row;
            while (!coloured[first]) first++;
            for (var i = row; i < first; i++) Buffer.BlockCopy(rgb, first * RgbBytes, rgb, i * RgbBytes, RgbBytes);
        }

        var source = -1;
        for (var y = 0; y < height; y++)
        {
            if (rowColoured[y]) source = y;
            else if (source >= 0) Buffer.BlockCopy(rgb, source * rowBytes, rgb, y * rowBytes, rowBytes);
        }
        var firstRow = Array.IndexOf(rowColoured, true);
        for (var y = 0; y < firstRow; y++) Buffer.BlockCopy(rgb, firstRow * rowBytes, rgb, y * rowBytes, rowBytes);
    }
}
