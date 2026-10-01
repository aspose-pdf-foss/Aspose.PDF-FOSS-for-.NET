using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Security;

public sealed partial class PdfSigner
{
    /// <summary>Patches the ByteRange, hashes the covered bytes with the resolved digest, signs the hash (a custom signer or the PKCS#7 builder), checks the reserved size and writes the hex signature into the placeholder.</summary>
    private static void EmbedSignature(SignFlowState sg)
    {
        sg.byteRange = new long[]
        {
            0,
            sg.contentsOffset,
            sg.contentsOffset + sg.contentsLength,
            sg.fileBytes.Length - (sg.contentsOffset + sg.contentsLength)
        };

        // Step 4: Patch the ByteRange in the signature value dict
        // Find and replace the placeholder ByteRange
        PatchByteRange(sg.fileBytes, sg.byteRange);

        sg.digest = CmsBuilder.ResolveDigest(
            sg.certificate.KeyKind, sg.options.Digest, DigestForSubFilter(sg.options.SubFilter));
        sg.hashInput = new byte[(int)sg.byteRange[1] + (int)sg.byteRange[3]];
        Array.Copy(sg.fileBytes, 0, sg.hashInput, 0, (int)sg.byteRange[1]);
        Array.Copy(sg.fileBytes, (int)sg.byteRange[2], sg.hashInput, (int)sg.byteRange[1], (int)sg.byteRange[3]);
        sg.hash = HashByteRange(sg.hashInput, sg.digest);

        sg.signatureBytes = CreatePkcs7Signature(sg.hash, sg.certificate, sg.digest, sg.options.CustomSignHash);

        if (sg.signatureBytes.Length > sg.contentsSize)
            throw sg.options.AvoidEstimating
                ? new SignatureLengthMismatchException(sg.signatureBytes.Length)
                : new InvalidOperationException(
                    $"Signature ({sg.signatureBytes.Length} bytes) exceeds reserved space ({sg.contentsSize} bytes). " +
                    "Increase SignatureOptions.ContentsSize.");

        sg.hexSignature = Compat.ToHexString(sg.signatureBytes);
        // Pad with zeros to fill the reserved space
        sg.hexSignature = sg.hexSignature.PadRight(sg.contentsSize * 2, '0');

        sg.hexBytes = Encoding.ASCII.GetBytes(sg.hexSignature);
        Array.Copy(sg.hexBytes, 0, sg.fileBytes, (int)sg.contentsOffset + 1, sg.hexBytes.Length);
    }

    /// <summary>Writes the updated catalog (AcroForm, a DocMDP Perms entry, a DSS with the certificate stream under LTV), then the cross-reference section and trailer of the incremental update.</summary>
    private static void WriteCatalogAndXref(SignFlowState sg)
    {
        sg.catalogRef = sg.trailer.Get("Root") as PdfIndirectRef;
        sg.catalogObjNum = sg.catalogRef?.ObjectNumber ?? 1;
        sg.catalogDict = CloneDict(sg.reader.Catalog);
        sg.catalogDict.Set("AcroForm", new PdfIndirectRef(sg.acroFormObjNum, 0));

        // Certifying signature: catalog /Perms /DocMDP points at the sig value.
        if (sg.options.DocMdpPermissions is not null)
        {
            var perms = new PdfDictionary();
            perms.Set("DocMDP", new PdfIndirectRef(sg.sigValObjNum, 0));
            sg.catalogDict.Set("Perms", perms);
        }

        // LTV: embed the signer certificate in a /DSS (Document Security Store)
        // so the signature stays verifiable after the certificate expires.
        if (sg.options.UseLtv)
        {
            sg.offsets[sg.dssCertObjNum] = sg.ms.Position;
            WriteStreamObject(sg.ms, sg.dssCertObjNum, BuildCertStreamDict(sg.certificate.CertificateDer));
            sg.catalogDict.Set("DSS", BuildDssDict(sg.dssCertObjNum));
        }

        sg.offsets[sg.catalogObjNum] = sg.ms.Position;
        WriteIndirectObject(sg.ms, sg.catalogObjNum, sg.catalogDict);

        sg.originalStartXref = XRefTable.FindStartXref(sg.pdfData);
        WriteXRefAndTrailer(sg.ms, sg.offsets, sg.trailer, sg.nextObj, sg.originalStartXref);

        sg.fileBytes = sg.ms.ToArray();
    }

