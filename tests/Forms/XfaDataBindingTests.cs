using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using Aspose.Pdf;
using Xunit;

namespace Aspose.Pdf.Tests.Forms;

public class XfaDataBindingTests
{
    private const string TemplateNs = "http://www.xfa.org/schema/xfa-template/3.3/";
    private const string DataNs = "http://www.xfa.org/schema/xfa-data/1.0/";

    /// <summary>The template: a presentation-only page subform over a row table (rows repeat), an applicant and a
    /// representative group, each with a firstName field bound by reference, an unbound button, and a
    /// repeated member subform.</summary>
    private const string Template =
        "<template xmlns=\"" + TemplateNs + "\"><subform name=\"form1\">"
        + "<subform name=\"page1\"><bind match=\"none\"/>"
        + "<subform name=\"rows\"><subform name=\"row\"><occur max=\"-1\"/><field name=\"date\"/></subform></subform>"
        + "<subform name=\"applicant\"><bind match=\"dataRef\" ref=\"$record.people.applicant\"/>"
        + "<field name=\"firstName\"><bind match=\"dataRef\" ref=\"firstName\"/></field>"
        + "<area name=\"box\"><field name=\"phone\"><bind match=\"dataRef\" ref=\"$.phone\"/></field></area>"
        + "<field><bind match=\"dataRef\" ref=\"$.title\"/></field></subform>"
        + "<subform name=\"representative\"><bind match=\"dataRef\" ref=\"$record.people.representative\"/>"
        + "<field name=\"firstName\"><bind match=\"dataRef\" ref=\"firstName\"/></field></subform>"
        + "<field name=\"addButton\"><bind match=\"none\"/></field>"
        + "<subform name=\"member\"><occur min=\"3\" max=\"-1\"/><field name=\"lastName\"/></subform>"
        + "</subform></subform></template>";

    private const string Data =
        "<form1><rows><row><date>first</date></row><row><date/></row><row><date/></row></rows>"
        + "<people><representative><firstName>Rep</firstName></representative></people>"
        + "<member><lastName/></member><member><lastName/></member><member><lastName/></member></form1>";

