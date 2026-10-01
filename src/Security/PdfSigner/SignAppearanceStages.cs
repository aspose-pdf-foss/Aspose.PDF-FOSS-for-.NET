using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Security;

public sealed partial class PdfSigner
{
    /// <summary>Patches the ByteRange, hashes the covered bytes, signs (custom signer or PKCS#7), checks the reserved size and writes the hex signature into the placeholder.</summary>
    private static void EmbedAppearanceSignature(SignAppearanceState sa)
    {
        sa.byteRange = new long[]
        {
            0,
            sa.contentsOffset,
            sa.contentsOffset + sa.contentsLength,
            sa.fileBytes.Length - (sa.contentsOffset + sa.contentsLength)
        };

        PatchByteRange(sa.fileBytes, sa.byteRange);

        sa.digest = CmsBuilder.ResolveDigest(
            sa.certificate.KeyKind, sa.options.Digest, DigestForSubFilter(sa.options.SubFilter));
        sa.hashInput = new byte[(int)sa.byteRange[1] + (int)sa.byteRange[3]];
        Array.Copy(sa.fileBytes, 0, sa.hashInput, 0, (int)sa.byteRange[1]);
        Array.Copy(sa.fileBytes, (int)sa.byteRange[2], sa.hashInput, (int)sa.byteRange[1], (int)sa.byteRange[3]);
        sa.hash = HashByteRange(sa.hashInput, sa.digest);

        sa.signatureBytes = CreatePkcs7Signature(sa.hash, sa.certificate, sa.digest, sa.options.CustomSignHash);

        if (sa.signatureBytes.Length > sa.contentsSize)
            throw sa.options.AvoidEstimating
                ? new SignatureLengthMismatchException(sa.signatureBytes.Length)
                : new InvalidOperationException(
                    $"Signature ({sa.signatureBytes.Length} bytes) exceeds reserved space ({sa.contentsSize} bytes).");

        sa.hexSignature = Compat.ToHexString(sa.signatureBytes);
        sa.hexSignature = sa.hexSignature.PadRight(sa.contentsSize * 2, '0');
        sa.hexBytes = Encoding.ASCII.GetBytes(sa.hexSignature);
        Array.Copy(sa.hexBytes, 0, sa.fileBytes, (int)sa.contentsOffset + 1, sa.hexBytes.Length);
    }

    /// <summary>Writes the updated catalog (AcroForm, DocMDP perms, DSS under LTV), then the cross-reference section and trailer.</summary>
    private static void WriteAppearanceCatalogAndXref(SignAppearanceState sa)
    {
        sa.catalogRef = sa.trailer.Get("Root") as PdfIndirectRef;
        sa.catalogObjNum = sa.catalogRef?.ObjectNumber ?? 1;
        sa.catalogDict = CloneDict(sa.reader.Catalog);
        sa.catalogDict.Set("AcroForm", new PdfIndirectRef(sa.acroFormObjNum, 0));

        if (sa.options.DocMdpPermissions is not null)
        {
            var perms = new PdfDictionary();
            perms.Set("DocMDP", new PdfIndirectRef(sa.sigValObjNum, 0));
            sa.catalogDict.Set("Perms", perms);
        }

        if (sa.options.UseLtv)
        {
            sa.offsets[sa.dssCertObjNum] = sa.ms.Position;
            WriteStreamObject(sa.ms, sa.dssCertObjNum, BuildCertStreamDict(sa.certificate.CertificateDer));
            sa.catalogDict.Set("DSS", BuildDssDict(sa.dssCertObjNum));
        }

        sa.offsets[sa.catalogObjNum] = sa.ms.Position;
        WriteIndirectObject(sa.ms, sa.catalogObjNum, sa.catalogDict);

        sa.originalStartXref = XRefTable.FindStartXref(sa.pdfData);
        WriteXRefAndTrailer(sa.ms, sa.offsets, sa.trailer, sa.nextObj, sa.originalStartXref);

        sa.fileBytes = sa.ms.ToArray();
    }

    /// <summary>Adds the signature widget to its page's Annots and rewrites that page object.</summary>
    private static void LinkAppearanceWidgetToPage(SignAppearanceState sa)
    {
        if (sa.pageRef is not null && sa.reader.ResolveDict(sa.pageRef) is { } sigPageDict)
        {
            var newPage = CloneDict(sigPageDict);
            var annots = new PdfArray();
            if (sa.reader.Resolve(sigPageDict.Get("Annots")) is PdfArray oldAnnots)
                foreach (var a in oldAnnots) annots.Add(a);
            annots.Add(new PdfIndirectRef(sa.sigFieldObjNum, 0));
            newPage.Set("Annots", annots);
            sa.offsets[sa.pageRef.ObjectNumber] = sa.ms.Position;
            WriteIndirectObject(sa.ms, sa.pageRef.ObjectNumber, newPage);
        }
    }

