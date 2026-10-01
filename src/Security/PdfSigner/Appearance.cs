using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Security;

public sealed partial class PdfSigner
{
    /// <summary>
    /// Sign a PDF document with an X.509 certificate and a visible signature appearance.
    /// Returns the signed PDF bytes.
    /// </summary>
    public static byte[] SignWithAppearance(byte[] pdfData, PdfCertificate certificate,
        SignatureOptions? options, SignatureAppearance appearance)
    {
        var sa = new SignAppearanceState();
        sa.pdfData = pdfData;
        sa.certificate = certificate;
        options ??= new SignatureOptions();
        sa.options = options;
        sa.appearance = appearance;
        Compat.ThrowIfNull(sa.appearance);
        sa.contentsSize = sa.options.ContentsSize;

        // The banner's first line names the signer; without an explicit signer
        // name it shows the certificate's full subject DN.
        sa.appearance.SignerName ??= sa.certificate.SubjectDn;
        // The signature properties always ride on the visible banner.
        sa.appearance.Reason ??= sa.options.Reason;
        sa.appearance.Location ??= sa.options.Location;
        sa.appearance.ContactInfo ??= sa.options.ContactInfo;

        using var doc = OpenDoc(sa.pdfData, sa.options.Password);
        sa.doc = doc;
        sa.reader = sa.doc.Reader;
        sa.trailer = sa.reader.Trailer;
        sa.xref = sa.reader.XRefTable;

        AllocateAppearanceObjects(sa);

        using var ms = new MemoryStream();
        sa.ms = ms;
        WriteAppearanceSignatureObjects(sa);

        sa.pageRef = FindPageRef(sa.reader, Math.Max(0, sa.appearance.PageNumber - 1));
        LinkAppearanceWidgetToPage(sa);

        WriteAppearanceCatalogAndXref(sa);

        EmbedAppearanceSignature(sa);

        return sa.fileBytes;
    }

    private static PdfDictionary BuildSignatureFieldWithAppearance(
        string fieldName, int sigValObjNum, int appearanceStreamObjNum,
        Document doc, SignatureAppearance appearance)
    {
        var dict = new PdfDictionary();
        dict.Set("Type", new PdfName("Annot"));
        dict.Set("Subtype", new PdfName("Widget"));
        dict.Set("FT", new PdfName("Sig"));
        dict.Set("T", new PdfString(Compat.Latin1.GetBytes(fieldName)));
        dict.Set("V", new PdfIndirectRef(sigValObjNum, 0));

        // Set rect from appearance
        var rect = new PdfArray();
        if (appearance.Rect is not null)
        {
            rect.Add(new PdfReal(appearance.Rect.LLX));
            rect.Add(new PdfReal(appearance.Rect.LLY));
            rect.Add(new PdfReal(appearance.Rect.URX));
            rect.Add(new PdfReal(appearance.Rect.URY));
        }
        else
        {
            rect.Add(new PdfInteger(0));
            rect.Add(new PdfInteger(0));
            rect.Add(new PdfInteger(0));
            rect.Add(new PdfInteger(0));
        }
        dict.Set("Rect", rect);
        dict.Set("F", new PdfInteger(4)); // Print flag

        // AP — appearance dictionary with /N (normal) pointing to form XObject
        var apDict = new PdfDictionary();
        apDict.Set("N", new PdfIndirectRef(appearanceStreamObjNum, 0));
        dict.Set("AP", apDict);

        // Page reference (0-based index from 1-based PageNumber)
        var pageIndex = Math.Max(0, appearance.PageNumber - 1);
        if (doc.PageCount > pageIndex)
        {
            var pagesDict = doc.Reader.ResolveDict(doc.Reader.Catalog.Get("Pages"));
            if (pagesDict is not null)
            {
                var kids = doc.Reader.Resolve(pagesDict.Get("Kids")) as PdfArray;
                if (kids is not null && pageIndex < kids.Count && kids[pageIndex] is PdfIndirectRef pageRef)
                {
                    dict.Set("P", pageRef);
                }
            }
        }

        return dict;
    }

    /// <summary>Indirect reference of the pageIndex-th page leaf (0-based), by a
    /// recursive walk of the page tree.</summary>
    private static PdfIndirectRef? FindPageRef(IO.PdfReader reader, int pageIndex)
    {
        var pagesRoot = reader.ResolveDict(reader.Catalog.Get("Pages"));
        if (pagesRoot is null) return null;
        return WalkPageTree(reader, pagesRoot, pageIndex, 0, depth: 0).page;
    }

