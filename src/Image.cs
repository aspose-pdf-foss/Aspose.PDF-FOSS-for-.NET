#nullable disable

using System.IO;

namespace Aspose.Pdf
{
    /// <summary>An image paragraph: a picture loaded from a file, a URL or a stream and laid out in the page flow (or at a fixed rectangle) when the document is built.</summary>
    public class Image : BaseParagraph
    {
        /// <summary>Creates an image paragraph with no source; set <c>File</c> or <c>ImageStream</c> before the page is built.</summary>
        public Image() { }

        /// <summary>Outer margins. Auto-initialized so callers can set
        /// <c>img.Margin.Top = 10</c> on a freshly-constructed Image.</summary>
        public new MarginInfo Margin { get; set; } = new MarginInfo();

        /// <summary>Gets or sets a stream holding the image data. When set, it is used instead of <c>File</c>; the stream is read from the start and its position is restored.</summary>
        public Stream ImageStream { get; set; }
        /// <summary>Gets or sets the path of the image file, or an http(s) URL that is downloaded when the page is built.</summary>
        public string File { get; set; }
        /// <summary>Gets or sets the drawn width in points. 0 (the default) uses the image's own width; when only one of <c>FixWidth</c> and <c>FixHeight</c> is set, the other axis keeps the image's own size.</summary>
        public double FixWidth { get; set; }
        /// <summary>Gets or sets the drawn height in points. 0 (the default) uses the image's own height; when only one of <c>FixWidth</c> and <c>FixHeight</c> is set, the other axis keeps the image's own size.</summary>
        public double FixHeight { get; set; }
        public double ImageScale { get; set; }
        public bool IsBlackWhite { get; set; }
        /// <summary>Gets or sets whether the image's embedded DPI sets its natural size. When <c>false</c> (the default) one pixel maps to one point; when <c>true</c> the DPI is honoured and an image wider than the content area is scaled down to fit, keeping its aspect ratio.</summary>
        public bool IsApplyResolution { get; set; }

        /// <summary>A colour painted under the image's box before it is drawn --
        /// the box being the image and its <see cref="Border"/> bands. Null (the
        /// default) paints nothing. Flow only.</summary>
        public Color BackgroundColor { get; set; }

        /// <summary>Rules drawn around the image, outside it: each side's band
        /// widens the box the image occupies, the way a bordered paragraph's do.
        /// Null (the default) is none. Flow only.</summary>
        public BorderInfo Border { get; set; }

        /// <summary>Where the picture is drawn, in page space, when the CALLER places
        /// it: the flow draws it there on the page it has reached when the image's
        /// turn comes, and moves nothing -- the paragraph after it lands where it
        /// would have without it. Null (the default) lets the flow place the picture.
        /// The box's own size is the rectangle's; Fix sizes and scaling are not read.</summary>
        public Rectangle FixedRectangle { get; set; }

        /// <summary>Whether a Fix box is the picture's size whatever the content band:
        /// one wider or taller than the band keeps it and runs off the page's edge
        /// instead of being brought down to the band. Off (the default) squashes it
        /// to the band on that axis. Flow only.</summary>
        public bool FixBoxIsExact { get; set; }

        /// <summary>Whether an image with no fixed size is scaled, its aspect kept,
        /// to the WIDTH of the content band, whatever its height then is -- up as
        /// well as down. Off (the default) draws it at its own size. Flow only.</summary>
        public bool FitToBandWidth { get; set; }

        /// <summary>Whether an image with no fixed size is scaled, its aspect kept,
        /// to fill the width of the content band or the height still free on the
        /// page below the cursor -- whichever binds first -- growing as well as
        /// shrinking. Off (the default) draws it at its own size. Flow only.</summary>
        public bool FitToRemainingArea { get; set; }
        /// <summary>Gets or sets a caption for the image. It is stored only and is not drawn by this library.</summary>
        public Aspose.Pdf.Text.TextFragment Title { get; set; }

        /// <summary>Pixel dimensions of the source image as a rectangle
        /// [0, 0, width, height], decoded from <see cref="ImageStream"/> or
        /// <see cref="File"/>. Returns a zero rectangle when the source is
        /// unset or its size can't be determined.</summary>
        public Aspose.Pdf.Rectangle BitmapSize
        {
            get
            {
                var (w, h) = ImageDimensions.Read(ReadSourceBytes());
                return new Aspose.Pdf.Rectangle(0, 0, w, h);
            }
        }

