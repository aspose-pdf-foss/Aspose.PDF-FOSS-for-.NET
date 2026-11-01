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
        /// <summary>The drawn width as a fraction of the content box the picture sits in (a
        /// cell's text box), its height following its own aspect; 0 (the default) sizes it by
        /// the Fix box or its own size. A converted page's `width: 50%` picture, resolved where
        /// the box is known: at layout, not when the paragraph is built. Table cells only.</summary>
        internal double BoxWidthFraction { get; set; }
        /// <summary>The most the drawn width may be, as a fraction of that content box (a converted
        /// page's `max-width: 100%`): a wider picture shrinks to it, keeping its aspect; 0 = no cap.
        /// Such a picture sets no floor on its column. Table cells only.</summary>
        internal double BoxMaxWidthFraction { get; set; }
        /// <summary>The most the drawn width may be, in points (a converted page's `max-width: 100px` on a picture
        /// sized as a fraction of its content box, whose width is known only at layout): a wider picture shrinks
        /// to it, keeping its aspect; 0 = no cap. Table cells only.</summary>
        internal double MaxDrawWidth { get; set; }
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
        /// which is fetched. Null when the source is unset; a local file that does not
        /// exist throws.</summary>
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
                return Utf16MarkupAsUtf8(ms.ToArray());
            }
#pragma warning disable S1168 // null means "none", distinct from an empty result
            if (string.IsNullOrEmpty(File)) return null;
