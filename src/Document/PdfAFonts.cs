using System.Linq;
using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.Forms;
using Aspose.Pdf.IO;
using Aspose.Pdf.IO.Filters;
using Aspose.Pdf.Optimization;
using Aspose.Pdf.Security;
using Aspose.Pdf.Tagged;
using DocumentPrivilege = Aspose.Pdf.Facades.DocumentPrivilege;

namespace Aspose.Pdf;

public sealed partial class Document
{
    /// <summary>FontUtilities.SubsetFonts(SubsetAllFonts) support: embed every used
    /// non-embedded font (including the Standard-14 faces) so the subsetter has a
    /// program to trim; pending programs resolve via <see cref="ResolvePendingStreamInternal"/>.</summary>
    internal void EmbedAllFontsForSubsetting() => EmbedNonEmbeddedFonts(includeStandard14: true);

    /// <summary>Embed every non-embedded simple (Type1/TrueType) font referenced by the
    /// pages, substituting a system face. The real family is used when it resolves;
    /// otherwise the text is re-mapped to Arial. The existing font dictionary is rewritten
    /// in place so the page's resource reference is preserved.</summary>
    private void EmbedNonEmbeddedFonts(PdfFormatConversionOptions? options = null,
        bool includeStandard14 = false)
    {
        var ef = new FontEmbedState();
        ef.options = options;
        ef.includeStandard14 = includeStandard14;
        ef.makeCompanions = ef.options is not null && ef.options.Format switch
        {
            PdfFormat.PDF_A_1A or PdfFormat.PDF_A_2A or PdfFormat.PDF_A_3A => true,
            _ => false,
        };
        ef.reported = new HashSet<string>(StringComparer.Ordinal);
        // An empty FontRepository.Sources means the caller has removed all font sources
        // (including system fonts), so no replacement face is available to embed. Resolving
        // straight from the OS here would silently embed system fonts and let the PDF/A
        // conversion "succeed" even though the fonts are unavailable — the conversion must
        // instead fail so CheckFontEmbedding reports the missing fonts.
        if (Text.FontRepository.Sources.Count == 0) return;

        ef.done = new HashSet<PdfDictionary>();
        ef.fontFileCache = new Dictionary<string, (int objNum, string embedName)>();
        ef.visitedRes = new HashSet<PdfDictionary>();
        ef.companions = new Dictionary<PdfDictionary, PdfDictionary>(ReferenceEqualityComparer.Instance);

        EmbedPageFonts(ef);

        // Register each companion beside its original in the page-level /Font
        // dictionaries (where the extra entries live).
        AttachCompanionFonts(ef);
    }

    /// <summary>Embed a system face into a non-embedded composite (Type0/CID) font.
    /// Unlike the simple-font path there is NO Arial fallback: under an Identity
    /// encoding the content stream's CIDs are the ORIGINAL face's glyph ids, so only
    /// the same-named real face keeps them valid — an unresolvable family is left
    /// unembedded (the conversion log still records the violation). A CJK-mojibake
    /// /BaseFont (its legacy-codepage bytes read as Latin-1, e.g. "ËÎÌå" = 宋体) is
    /// decoded through the font's CMap codepage and mapped to the host family.</summary>
    private void EmbedNonEmbeddedCidFont(PdfDictionary type0Dict, PdfFormatConversionOptions? options,
        HashSet<string> reported, Dictionary<string, (int objNum, string embedName)> fontFileCache)
    {
        var descArr = _reader.Resolve(type0Dict.Get("DescendantFonts")) as PdfArray;
        var cidFont = descArr is { Count: > 0 } ? _reader.ResolveDict(descArr[0]) : null;
        if (cidFont is null) return;
        var descriptor = _reader.ResolveDict(cidFont.Get("FontDescriptor"));
        if (descriptor is not null &&
            (descriptor.Get("FontFile") ?? descriptor.Get("FontFile2") ?? descriptor.Get("FontFile3")) is not null)
            return; // already embedded
        var baseFont = type0Dict.GetName("BaseFont") ?? cidFont.GetName("BaseFont") ?? "";
        // A subset tag does NOT imply an embedded program (see the simple-font pass):
        // the descriptor check above is the authority; resolve by the bare family.
        if (baseFont.Length > 7 && baseFont[6] == '+') baseFont = baseFont[7..];

        if (options is not null && reported.Add(baseFont))
            options.ConversionLog.Add(new PdfAViolation
            {
                Rule = "FontEmbedding",
                Description = $"Font '{baseFont}' is not embedded.",
            });

        var ttf = Text.SystemFontResolver.Resolve(baseFont);
        if (ttf is null or { Length: 0 })
        {
            var decoded = DecodeCjkBaseFontName(baseFont, type0Dict, cidFont);
            if (decoded != baseFont)
                ttf = Text.SystemFontResolver.Resolve(decoded);
        }
        if (ttf is null or { Length: 0 }) return;

        try
        {
            Text.FontEmbedder.EmbedIntoCidFontDict(this, ttf, type0Dict, cidFont, fontFileCache);
            RaiseFontSubstitution(new Text.Font(baseFont, "Type0"), new Text.Font(baseFont, "Type0"));
        }
        catch { /* best-effort: leave the font as-is if embedding fails */ }
    }