    private static byte[] BuildForm(string data = Data, string? record = null, string description = "")
    {
        var datasets = $"<xfa:datasets xmlns:xfa=\"{DataNs}\"><xfa:data>{record ?? data}</xfa:data>{description}</xfa:datasets>";
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R /AcroForm 5 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R >>",
            "<< /Length 0 >>\nstream\n\nendstream",
            "<< /Fields [] /XFA [(template) 6 0 R (datasets) 7 0 R] >>",
            $"<< /Length {Template.Length} >>\nstream\n{Template}\nendstream",
            $"<< /Length {Encoding.UTF8.GetByteCount(datasets)} >>\nstream\n{datasets}\nendstream",
        };
        using var ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.UTF8.GetBytes(s));
        Write("%PDF-1.7\n");
        var offsets = new long[objects.Length];
        for (var i = 0; i < objects.Length; i++)
        {
            offsets[i] = ms.Position;
            Write($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        var xref = ms.Position;
        Write($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets) Write($"{o:D10} 00000 n \n");
        Write($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return ms.ToArray();
    }

    private static Document Saved(Document doc)
    {
        var saved = new MemoryStream();
        doc.Save(saved);
        saved.Position = 0;
        return new Document(saved);
    }

    /// <summary>The datasets packet of a saved form, as written.</summary>
    private static string DatasetsPacket(Document doc)
    {
        using var ms = new MemoryStream();
        new Aspose.Pdf.Facades.Form(doc).ExtractXfaData(ms);
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static XmlElement DataRecord(Document doc)
    {
        var xml = new XmlDocument();
        xml.LoadXml(DatasetsPacket(doc));
        var ns = new XmlNamespaceManager(xml.NameTable);
        ns.AddNamespace("xfa", DataNs);
        return (XmlElement)xml.SelectSingleNode("//xfa:data/*", ns)!;
    }

    [Fact]
    public void ARowAddressedThroughAPresentationOnlySubformIsWrittenAndReadAtItsOwnIndex()
    {
        using var doc = new Document(new MemoryStream(BuildForm()));
        Assert.Equal("", doc.Form.XFA!["form1[0].page1[0].rows[0].row[1].date[0]"]);
        doc.Form.XFA["form1[0].page1[0].rows[0].row[1].date[0]"] = "second";
        doc.Form.XFA["form1[0].page1[0].rows[0].row[3].date[0]"] = "fourth";
        using var back = Saved(doc);

        var dates = DataRecord(back).SelectNodes("rows/row/date")!.Cast<XmlNode>().Select(n => n.InnerText).ToArray();
        Assert.Equal(new[] { "first", "second", "", "fourth" }, dates);
        Assert.Equal("second", back.Form.XFA!["form1[0].page1[0].rows[0].row[1].date[0]"]);
    }

    [Fact]
    public void AFieldBoundByReferenceToANodeTheDataLacksGetsItsOwnNode_NotTheSameNamedNodeOfAnotherGroup()
    {
        using var doc = new Document(new MemoryStream(BuildForm()));
        var form = new Aspose.Pdf.Facades.Form(doc);
        Assert.True(form.FillField("form1[0].page1[0].applicant[0].firstName[0]", "John"));
        using var back = Saved(doc);

        var record = DataRecord(back);
        Assert.Equal("John", record.SelectSingleNode("people/applicant/firstName")?.InnerText);
        Assert.Equal("Rep", record.SelectSingleNode("people/representative/firstName")?.InnerText);
        Assert.Equal("John", new Aspose.Pdf.Facades.Form(back).GetField("form1[0].page1[0].applicant[0].firstName[0]"));
    }

    [Fact]
    public void AFieldInsideAnAreaIsFilledInTheDataGroupOfItsSubform()
    {
        using var doc = new Document(new MemoryStream(BuildForm()));
        var form = new Aspose.Pdf.Facades.Form(doc);
        Assert.True(form.FillField("form1[0].page1[0].applicant[0].phone[0]", "555"));
        using var back = Saved(doc);

        Assert.Equal("555", DataRecord(back).SelectSingleNode("people/applicant/phone")?.InnerText);
    }

    [Fact]
    public void AnUnnamedFieldBoundByReferenceIsWrittenWhereItsReferenceNames()
    {
        using var doc = new Document(new MemoryStream(BuildForm()));
        doc.Form.XFA!["form1[0].page1[0].applicant[0].#field[0]"] = "Dr";
        using var back = Saved(doc);

        Assert.Equal("Dr", DataRecord(back).SelectSingleNode("people/applicant/title")?.InnerText);
    }

    [Fact]
    public void APathTheTemplateDoesNotHoldIsStillWrittenIntoAFormWithNoDataYet()
    {
        using var doc = new Document(new MemoryStream(BuildForm(record: "")));
        doc.Form.XFA!["form1[0].extra[0]"] = "kept";
        using var back = Saved(doc);

        Assert.Equal("kept", DataRecord(back).SelectSingleNode("extra")?.InnerText);
        Assert.Equal("kept", back.Form.XFA!["form1[0].extra[0]"]);
    }

    [Fact]
    public void AFieldThatBindsNoDataKeepsItsValueUnderTheDataGroupItsContainersReach()
    {
        using var doc = new Document(new MemoryStream(BuildForm()));
        doc.Form.XFA!["form1[0].page1[0].addButton[0]"] = "pressed";
        using var back = Saved(doc);

        Assert.Equal("pressed", DataRecord(back).SelectSingleNode("addButton")?.InnerText);
        Assert.DoesNotContain("page1", DatasetsPacket(back));
        Assert.Equal("pressed", back.Form.XFA!["form1[0].page1[0].addButton[0]"]);
    }

    [Fact]
    public void EveryInstanceOfARepeatedSubformCanBeFilled()
    {
        using var doc = new Document(new MemoryStream(BuildForm()));
        var form = new Aspose.Pdf.Facades.Form(doc);
        for (var i = 0; i < 3; i++)
            Assert.True(form.FillField($"form1[0].page1[0].member[{i}].lastName[0]", $"member {i}"));
        using var back = Saved(doc);

        var names = DataRecord(back).SelectNodes("member/lastName")!.Cast<XmlNode>().Select(n => n.InnerText).ToArray();
        Assert.Equal(new[] { "member 0", "member 1", "member 2" }, names);
    }

    [Fact]
    public void ADatasetsPacketIsWrittenWithoutAnXmlDeclaration()
    {
        using var doc = new Document(new MemoryStream(BuildForm()));
        doc.Form.XFA!["form1[0].page1[0].rows[0].row[0].date[0]"] = "changed";
        using var back = Saved(doc);

        Assert.StartsWith("<xfa:datasets", DatasetsPacket(back));
    }

    [Fact]
    public void DataImportedIntoANamespacedFormKeepsItsNamespaces()
    {
        const string ns = "urn:example:filing";
        using var doc = new Document(new MemoryStream(BuildForm(record: $"<f:form1 xmlns:f=\"{ns}\"><f:rows/></f:form1>")));
        var import = $"<xfa:data xmlns:xfa=\"{DataNs}\"><f:form1 xmlns:f=\"{ns}\"><f:rows><f:row><f:date>x</f:date></f:row></f:rows></f:form1></xfa:data>";
        new Aspose.Pdf.Facades.Form(doc).ImportXml(new MemoryStream(Encoding.UTF8.GetBytes(import)));
        using var back = Saved(doc);

        var record = DataRecord(back);
        Assert.Equal(ns, record.NamespaceURI);
        Assert.Equal(ns, record.FirstChild!.FirstChild!.FirstChild!.NamespaceURI);
    }

    [Fact]
    public void DataImportedIntoAFormHoldingNoDataYetLosesTheNamespacesOfItsFile_WhateverItsDataDescriptionSays()
    {
        const string ns = "urn:example:filing";
        var description = $"<dd:dataDescription xmlns:dd=\"http://ns.adobe.com/data-description/\" dd:name=\"form1\"><f:form1 xmlns:f=\"{ns}\"/></dd:dataDescription>";
        using var doc = new Document(new MemoryStream(BuildForm(record: "", description: description)));
        var import = $"<f:form1 xmlns:f=\"{ns}\"><f:rows><f:row><f:date>x</f:date></f:row></f:rows></f:form1>";
        new Aspose.Pdf.Facades.Form(doc).ImportXml(new MemoryStream(Encoding.UTF8.GetBytes(import)));
        using var back = Saved(doc);

        Assert.Equal("", DataRecord(back).NamespaceURI);
    }

    [Fact]
    public void DataImportedIntoANamespaceLessFormLosesTheNamespacesOfItsFile()
    {
        using var doc = new Document(new MemoryStream(BuildForm()));
        var import = "<e:form1 xmlns:e=\"urn:example:efile\"><e:rows><e:row><e:date>x</e:date></e:row></e:rows></e:form1>";
        new Aspose.Pdf.Facades.Form(doc).ImportXml(new MemoryStream(Encoding.UTF8.GetBytes(import)));
        using var back = Saved(doc);

        var record = DataRecord(back);
        Assert.Equal("", record.NamespaceURI);
        Assert.Equal("x", back.Form.XFA!["form1[0].page1[0].rows[0].row[0].date[0]"]);
    }
}
