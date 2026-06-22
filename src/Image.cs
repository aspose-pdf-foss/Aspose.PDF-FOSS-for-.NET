#nullable disable

using System.IO;

namespace Aspose.Pdf
{
    public class Image : BaseParagraph
    {
        /// <summary>Outer margins. Auto-initialized so callers can set
        /// <c>img.Margin.Top = 10</c> on a freshly-constructed Image.</summary>
        public new MarginInfo Margin { get; set; } = new MarginInfo();

        public Stream ImageStream { get; set; }
        public string File { get; set; }
        public double FixWidth { get; set; }
        public double FixHeight { get; set; }
        public double ImageScale { get; set; }
        public bool IsBlackWhite { get; set; }
        public bool IsApplyResolution { get; set; }
        public Aspose.Pdf.Text.TextFragment Title { get; set; }
        public Aspose.Pdf.Rectangle BitmapSize { get; }
        public ImageFilterType ImageFilterType { get; set; }

        /// <summary>Optional in-memory pixel buffer (Aspose.PDF for .NET parity).</summary>
        public BitmapInfo BitmapInfo { get; set; }

        /// <summary>Source file format hint.</summary>
        public ImageFileType FileType { get; set; } = ImageFileType.Unknown;

        public override object Clone() => MemberwiseClone();

        /// <summary>Best-effort MIME type for a System.Drawing.Image; returns
        /// "application/octet-stream" when the type cannot be determined.</summary>
        public static string GetMimeType(System.Drawing.Image i) => "application/octet-stream";
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