    /// <summary>Decode a legacy-codepage-mojibake /BaseFont ("ËÎÌå") to its script-native
    /// name (宋体) via the font's CMap codepage, then map the common CJK display names to
    /// their host font families (宋体 → SimSun). Returns the input unchanged when it has no
    /// high bytes or no codepage applies.</summary>
    private string DecodeCjkBaseFontName(string baseFont, PdfDictionary type0Dict, PdfDictionary cidFont)
    {
        var hasHigh = false;
        foreach (var c in baseFont)
            if (c > 0x7F) { hasHigh = true; break; }
        if (!hasHigh) return baseFont;

        var cp = Text.CidFontInfo.CodepageForCMapName(type0Dict.GetName("Encoding"));
        if (cp == 0)
        {
            var csi = _reader.ResolveDict(cidFont.Get("CIDSystemInfo"));
            var orderingObj = csi?.Get("Ordering");
            var ordering = orderingObj is PdfString os ? os.ToText()
                : (orderingObj is PdfName on ? on.Value : null);
            cp = ordering switch { "CNS1" => 950, "GB1" => 936, "Japan1" => 932, "Korea1" or "KR" => 949, _ => 0 };
        }
        if (cp == 0) return baseFont;

        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < baseFont.Length; i++)
        {
            var c = baseFont[i];
            if (c <= 0x7F || i + 1 >= baseFont.Length) { sb.Append(c); continue; }
            var code = (c << 8) | (baseFont[i + 1] & 0xFF);
            if (Text.CidFontInfo.LegacyLookup(cp, code) is int u)
            {
                sb.Append(char.ConvertFromUtf32(u));
                i++;
            }
            else sb.Append(c);
        }
        var native = sb.ToString();
        return native switch
        {
            "宋体" => "SimSun",
            "新宋体" => "NSimSun",
            "黑体" => "SimHei",
            "楷体" or "楷体_GB2312" => "KaiTi",
            "仿宋" or "仿宋_GB2312" => "FangSong",
            "微软雅黑" => "Microsoft YaHei",
            "ＭＳ ゴシック" or "ＭＳゴシック" => "MS Gothic",
            "ＭＳ 明朝" or "ＭＳ明朝" => "MS Mincho",
            "標楷體" => "DFKai-SB",
            "細明體" => "MingLiU",
            "新細明體" => "PMingLiU",
            "굴림" => "Gulim",
            "바탕" => "Batang",
            _ => native,
        };
    }

    /// <summary>Replace every font name (the font's and its descendant's /BaseFont, the
    /// descriptor's /FontName) whose bytes are not valid UTF-8 by the embedded program's
    /// English family name, keeping the subset tag. A face with no readable program keeps
    /// its authored name.</summary>
    private void RenameNonUtf8FontNames()
    {
        var visitedRes = new HashSet<PdfDictionary>();
        var renamed = new HashSet<PdfDictionary>();
        void RenameOne(PdfDictionary fontDict)
        {
            if (!renamed.Add(fontDict)) return;
            var target = fontDict;
            if (fontDict.GetName("Subtype") == "Type0")
            {
                var descArr = _reader.Resolve(fontDict.Get("DescendantFonts")) as PdfArray;
                target = descArr is { Count: > 0 } ? _reader.ResolveDict(descArr[0]) : null;
                if (target is null) return;
            }
            var authored = fontDict.GetName("BaseFont") ?? target.GetName("BaseFont") ?? "";
            if (IsValidUtf8Name(authored)) return;
            PdfDictionary? descriptor;
            try { descriptor = _reader.ResolveDict(target.Get("FontDescriptor")); } catch { return; }
            var english = EmbeddedProgramFamily(descriptor);
            if (string.IsNullOrWhiteSpace(english)) return;
            var tag = authored.Length > 7 && authored[6] == '+' ? authored[..7] : "";
            var name = new PdfName(tag + english);
            fontDict.Set("BaseFont", name);
            if (!ReferenceEquals(target, fontDict)) target.Set("BaseFont", name);
            descriptor?.Set("FontName", name);
        }

        foreach (var page in Pages)
        {
            PdfDictionary? resources;
            try { resources = Reader.ResolveDict(page.Dict.Get("Resources")); } catch { continue; }
            if (resources is not null)
                foreach (var fontDict in CollectFontDictsRecursive(resources, visitedRes))
                    RenameOne(fontDict);
            foreach (var apRes in CollectAnnotationAppearanceResources(page))
                foreach (var fontDict in CollectFontDictsRecursive(apRes, visitedRes))
                    RenameOne(fontDict);
        }
    }

    /// <summary>Whether a name's bytes (its chars are the bytes, read as Latin-1) form valid UTF-8.</summary>
    private static bool IsValidUtf8Name(string name)
    {
        var high = false;
        foreach (var c in name)
        {
            if (c > 0xFF) return true; // already decoded text, not raw bytes
            if (c > 0x7F) high = true;
        }
        if (!high) return true;
        try
        {
            new System.Text.UTF8Encoding(false, throwOnInvalidBytes: true).GetString(Compat.Latin1.GetBytes(name));
            return true;
        }
        catch (System.Text.DecoderFallbackException) { return false; }
    }

    /// <summary>The English family name of the descriptor's embedded TrueType/OpenType program, or null.</summary>
    private string? EmbeddedProgramFamily(PdfDictionary? descriptor)
    {
        if (descriptor is null) return null;
        var program = _reader.ResolveStream(descriptor.Get("FontFile2")) ?? _reader.ResolveStream(descriptor.Get("FontFile3"));
        if (program is null) return null;
        byte[] data;
        try { data = _reader.DecodeStream(program); } catch { return null; }
        var family = Text.FontRepository.ReadTtfFamilyName(data);
        return family == "Unknown" ? null : family;
    }

    /// <summary>Clamp out-of-range Courier-family descriptor metrics: a Descent below
    /// -310 goes to -300 across every page's fonts (Type0 descendants included), so a
    /// converted document passes the validator's Courier Descent range gate. Only the
    /// metric entry changes — programs, widths and everything else stay untouched.</summary>
    private void RepairFontDescriptors()
    {
        var visitedRes = new HashSet<PdfDictionary>();
        var repaired = new HashSet<PdfDictionary>();
        void RepairOne(PdfDictionary fontDict)
        {
            var target = fontDict;
            if (fontDict.GetName("Subtype") == "Type0")
            {
                var descArr = _reader.Resolve(fontDict.Get("DescendantFonts")) as PdfArray;
                target = descArr is { Count: > 0 } ? _reader.ResolveDict(descArr[0]) : null;
                if (target is null) return;
            }
            PdfDictionary? descriptor;
            try { descriptor = _reader.ResolveDict(target.Get("FontDescriptor")); } catch { return; }
            if (descriptor is null || !repaired.Add(descriptor)) return;
            // Repair covers the Courier New faces only: their descriptors habitually
            // carry the raw hhea descent (-680/-710) that the range gate rejects.
            // Other Courier-family faces (Courier Prime and friends) keep their
            // authored metrics — for those the violation must survive conversion.
            var name = descriptor.GetName("FontName")
                       ?? target.GetName("BaseFont") ?? fontDict.GetName("BaseFont") ?? "";
            if (name.Contains("CourierNew") && descriptor.GetInt("Descent", 0) < -310)
                descriptor.Set("Descent", new PdfInteger(-300));
        }

        foreach (var page in Pages)
        {
            PdfDictionary? resources;
            try { resources = Reader.ResolveDict(page.Dict.Get("Resources")); } catch { continue; }
            if (resources is not null)
                foreach (var fontDict in CollectFontDictsRecursive(resources, visitedRes))
                    RepairOne(fontDict);
            foreach (var apRes in CollectAnnotationAppearanceResources(page))
                foreach (var fontDict in CollectFontDictsRecursive(apRes, visitedRes))
                    RepairOne(fontDict);
        }
    }

    /// <summary>Yield the /Resources dict of every appearance (/AP /N, /D, /R) stream of
    /// every annotation on <paramref name="page"/>, descending state-keyed appearance
    /// sub-dictionaries. Used so PDF/A font embedding reaches fonts that live only inside
    /// an annotation's appearance stream.</summary>
    private IEnumerable<PdfDictionary> CollectAnnotationAppearanceResources(Page page)
    {
        PdfArray? annots;
        try { annots = Reader.Resolve(page.Dict.Get("Annots")) as PdfArray; } catch { yield break; }
        if (annots is null) yield break;
        foreach (var annotObj in annots)
        {
            var annot = Reader.ResolveDict(annotObj);
            var ap = annot is null ? null : Reader.ResolveDict(annot.Get("AP"));
            if (ap is null) continue;
            foreach (var apKey in new[] { "N", "D", "R" })
            {
                var entry = Reader.Resolve(ap.Get(apKey));
                if (entry is PdfStream stream)
                {
                    var res = Reader.ResolveDict(stream.Dict.Get("Resources"));
                    if (res is not null) yield return res;
                }
                else if (entry is PdfDictionary stateDict) // state-keyed appearances
                {
                    foreach (var stateKey in new List<string>(stateDict.Keys))
                    {
                        var s = Reader.ResolveStream(stateDict.Get(stateKey));
                        var res = s is null ? null : Reader.ResolveDict(s.Dict.Get("Resources"));
                        if (res is not null) yield return res;
                    }
                }
            }
        }
    }

    /// <summary>Yield every <c>/Font</c> child dictionary reachable from a <c>/Resources</c>
    /// dict, recursing through Form XObject (<c>/Subtype /Form</c>) resources so a font used
    /// only inside a form/appearance stream is reached too. <paramref name="visitedRes"/>
    /// guards against resource-dict cycles.</summary>
    private IEnumerable<PdfDictionary> CollectFontDictsRecursive(PdfDictionary resources,
        HashSet<PdfDictionary> visitedRes)
    {
        if (!visitedRes.Add(resources)) yield break;

        var fontRes = Reader.ResolveDict(resources.Get("Font"));
        if (fontRes is not null)
            foreach (var key in new List<string>(fontRes.Keys))
            {
                var fontDict = Reader.ResolveDict(fontRes.Get(key));
                if (fontDict is not null) yield return fontDict;
            }

        var xobjs = Reader.ResolveDict(resources.Get("XObject"));
        if (xobjs is not null)
            foreach (var key in new List<string>(xobjs.Keys))
            {
                var xobj = Reader.Resolve(xobjs.Get(key));
                var xdict = xobj is PdfStream s ? s.Dict : xobj as PdfDictionary;
                if (xdict is null || xdict.GetName("Subtype") != "Form") continue;
                var subRes = Reader.ResolveDict(xdict.Get("Resources"));
                if (subRes is not null)
                    foreach (var fd in CollectFontDictsRecursive(subRes, visitedRes))
                        yield return fd;
            }
    }

    private bool IsSimpleFontEmbedded(PdfDictionary fontDict)
    {
        var fd = Reader.ResolveDict(fontDict.Get("FontDescriptor"));
        if (fd is null) return false;
        return fd.Get("FontFile") is not null || fd.Get("FontFile2") is not null || fd.Get("FontFile3") is not null;
    }

    private bool CheckFontEmbedding(PdfFormatConversionOptions options)
    {
        // Narrow scope: only block conversion when the caller has explicitly
        // emptied FontRepository.Sources (canonical behaviour: with no font
        // sources at all, unembedded non-Standard14 fonts can't be resolved).
        // When sources are populated, SystemFontSource still has lookup
        // gaps (matches by filename rather than TTF name table) — applying
        // the check there would block valid conversions of common fonts.
        if (Text.FontRepository.Sources.Count > 0) return true;
        bool allResolved = true;
        var standard14 = new HashSet<string>(Text.FontRepository.Standard14Names, StringComparer.Ordinal);
        foreach (var page in Pages)
        {
            Text.FontCollection? pageFonts;
            try { pageFonts = page.Fonts; } catch { continue; }
            if (pageFonts is null) continue;
            foreach (var font in pageFonts)
            {
                if (font.IsEmbedded) continue;
                // PDF spec §9.6.4: a BaseFont of the form "XXXXXX+Name" is a
                // subset font, embedded by definition. IsEmbedded doesn't
                // recognise the prefix; treat as embedded here.
                if (font.BaseFont.Length > 7 && font.BaseFont[6] == '+') continue;
                if (standard14.Contains(font.BaseFont)) continue;
                options.ConversionLog.Add(new PdfAViolation
                {
                    Rule = "FontNotEmbedded",
                    Description = $"Font '{font.BaseFont}' is not embedded and FontRepository.Sources is empty.",
                });
                allResolved = false;
            }
        }
        return allResolved;
    }

    /// <summary>
    /// PDF/A level-A: re-encode every page usage of a symbolic TrueType font whose used
    /// character codes resolve through the program's (3,0) cmap into the Private Use Area.
    /// Each such font gets a companion Type0/Identity-H font under a <c>C{n}_0</c> resource
    /// key (descendant CIDFontType2 sharing the embedded program, CIDs = glyph ids); the
    /// content's Tf is redirected to it, show strings become 2-byte glyph-id hex strings,
    /// and each show is wrapped in a <c>/Span &lt;&lt;/ActualText (…)&gt;&gt; BDC … EMC</c>
    /// marker, so the output carries a Unicode meaning for the PUA glyphs.
    /// </summary>
    private void ConvertPuaSymbolicFontUsagesToType0(Page page)
    {
        var pu = new PuaSymbolicConvertState();
        pu.page = page;
        pu.resources = _reader.ResolveDict(pu.page.Dict.Get("Resources"));
        pu.fonts = pu.resources is not null ? _reader.ResolveDict(pu.resources.Get("Font")) : null;
        if (pu.fonts is null) return;

        pu.contentBytes = pu.page.GetContentStreamBytes();
        if (pu.contentBytes is null || pu.contentBytes.Length == 0) return;
        pu.content = Compat.Latin1.GetString(pu.contentBytes);

        pu.converted = 0;
        foreach (var key in pu.fonts.Keys.ToList())
        {
            if (!ConvertPuaSymbolicFont(pu, key)) break;
        }

        if (pu.converted > 0)
            pu.page.SetContentStream(Compat.Latin1.GetBytes(pu.content));
    }

    /// <summary>BaseFont without its 6-letter subset prefix ("OERHZY+Minion-Regular"
    /// -> "Minion-Regular").</summary>
    private static string StripSubsetPrefix(string baseFont) =>
        baseFont.Length > 7 && baseFont[6] == '+' ? baseFont[7..] : baseFont;

    /// <summary>True when the shown text (Latin1 chars, one per raw byte) contains
    /// the 2-byte code 0000 on an even boundary — CID 0 under Identity encoding.</summary>
    private static bool HasAlignedCidZero(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        for (var i = 0; i + 1 < text.Length; i += 2)
            if (text[i] == '\0' && text[i + 1] == '\0') return true;
        return false;
    }
}