    /// <returns>The leaf at the target index when this subtree holds it, and the leaves counted so far.</returns>
    private static (PdfIndirectRef? page, int counter) WalkPageTree(IO.PdfReader reader, PdfDictionary node,
        int target, int counter, int depth)
    {
        if (depth > 64) return (null, counter);
        if (reader.Resolve(node.Get("Kids")) is not PdfArray kids) return (null, counter);
        foreach (var kid in kids)
        {
            if (kid is not PdfIndirectRef kref) continue;
            var kd = reader.ResolveDict(kref);
            if (kd is null) continue;
            if (kd.GetName("Type") == "Pages")
            {
                PdfIndirectRef? r;
                (r, counter) = WalkPageTree(reader, kd, target, counter, depth + 1);
                if (r is not null) return (r, counter);
            }
            else
            {
                if (counter == target) return (kref, counter);
                counter++;
            }
        }
        return (null, counter);
    }

    // The visible-signature banner's text colour (a light blue).
    private const string BannerFillRgb = "0.301960784313725 0.501960784313725 1";

    private const string BannerStrokeRgb = "0.3 0.5 1";

    /// <summary>
    /// Build a Form XObject appearance stream showing signature details: a
    /// left-aligned block of banner lines — signer, date, then any of
    /// reason/location/contact that are present — set with the text object's
    /// leading (fontSize × 1.2) from the box top, in the banner blue.
    /// Returns a dictionary with stream content stored under the "__StreamData" key
    /// (used by WriteStreamObject).
    /// </summary>
    internal static PdfDictionary BuildSignatureAppearanceStream(SignatureAppearance appearance)
    {
        var r = appearance.Rect ?? new Rectangle(0, 0, 200, 100);
        var width = r.Width;
        var height = r.Height;
        var fontSize = appearance.FontSize;

        // The same rows the CID banner draws (a caller-supplied UTC instant is shown as
        // local wall-clock time with its offset; an outright date shows bare).
        var lines = SignatureBanner.Lines(appearance.SignerName, appearance.SignDate ?? DateTime.Now,
            appearance.Reason, appearance.Location, appearance.ContactInfo, appearance.Labels);

        var sb = new StringBuilder();
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        sb.Append(BannerFillRgb).Append(" rg\n");
        sb.Append("q\n");
        sb.Append("1 0 0 1 0 0 cm\n");
        sb.Append("BT\n");
        // First baseline one font size below the box top; the leading advances
        // each further line via T*.
        sb.AppendFormat(inv, "1 0 0 1 0 {0:0.##} Tm\n", height - fontSize);
        sb.AppendFormat(inv, "{0:0.##} TL\n", fontSize * 1.2);
        sb.Append(BannerStrokeRgb).Append(" RG\n");
        sb.AppendFormat(inv, "/F1 {0:0.##} Tf\n", fontSize);
        foreach (var line in lines)
        {
            sb.AppendFormat("({0}) Tj\n", EscapePdfString(line));
            sb.Append("T*\n");
        }
        sb.Append("ET\n");
        sb.Append("Q\n");

        var streamContent = Compat.Latin1.GetBytes(sb.ToString());

        var dict = new PdfDictionary();
        dict.Set("Type", new PdfName("XObject"));
        dict.Set("Subtype", new PdfName("Form"));
        dict.Set("BBox", CreateBBoxArray(0, 0, width, height));
        dict.Set("Length", new PdfInteger(streamContent.Length));

        // Resources with the appearance font (a custom appearance may request a
        // specific family, e.g. "Times New Roman" → "TimesNewRoman"). The default is
        // Arial — the signer names the concrete host face rather than the
        // abstract Helvetica (a signed PDF/A must embed a concrete font, e.g. Arial).
        var baseFont = string.IsNullOrEmpty(appearance.FontFamily)
            ? "Arial"
            : appearance.FontFamily.Replace(" ", "");
        var fontDict = new PdfDictionary();
        var f1Dict = new PdfDictionary();
        f1Dict.Set("Type", new PdfName("Font"));
        f1Dict.Set("Subtype", new PdfName("Type1"));
        f1Dict.Set("BaseFont", new PdfName(baseFont));
        // The banner lines may carry accented text (a reason such as "Approuvé");
        // WinAnsi maps those Latin-1 codes to the right glyphs.
        f1Dict.Set("Encoding", new PdfName("WinAnsiEncoding"));
        fontDict.Set("F1", f1Dict);
        var resources = new PdfDictionary();
        resources.Set("Font", fontDict);
        dict.Set("Resources", resources);

        // Store stream data for WriteStreamObject
        dict.Set("__StreamData", new PdfString(streamContent));

        return dict;
    }

    private static PdfArray CreateBBoxArray(double x1, double y1, double x2, double y2)
    {
        var arr = new PdfArray();
        arr.Add(new PdfReal(x1));
        arr.Add(new PdfReal(y1));
        arr.Add(new PdfReal(x2));
        arr.Add(new PdfReal(y2));
        return arr;
    }
}