#pragma warning restore S1168
            if (IsRemote(File)) return FetchRemote(File);
            // A file that is not there is the caller's error, not an image to leave out: the
            // document used to save without it and without a word.
            if (!System.IO.File.Exists(File))
                throw new FileNotFoundException($"The image file '{File}' was not found.", File);
            return Utf16MarkupAsUtf8(System.IO.File.ReadAllBytes(File));
        }

        private const int ByteOrderMarkLength = 2;

        /// <summary>An SVG saved as UTF-16 (told by its byte-order mark), as the UTF-8 every
        /// reader of the source bytes takes markup in; any other source as it is. Read as
        /// UTF-8, such a file is no SVG at all and the image came out blank or missing.</summary>
        private static byte[] Utf16MarkupAsUtf8(byte[] data)
        {
            if (data.Length <= ByteOrderMarkLength) return data;
            System.Text.Encoding encoding;
            if (data[0] == 0xFF && data[1] == 0xFE) encoding = System.Text.Encoding.Unicode;
            else if (data[0] == 0xFE && data[1] == 0xFF) encoding = System.Text.Encoding.BigEndianUnicode;
            else return data;
            var text = encoding.GetString(data, ByteOrderMarkLength, data.Length - ByteOrderMarkLength);
            if (!text.TrimStart().StartsWith("<", System.StringComparison.Ordinal)) return data;
            // The declaration still names the old encoding, which an XML reader would hold the bytes to.
            text = SafeRegex.Replace(text, "^(\\s*<\\?xml[^>]*?)\\s+encoding\\s*=\\s*(\"[^\"]*\"|'[^']*')", "$1");
            return System.Text.Encoding.UTF8.GetBytes(text);
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
            // BMP: 'BM', then the DIB header, whose size it opens with: an OS/2 core header holds a
            // 16-bit width and height at offsets 18 and 20, any longer one a 32-bit pair at 18 and
            // 22, the height signed - negative for rows stored top to bottom.
            else if (data[0] == 0x42 && data[1] == 0x4D)
            {
                if (data.Length >= 26)
                    return LE32(data, 14) == BmpCoreHeaderSize
                        ? (LE16(data, 18), LE16(data, 20))
                        : (LE32(data, 18), System.Math.Abs((double)unchecked((int)U32(data, 22, le: true))));
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

        /// <summary>The resolution a file that states none is read at, as the platform image codec reads it.</summary>
        private const float UnstatedDpi = 96;

        private const double InchesPerMetre = 0.0254;
        private const double CentimetresPerInch = 2.54;

        /// <summary>The pHYs unit that makes its values pixels per metre (the other, 0, an aspect ratio only).</summary>
        private const byte PhysUnitMetre = 1;

        // JFIF's density units, which an EXIF ResolutionUnit restates.
        private const int NoUnit = 0;
        private const int DotsPerInch = 1;
        private const int DotsPerCentimetre = 2;

        // TIFF (and EXIF) resolution tags, the RATIONAL type they hold and the ResolutionUnit values.
        private const int TiffXResolution = 282;
        private const int TiffYResolution = 283;
        private const int TiffResolutionUnit = 296;
        private const int TiffRational = 5;
        private const int TiffShort = 3;
        private const int TiffInch = 2;
        private const int TiffCentimetre = 3;

        /// <summary>The resolution an image states, read the way the platform image codec reads it:
        /// a PNG's pHYs, a JPEG's JFIF and EXIF densities (each restating it, in file order), a BMP's
        /// pixels per metre, a TIFF's resolution tags - and 96 DPI where the file states none. In
        /// single precision, as the codec reports it.</summary>
        public static (float X, float Y) Resolution(byte[] data)
        {
            if (data is null || data.Length < 8) return (UnstatedDpi, UnstatedDpi);
            (float X, float Y)? stated = null;
            if (data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47) stated = PngResolution(data);
            else if (data[0] == 0xFF && data[1] == 0xD8) stated = JpegResolution(data);
            else if (data[0] == 0x42 && data[1] == 0x4D) stated = BmpResolution(data);
            else if ((data[0] == 0x49 && data[1] == 0x49) || (data[0] == 0x4D && data[1] == 0x4D)) stated = TiffResolution(data);
            return stated is { X: > 0, Y: > 0 } resolution ? resolution : (UnstatedDpi, UnstatedDpi);
        }

        /// <summary>A PNG's pHYs chunk, when its unit is the metre; the chunk precedes the image data.</summary>
        private static (float, float)? PngResolution(byte[] d)
        {
            for (int i = 8; i + 12 <= d.Length;)
            {
                var length = U32(d, i, le: false);
                if (d[i + 4] == 'p' && d[i + 5] == 'H' && d[i + 6] == 'Y' && d[i + 7] == 's' && i + 17 <= d.Length)
                    return d[i + 16] == PhysUnitMetre ? PerMetre(U32(d, i + 8, le: false), U32(d, i + 12, le: false)) : null;
                if ((d[i + 4] == 'I' && d[i + 5] == 'D' && d[i + 6] == 'A' && d[i + 7] == 'T') || length > d.Length) break;
                i += 12 + (int)length;
            }
            return null;
        }

        /// <summary>A BMP's pixels per metre: its info header's biXPelsPerMeter and biYPelsPerMeter, which
        /// a header of 40 bytes or more holds. A shorter one (an OS/2 header) states none - the bytes
        /// there are its palette's or its pixels.</summary>
        private static (float, float)? BmpResolution(byte[] d)
            => d.Length >= 46 && U32(d, 14, le: true) >= BmpInfoHeaderSize
                ? PerMetre(unchecked((int)U32(d, 38, le: true)), unchecked((int)U32(d, 42, le: true))) : null;

        // The DIB header sizes: OS/2's core header, and the info header, the first to state a resolution.
        private const int BmpCoreHeaderSize = 12;
        private const int BmpInfoHeaderSize = 40;

        private static (float, float) PerMetre(long x, long y) => ((float)(x * InchesPerMetre), (float)(y * InchesPerMetre));

        /// <summary>A JPEG's density: its JFIF and EXIF segments up to the first scan, read in file order,
        /// each restating the unit and the values it carries - so the later one wins.</summary>
        private static (float, float)? JpegResolution(byte[] d)
        {
            var density = new JpegDensity();
            int p = 2;
            while (p + 4 <= d.Length)
            {
                if (d[p] != 0xFF) { p++; continue; }
                int marker = d[p + 1];
                if (marker == 0xFF) { p++; continue; } // a fill byte
                // SOI, TEM and the restart markers stand alone; the scan (SOS) or the end (EOI) ends the headers.
                if (marker is 0xD8 or 0x01 || (marker >= 0xD0 && marker <= 0xD7)) { p += 2; continue; }
                if (marker is 0xDA or 0xD9) break;
                int segment = (d[p + 2] << 8) | d[p + 3];
                if (segment < 2 || p + 2 + segment > d.Length) break;
                // APP0 JFIF: version@9, units@11, X density@12, Y density@14; APP1 EXIF: a TIFF structure@10.
                if (marker == 0xE0 && segment >= 16 && Ascii(d, p + 4, "JFIF\0"))
                {
                    density.Unit = d[p + 11];
                    density.X = (d[p + 12] << 8) | d[p + 13];
                    density.Y = (d[p + 14] << 8) | d[p + 15];
                }
                else if (marker == 0xE1 && segment >= 16 && Ascii(d, p + 4, "Exif\0\0"))
                    ReadExifDensity(d, p + 10, p + 2 + segment, density);
                p += 2 + segment;
            }
            return density.Unit switch
            {
                DotsPerInch => (density.X, density.Y),
                DotsPerCentimetre => ((float)(density.X * CentimetresPerInch), (float)(density.Y * CentimetresPerInch)),
                _ => null,
            };
        }

        /// <summary>A JPEG's density as its segments leave it: no unit and one dot each way until one says otherwise.</summary>
        private sealed class JpegDensity
        {
            public int Unit = NoUnit;
            public long X = 1;
            public long Y = 1;
        }

        /// <summary>The density an EXIF segment restates, from its first directory's resolution tags read
        /// as whole numbers: its ResolutionUnit, where it gives one, replaces the unit (inch, centimetre,
        /// any other none), its X and Y resolutions, where their denominators are not zero, the values.</summary>
        private static void ReadExifDensity(byte[] d, int tiff, int end, JpegDensity density)
        {
            var le = d[tiff] == 0x49;
            foreach (var (tag, type, value) in TiffEntries(d, tiff, end, le))
            {
                if (tag == TiffResolutionUnit)
                {
                    density.Unit = value switch { TiffInch => DotsPerInch, TiffCentimetre => DotsPerCentimetre, _ => NoUnit };
                }
                else if ((tag == TiffXResolution || tag == TiffYResolution) && type == TiffRational && tiff + value + 8 <= end)
                {
                    var numerator = U32(d, tiff + (int)value, le);
                    var denominator = U32(d, tiff + (int)value + 4, le);
                    if (denominator == 0) continue;
                    if (tag == TiffXResolution) density.X = numerator / denominator;
                    else density.Y = numerator / denominator;
                }
            }
        }

        /// <summary>A TIFF's resolution tags: X and Y as given, in inches unless the unit is the
        /// centimetre; none unless both are given.</summary>
        private static (float, float)? TiffResolution(byte[] d)
        {
            var le = d[0] == 0x49;
            double? x = null, y = null;
            long unit = TiffInch;
            foreach (var (tag, type, value) in TiffEntries(d, 0, d.Length, le))
            {
                if (tag == TiffResolutionUnit) unit = value;
                else if ((tag == TiffXResolution || tag == TiffYResolution) && type == TiffRational && value + 8 <= d.Length)
                {
                    var denominator = U32(d, (int)value + 4, le);
                    double? resolution = denominator == 0 ? null : (double)U32(d, (int)value, le) / denominator;
                    if (tag == TiffXResolution) x = resolution;
                    else y = resolution;
                }
            }
            if (x is not { } dpiX || y is not { } dpiY) return null;
            var scale = unit == TiffCentimetre ? CentimetresPerInch : 1;
            return ((float)(dpiX * scale), (float)(dpiY * scale));
        }

        /// <summary>The entries of the first directory of the TIFF structure at <paramref name="start"/> (a
        /// TIFF file, or an EXIF segment's body): tag, type, and the value field as a number - a SHORT's
        /// value, else the LONG value or the offset (counted from <paramref name="start"/>) it holds.</summary>
        private static IEnumerable<(int Tag, int Type, long Value)> TiffEntries(byte[] d, int start, int end, bool le)
        {
            if (start + 8 > end) yield break;
            var directory = start + U32(d, start + 4, le);
            if (directory + 2 > end) yield break;
            var count = (int)U16(d, (int)directory, le);
            for (var i = 0; i < count; i++)
            {
                var entry = (int)directory + 2 + i * 12;
                if (entry + 12 > end) yield break;
                var type = (int)U16(d, entry + 2, le);
                var value = type == TiffShort ? U16(d, entry + 8, le) : U32(d, entry + 8, le);
                yield return ((int)U16(d, entry, le), type, value);
            }
        }

        private static bool Ascii(byte[] d, int at, string text)
        {
            if (at + text.Length > d.Length) return false;
            for (var i = 0; i < text.Length; i++)
                if (d[at + i] != text[i]) return false;
            return true;
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
                if (marker >= 0xC0 && marker <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC && p + 6 < d.Length)
                    return ((d[p + 5] << 8) | d[p + 6], (d[p + 3] << 8) | d[p + 4]);
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
