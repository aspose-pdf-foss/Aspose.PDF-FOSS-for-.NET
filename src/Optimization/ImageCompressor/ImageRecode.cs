using System.Runtime.ExceptionServices;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Optimization;

internal static partial class ImageCompressor
{
    /// <summary>One image as a pass sees it before the recode: everything the reader is asked
    /// (decoded samples, colour space, mask role) is gathered here on the calling thread, so
    /// the recode itself reads only this and can run beside the other images'.</summary>
    private sealed class ImageJob
    {
        public PdfStream Stream = default!;
        public byte[] Raw = [];
        public int Width, Height, Bpc, Components, Quality, MaxDpi;
        public string? FilterName, ColorSpaceName;
        public bool SoleFilter;          // /Filter is one name: Raw is the codec's own stream
        public bool ImageMask;           // a stencil mask
        public bool IsMask;              // a stencil mask, or another image's /SMask or /Mask
        public bool HasColorKeyMask;     // /Mask is a colour-key range
        public byte[]? Decoded;          // the decoded samples; null when the stream is undecodable
        public CsKind Kind, IndexBase;
        public byte[]? Lookup;
        public bool GrayOrCcitt, InvertBilevel, Indexed;
        public int NewWidth, NewHeight;
        public double DrawnWidth, DrawnHeight; // the largest size a page draws the image at, in points
        public Resample Route;
    }

    /// <summary>A recoded image: the new data and the entries that describe it, set on the
    /// stream once its batch is done.</summary>
    private sealed record ImageRecode(byte[] Data, string Filter, PdfObject? ColorSpace = null,
        int? BitsPerComponent = null, int? Width = null, int? Height = null, bool DropDecode = false)
    {
        /// <summary>/DecodeParms described the old filter and always goes; /Decode goes only
        /// when the samples now mean something else.</summary>
        public void Apply(PdfStream stream)
        {
            stream.ReplaceData(Data);
            stream.Dict.Set("Filter", new PdfName(Filter));
            stream.Dict.Set("Length", new PdfInteger(Data.Length));
            if (Width is { } w) stream.Dict.Set("Width", new PdfInteger(w));
            if (Height is { } h) stream.Dict.Set("Height", new PdfInteger(h));
            if (ColorSpace is not null) stream.Dict.Set("ColorSpace", ColorSpace);
            if (BitsPerComponent is { } bpc) stream.Dict.Set("BitsPerComponent", new PdfInteger(bpc));
            stream.Dict.Remove("DecodeParms");
            if (DropDecode) stream.Dict.Remove("Decode");
        }
    }

    /// <summary>The images of a document are independent of each other, so a pass recodes
    /// them a batch at a time: the reader is consulted for the batch on the calling thread (it
    /// is not thread-safe), the batch is recoded in parallel, and the results are set on the
    /// streams in order. The batch size bounds the threads and the decoded images held at once.</summary>
    private const int MaxRecodesInFlight = 8;

    private static void RecodeAll(List<PdfStream> streams, Func<PdfStream, ImageJob?> prepare,
        Func<ImageJob, ImageRecode?> recode)
    {
        var batchSize = Math.Min(Environment.ProcessorCount, MaxRecodesInFlight);
        var batch = new List<ImageJob>(batchSize);
        foreach (var stream in streams)
        {
            if (prepare(stream) is { } job) batch.Add(job);
            if (batch.Count == batchSize) RecodeBatch(batch, recode);
        }
        RecodeBatch(batch, recode);
    }

    private static void RecodeBatch(List<ImageJob> batch, Func<ImageJob, ImageRecode?> recode)
    {
        var results = new ImageRecode?[batch.Count];
        try
        {
            Parallel.For(0, batch.Count, new ParallelOptions { MaxDegreeOfParallelism = MaxRecodesInFlight },
                i => results[i] = recode(batch[i]));
        }
        catch (AggregateException e) when (e.InnerExceptions.Count == 1)
        {
            // The recode's own exception, as a loop over the images would have thrown it.
            ExceptionDispatchInfo.Capture(e.InnerExceptions[0]).Throw();
        }
        for (var i = 0; i < batch.Count; i++) results[i]?.Apply(batch[i].Stream);
        batch.Clear();
    }

    /// <summary>What every pass asks of an image before it decides anything. Null for an
    /// image no pass touches: JPEG 2000 and JBIG2 have no managed re-encoder, and a
    /// degenerate size has nothing to recode.</summary>
    private static ImageJob? DescribeImage(PdfStream stream, PdfReader reader, HashSet<PdfStream> maskStreams,
        int quality, int maxDpi)
    {
        var filterName = GetFilterName(stream);
        if (filterName is "JPXDecode" or "JBIG2Decode") return null;
        var width = (int)stream.Dict.GetInt("Width", 0);
        var height = (int)stream.Dict.GetInt("Height", 0);
        if (width <= 0 || height <= 0) return null;

        var imageMask = stream.Dict.GetBool("ImageMask");
        return new ImageJob
        {
            Stream = stream,
            Raw = stream.RawData,
            Width = width,
            Height = height,
            Bpc = (int)stream.Dict.GetInt("BitsPerComponent", 8),
            Components = GetComponents(stream, reader),
            Quality = quality,
            MaxDpi = maxDpi,
            FilterName = filterName,
            ColorSpaceName = stream.Dict.GetName("ColorSpace"),
            SoleFilter = stream.Dict.Get("Filter") is not PdfArray,
            ImageMask = imageMask,
            IsMask = imageMask || maskStreams.Contains(stream),
            HasColorKeyMask = reader.Resolve(stream.Dict.Get("Mask")) is PdfArray,
        };
    }

    private static byte[]? SafeDecode(PdfStream st, PdfReader reader)
    {
        try { return reader.DecodeStream(st); }
        catch { return null; }
    }

    /// <summary>Gray samples as packed RGB, the layout the palette and JPEG encoders take.</summary>
    private static byte[] ExpandGrayToRgb(byte[] gray, int pixels)
    {
        var rgb = new byte[pixels * 3];
        for (var i = 0; i < pixels; i++)
        {
            var v = gray[i];
            rgb[i * 3] = v; rgb[i * 3 + 1] = v; rgb[i * 3 + 2] = v;
        }
        return rgb;
    }

    /// <summary>A packed-RGB buffer as a 4:2:0 JPEG at the given quality; empty when the
    /// encoder refuses the image.</summary>
    private static byte[] EncodeRgbJpeg(byte[] rgb, int width, int height, int quality)
    {
        try
        {
            return JpegEncoderImpl.Encode((int x, int y) =>
            {
                var idx = (y * width + x) * 3;
                return (rgb[idx], rgb[idx + 1], rgb[idx + 2]);
            }, width, height, quality);
        }
        catch
        {
            return [];
        }
    }
}
