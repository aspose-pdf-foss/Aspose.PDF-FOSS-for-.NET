using System.Globalization;
using System.Xml;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

internal static partial class XmlBinding
{
    /// <summary>The stages of the page fragment build: the fragment-level state, the style and margin children, the segments, and the foot note.</summary>
    private static void ReadFragmentFootNote(PageFragmentBuildState pf)
    {
        foreach (XmlNode child in pf.fragNode.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element || child.LocalName != "FootNote") continue;
            var note = new Note();
            foreach (XmlNode fnChild in child.ChildNodes)
            {
                if (fnChild.NodeType != XmlNodeType.Element) continue;
                switch (fnChild.LocalName)
                {
                    case "Text":
                        note.Text = GetAttr(fnChild, "Text");
                        break;
                    case "TextFragment":
                        // Empty fragments stay (as XmlEmptyShell): the note's last
                        // one closes the note with an empty line in the band.
                        if (BuildPageFragment(pf.document, fnChild, pf.defaults, includeEmpty: true) is { } noteFrag)
                            note.Paragraphs.Add(noteFrag);
                        break;
                }
            }
            if (note.Text is not null || note.Paragraphs.Count > 0)
                pf.tf.FootNote = note;
            break;
        }
    }

    /// <summary>The stages of the page fragment build: the fragment-level state, the style and margin children, the segments, and the foot note.</summary>
    private static void ReadFragmentSegments(PageFragmentBuildState pf)
    {
        foreach (XmlNode child in pf.fragNode.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element) continue;
            if (child.LocalName == "TextSegment")
            {
                pf.authoredSegments++;
                pf.any |= AddXmlSegmentFromNode(pf.tf, child, pf.fragState);
            }
            else if (child.LocalName == "TextState")
            {
                foreach (XmlNode wrapped in child.ChildNodes)
                {
                    if (wrapped.NodeType != XmlNodeType.Element || wrapped.LocalName != "TextSegment") continue;
                    pf.authoredSegments++;
                    var wrapStyle = pf.fragState.Clone();
                    ReadXmlTextStyle(child, wrapStyle, segmentNested: false);
                    pf.any |= AddXmlSegmentFromNode(pf.tf, wrapped, wrapStyle);
                }
            }
        }
    }

    /// <summary>The stages of the page fragment build: the fragment-level state, the style and margin children, the segments, and the foot note.</summary>
    private static void ReadFragmentStyleAndMargin(PageFragmentBuildState pf)
    {
        foreach (XmlNode child in pf.fragNode.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element) continue;
            switch (child.LocalName)
            {
                case "TextState":
                    // A TextState WRAPPING segments styles only
                    // those segments — it must not bleed into the fragment level.
                    if (!HasElementChild(child, "TextSegment"))
                        ReadXmlTextStyle(child, pf.fragState, segmentNested: false);
                    pf.tabStops ??= ParseTabStops(child);
                    break;
                case "Margin":
                    pf.margin = ParseMargin(child);
                    break;
            }
        }
    }

    /// <summary>The stages of the page fragment build: the fragment-level state, the style and margin children, the segments, and the foot note.</summary>
    private static void ResolveFragmentState(PageFragmentBuildState pf)
    {
        foreach (XmlNode child in pf.fragNode.ChildNodes)
            if (child.NodeType == XmlNodeType.Element && child.LocalName == "TextState"
                && !HasElementChild(child, "TextSegment"))
            { pf.hasFragState = true; break; }

        pf.fragState = pf.hasFragState
            ? new XmlTextStyle { FontSize = 10, FontSizeSet = true }
            : new XmlTextStyle
            {
                FontSize = pf.defaults.FontSize > 0 ? pf.defaults.FontSize : 10,
                FontSizeSet = true, // a page fragment always resolves a concrete size
                FontName = pf.defaults.FontName,
                Foreground = pf.defaults.Foreground,
            };
    }
}