        /// <summary>Gets or sets the compression filter to use for the embedded image. It is stored only and has no effect in this library.</summary>
        public ImageFilterType ImageFilterType { get; set; }

        /// <summary>Optional in-memory pixel buffer (public-API compatibility).</summary>
        public BitmapInfo BitmapInfo { get; set; }

        /// <summary>Source file format hint.</summary>
        public ImageFileType FileType { get; set; } = ImageFileType.Unknown;

        public override object Clone() => MemberwiseClone();

        /// <summary>The source bytes behind this image: its <see cref="ImageStream"/>
        /// (rewound and restored, so a second layout pass still sees the data), a local
        /// <see cref="File"/>, or a remote one — <c>File</c> may name an http(s) URL,
        /// which is fetched. Null when the source is unset or cannot be read.</summary>
        /// <remarks>Every path that turns an Image paragraph into page content reads it
        /// through here. They each used to inline the stream-or-local-file pair, which is
        /// why a URL loaded in none of them.</remarks>
        internal byte[] ReadSourceBytes()
        {
            if (ImageStream is not null)
            {
                var pos = ImageStream.CanSeek ? ImageStream.Position : -1L;
                if (ImageStream.CanSeek) ImageStream.Position = 0;
                using var ms = new MemoryStream();
                ImageStream.CopyTo(ms);
                if (pos >= 0) ImageStream.Position = pos;
                return ms.ToArray();
            }
            if (string.IsNullOrEmpty(File)) return null;
            if (IsRemote(File)) return FetchRemote(File);
            return System.IO.File.Exists(File) ? System.IO.File.ReadAllBytes(File) : null;
        }

        /// <summary>True when <paramref name="source"/> names an http(s) URL rather than
        /// a path. Scheme-checked on purpose: a Windows path parses as an absolute URI
        /// too, under the <c>file</c> scheme.</summary>
        internal static bool IsRemote(string source) =>
            System.Uri.TryCreate(source, System.UriKind.Absolute, out var uri)
            && (uri.Scheme == System.Uri.UriSchemeHttp || uri.Scheme == System.Uri.UriSchemeHttps);

        // One shared client: a new HttpClient per fetch leaks sockets, and an image is
        // commonly placed several times in one document.
        private static readonly System.Net.Http.HttpClient Http =
            new() { Timeout = System.TimeSpan.FromSeconds(100) };

        // The bytes fetched for _cachedFor, so laying the same image out repeatedly - and
        // measuring it before drawing it - costs ONE request. Keyed by the File value so
        // re-pointing File refetches. A failed fetch caches null with its cause, which
        // RemoteFailure reports at save time.
        private string _cachedFor;
        private byte[] _cachedBytes;
        private System.Exception _cachedError;

        private byte[] FetchRemote(string url)
        {
            if (string.Equals(_cachedFor, url, System.StringComparison.Ordinal))
                return _cachedBytes;
            _cachedFor = url;
            _cachedError = null;
            try
            {
                _cachedBytes = Http.GetByteArrayAsync(url).GetAwaiter().GetResult();
            }
            catch (System.Exception e)
            {
                // Never throw from here: this runs behind property getters (BitmapSize)
                // and inside layout, where an unreachable host must degrade to "no
                // image", not tear down the save. Save-time validation reports it.
                _cachedBytes = null;
                _cachedError = e;
            }
            return _cachedBytes;
        }

        /// <summary>The transport error from the last remote fetch of the CURRENT
        /// <see cref="File"/>, or null when the source loaded (or was never remote).
        /// Fetches once and remembers, so asking does not repeat the request.</summary>
        internal System.Exception RemoteFailure()
        {
            if (string.IsNullOrEmpty(File) || !IsRemote(File)) return null;
            FetchRemote(File);
            return _cachedError;
        }

        /// <summary>Best-effort MIME type for a System.Drawing.Image; returns
        /// "application/octet-stream" when the type cannot be determined.</summary>
        public static string GetMimeType(System.Drawing.Image i) => "application/octet-stream";
    }