    /// <summary>Copies the original bytes and writes the signature value, the widget field with its appearance, the AcroForm, the appearance stream and the banner objects, recording every offset; an encrypted document gets its objects through the append decryptor.</summary>
    private static void WriteAppearanceSignatureObjects(SignAppearanceState sa)
    {
        sa.ms.Write(sa.pdfData);

        sa.sigValDict = BuildSignatureValueDict(sa.certificate, sa.options, sa.contentsSize);
        sa.visRect = sa.appearance.Rect;
        sa.visBanner = BuildBannerObjects(
            sa.visRect is null ? 0 : Math.Abs(sa.visRect.URX - sa.visRect.LLX),
            sa.visRect is null ? 0 : Math.Abs(sa.visRect.URY - sa.visRect.LLY),
            sa.options, sa.certificate, sa.nextObj,
            sa.appearance.FontFamily, sa.appearance.FontSize, sa.appearance);
        if (sa.visBanner is { } visBuilt) sa.nextObj = visBuilt.NextObj;
        sa.sigFieldDict = BuildSignatureFieldWithAppearance(
            sa.fieldName, sa.sigValObjNum,
            sa.visBanner?.ApObjNum ?? sa.appearanceStreamObjNum, sa.doc, sa.appearance);
        sa.acroFormDict = BuildAcroFormDict(sa.doc, sa.sigFieldObjNum);
        sa.appearanceStreamDict = sa.visBanner is null
            ? BuildSignatureAppearanceStream(sa.appearance)
            : null;

        sa.appendDecryptor = sa.reader.Decryptor;
        if (sa.appendDecryptor is not null)
        {
            // Encrypt the signer-supplied text (/Reason /Location /ContactInfo /M …)
            // and the visible appearance content. The field dict's /T is left as-is
            // because signature-name lookup reads it structurally during verify.
            EncryptAppendedDict(sa.appendDecryptor, sa.sigValObjNum, sa.sigValDict);
            if (sa.appearanceStreamDict is not null)
                EncryptAppendedStream(sa.appendDecryptor, sa.appearanceStreamObjNum, sa.appearanceStreamDict);
            if (sa.visBanner is { } encBanner)
                foreach (var (num, dict, isStream) in encBanner.Objects)
                {
                    if (isStream) EncryptAppendedStream(sa.appendDecryptor, num, dict);
                    else EncryptAppendedDict(sa.appendDecryptor, num, dict);
                }
        }

        sa.offsets = new Dictionary<int, long>();

        sa.offsets[sa.sigValObjNum] = sa.ms.Position;
        (sa.sigValBytes, sa.contentsOffset, sa.contentsLength) = SerializeObject(sa.sigValObjNum, sa.sigValDict, sa.contentsSize);
        sa.ms.Write(sa.sigValBytes);
        sa.contentsOffset += sa.offsets[sa.sigValObjNum];

        sa.offsets[sa.sigFieldObjNum] = sa.ms.Position;
        WriteIndirectObject(sa.ms, sa.sigFieldObjNum, sa.sigFieldDict);

        sa.offsets[sa.acroFormObjNum] = sa.ms.Position;
        WriteIndirectObject(sa.ms, sa.acroFormObjNum, sa.acroFormDict);

        // Write appearance stream object (stream with dict)
        if (sa.appearanceStreamDict is not null)
        {
            sa.offsets[sa.appearanceStreamObjNum] = sa.ms.Position;
            WriteStreamObject(sa.ms, sa.appearanceStreamObjNum, sa.appearanceStreamDict);
        }
        if (sa.visBanner is { } visObjs)
            foreach (var (num, dict, isStream) in visObjs.Objects)
            {
                sa.offsets[num] = sa.ms.Position;
                if (isStream) WriteStreamObject(sa.ms, num, dict);
                else WriteIndirectObject(sa.ms, num, dict);
            }
    }

    /// <summary>Resolves the field name and allocates the object numbers of the update (value, field, AcroForm, appearance stream, DSS), picking the signature page.</summary>
    private static void AllocateAppearanceObjects(SignAppearanceState sa)
    {
        sa.fieldName = ResolveSignatureFieldName(sa.doc, sa.options.FieldName);

        sa.maxObj = 0;
        foreach (var entry in sa.xref.Entries.Values)
            if (entry.ObjectNumber > sa.maxObj) sa.maxObj = entry.ObjectNumber;
        sa.nextObj = sa.maxObj + 1;

        sa.sigValObjNum = sa.nextObj++;
        sa.sigFieldObjNum = sa.nextObj++;
        sa.acroFormObjNum = sa.nextObj++;
        sa.appearanceStreamObjNum = sa.nextObj++;
        sa.dssCertObjNum = sa.options.UseLtv ? sa.nextObj++ : 0;
    }
}