    /// <summary>When a direct (non-indirect) field is adopted, rewrites the first page's Annots so the adopted widget points at the new signature field object.</summary>
    private static void ReplaceAdoptedFieldAnnot(SignFlowState sg)
    {
        if (sg.adoptDirectField)
        {
            var annotPageRef = FindPageRef(sg.reader, 0);
            if (annotPageRef is not null && sg.reader.ResolveDict(annotPageRef) is { } annotPage
                && sg.reader.Resolve(annotPage.Get("Annots")) is PdfArray oldAnnotArr)
            {
                var newAnnots = new PdfArray();
                var replaced = false;
                foreach (var a in oldAnnotArr)
                {
                    if (!replaced && a is PdfDictionary ad
                        && (sg.reader.Resolve(ad.Get("T")) as PdfString)?.ToText() == sg.fieldName)
                    {
                        newAnnots.Add(new PdfIndirectRef(sg.sigFieldObjNum, 0));
                        replaced = true;
                        continue;
                    }
                    newAnnots.Add(a);
                }
                if (replaced)
                {
                    var newAnnotPage = CloneDict(annotPage);
                    newAnnotPage.Set("Annots", newAnnots);
                    sg.offsets[annotPageRef.ObjectNumber] = sg.ms.Position;
                    WriteIndirectObject(sg.ms, annotPageRef.ObjectNumber, newAnnotPage);
                }
            }
        }
    }

    /// <summary>Copies the original bytes and writes the signature value (with its contents placeholder), the signature field (with the banner appearance), the AcroForm and the banner objects, recording every offset.</summary>
    private static void WriteSignatureObjects(SignFlowState sg)
    {
        sg.ms.Write(sg.pdfData);

        sg.sigValDict = BuildSignatureValueDict(sg.certificate, sg.options, sg.contentsSize);

        sg.sigFieldDict = sg.existingFieldDict is not null
            ? BuildUpdatedSignatureFieldDict(sg.existingFieldDict, sg.sigValObjNum)
            : BuildSignatureFieldDict(sg.fieldName, sg.sigValObjNum, sg.doc);
        if (sg.banner is { } bannerAp)
        {
            var apDict = new PdfDictionary();
            apDict.Set("N", new PdfIndirectRef(bannerAp.ApObjNum, 0));
            sg.sigFieldDict.Set("AP", apDict);
        }

        sg.acroFormDict = BuildAcroFormDict(sg.doc, sg.sigFieldObjNum,
            appendField: sg.existingFieldDict is null,
            replaceFieldNamed: sg.adoptDirectField ? sg.fieldName : null,
            reader: sg.doc.Reader);

        sg.offsets = new Dictionary<int, long>();

        // Write sig value dict — we need to track the /Contents placeholder position
        sg.offsets[sg.sigValObjNum] = sg.ms.Position;
        (sg.sigValBytes, sg.contentsOffset, sg.contentsLength) = SerializeObject(sg.sigValObjNum, sg.sigValDict, sg.contentsSize);
        sg.ms.Write(sg.sigValBytes);

        // Adjust sg.contentsOffset to be relative to the full file
        sg.contentsOffset += sg.offsets[sg.sigValObjNum];

        // Write sig field
        sg.offsets[sg.sigFieldObjNum] = sg.ms.Position;
        WriteIndirectObject(sg.ms, sg.sigFieldObjNum, sg.sigFieldDict);

        // Write AcroForm
        sg.offsets[sg.acroFormObjNum] = sg.ms.Position;
        WriteIndirectObject(sg.ms, sg.acroFormObjNum, sg.acroFormDict);

        // The banner's own objects: the face, then the nested forms.
        if (sg.banner is { } bnObjs)
            foreach (var (num, dict, isStream) in bnObjs.Objects)
            {
                sg.offsets[num] = sg.ms.Position;
                if (isStream) WriteStreamObject(sg.ms, num, dict);
                else WriteIndirectObject(sg.ms, num, dict);
            }
    }

    /// <summary>Resolves the field name, allocates the object numbers of the incremental update (value, field, AcroForm, DSS) around an existing field and builds the widget banner.</summary>
    private static void AllocateSignatureObjects(SignFlowState sg)
    {
        sg.fieldName = ResolveSignatureFieldName(sg.doc, sg.options.FieldName);

        sg.maxObj = 0;
        foreach (var entry in sg.xref.Entries.Values)
            if (entry.ObjectNumber > sg.maxObj) sg.maxObj = entry.ObjectNumber;
        sg.nextObj = sg.maxObj + 1;

        // If the document already contains a (blank) signature field with this
        // name, reuse it — update its /V in place rather than appending a
        // duplicate field. Otherwise allocate a fresh field object.
        var existingField = FindTopLevelField(sg.doc, sg.fieldName);
        sg.existingFieldRef = existingField.Ref;
        sg.existingFieldDict = existingField.Dict;
        sg.adoptDirectField = sg.existingFieldRef is null && sg.existingFieldDict is not null;

        sg.sigValObjNum = sg.nextObj++;
        sg.sigFieldObjNum = sg.existingFieldRef is not null
            ? sg.existingFieldRef.ObjectNumber
            : sg.nextObj++;
        sg.acroFormObjNum = sg.nextObj++;
        sg.dssCertObjNum = sg.options.UseLtv ? sg.nextObj++ : 0;

        sg.banner = sg.existingFieldDict is not null
            ? BuildBannerObjects(sg.existingFieldDict, sg.reader, sg.options, sg.certificate, sg.nextObj)
            : null;
        if (sg.banner is { } built) sg.nextObj = built.NextObj;
    }
}