    /// <summary>Decodes the pixel dimensions of a raster image from its header
    /// bytes without a full decode — supports PNG, JPEG, GIF, BMP and TIFF.</summary>
    internal static class ImageDimensions
    {
        public static (double Width, double Height) Read(byte[] data)
        {
            if (data is null || data.Length < 8) return (0, 0);

            // PNG: 8-byte signature, then IHDR (width/height big-endian at offset 16).
            if (data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47)
            {
                if (data.Length >= 24)
                    return (BE32(data, 16), BE32(data, 20));
            }
            // GIF: "GIF8", logical screen width/height little-endian at offset 6.
            else if (data[0] == 0x47 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x38)
            {
                if (data.Length >= 10)
                    return (LE16(data, 6), LE16(data, 8));
            }
            // BMP: 'BM', width/height little-endian at offset 18/22.
            else if (data[0] == 0x42 && data[1] == 0x4D)
            {
                if (data.Length >= 26)
                    return (LE32(data, 18), System.Math.Abs(unchecked((int)LE32(data, 22))));
            }
            // JPEG: FFD8, scan segments for a Start-Of-Frame (C0..CF except C4/C8/CC).
            else if (data[0] == 0xFF && data[1] == 0xD8)
            {
                return ReadJpeg(data);
            }
            // TIFF: 'II'/'MM' byte-order mark then 0x002A; walk the first IFD.
            else if ((data[0] == 0x49 && data[1] == 0x49) || (data[0] == 0x4D && data[1] == 0x4D))
            {
                return ReadTiff(data);
            }
            return (0, 0);
        }

        private static (double, double) ReadJpeg(byte[] d)
        {
            int p = 2;
            while (p + 9 < d.Length)
            {
                if (d[p] != 0xFF) { p++; continue; }
                int marker = d[p + 1];
                p += 2;
                if (marker is 0xD8 or 0xD9 || (marker >= 0xD0 && marker <= 0xD7)) continue;
                if (p + 1 >= d.Length) break;
                int len = (d[p] << 8) | d[p + 1];
                // SOF markers carry the frame dimensions (skip DHT/JPG/DAC).
                if (marker >= 0xC0 && marker <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC)
                {
                    if (p + 6 < d.Length)
                        return ((d[p + 5] << 8) | d[p + 6], (d[p + 3] << 8) | d[p + 4]);
                }
                p += len;
            }
            return (0, 0);
        }

        private static (double, double) ReadTiff(byte[] d)
        {
            bool le = d[0] == 0x49;
            long ifd = U32(d, 4, le);
            if (ifd <= 0 || ifd + 2 > d.Length) return (0, 0);
            int count = (int)U16(d, (int)ifd, le);
            double w = 0, h = 0;
            for (int i = 0; i < count; i++)
            {
                int e = (int)ifd + 2 + i * 12;
                if (e + 12 > d.Length) break;
                int tag = (int)U16(d, e, le);
                int type = (int)U16(d, e + 2, le);
                long val = type == 3 ? U16(d, e + 8, le) : U32(d, e + 8, le); // SHORT vs LONG
                if (tag == 256) w = val;        // ImageWidth
                else if (tag == 257) h = val;   // ImageLength
            }
            return (w, h);
        }

        private static double BE32(byte[] d, int o) =>
            (uint)((d[o] << 24) | (d[o + 1] << 16) | (d[o + 2] << 8) | d[o + 3]);
        private static double LE32(byte[] d, int o) =>
            (uint)(d[o] | (d[o + 1] << 8) | (d[o + 2] << 16) | (d[o + 3] << 24));
        private static double LE16(byte[] d, int o) => d[o] | (d[o + 1] << 8);
        private static long U32(byte[] d, int o, bool le) => le
            ? (uint)(d[o] | (d[o + 1] << 8) | (d[o + 2] << 16) | (d[o + 3] << 24))
            : (uint)((d[o] << 24) | (d[o + 1] << 16) | (d[o + 2] << 8) | d[o + 3]);
        private static long U16(byte[] d, int o, bool le) => le
            ? d[o] | (d[o + 1] << 8)
            : (d[o] << 8) | d[o + 1];
    }

    /// <summary>Recognised image-source file types reported by Image.FileType.</summary>
    public enum ImageFileType
    {
        Unknown,
        Base64,
        Dicom,
        Svg,
    }
}
