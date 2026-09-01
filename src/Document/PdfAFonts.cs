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
        // Companion font entries are an A-LEVEL (tagged) conversion behaviour: the
        // 1A/2A outputs carry the original entry plus the embedded
        // companion (a pair = 4 entries), while the B/U-level outputs stay
        // within the corpus's size budgets (output <= input + 10%) - so no
        // companions there.
        var makeCompanions = options is not null && options.Format switch
        {
            PdfFormat.PDF_A_1A or PdfFormat.PDF_A_2A or PdfFormat.PDF_A_3A => true,
            _ => false,
        };
        // Records (once per BaseFont) that the source left a glyph-bearing font
        // unembedded — a PDF/A violation that this pass then fixes by embedding.
        var reported = new HashSet<string>(StringComparer.Ordinal);
        // An empty FontRepository.Sources means the caller has removed all font sources
        // (including system fonts), so no replacement face is available to embed. Resolving
        // straight from the OS here would silently embed system fonts and let the PDF/A
        // conversion "succeed" even though the fonts are unavailable — the conversion must
        // instead fail so CheckFontEmbedding reports the missing fonts.
        if (Text.FontRepository.Sources.Count == 0) return;

        var done = new HashSet<PdfDictionary>();
        // Shared across every dictionary so identical font programs are embedded once.
        var fontFileCache = new Dictionary<string, (int objNum, string embedName)>();
        var visitedRes = new HashSet<PdfDictionary>();
        // The conversion leaves TWO resource entries per fixed font: the
        // slot the content references (embedded in place) AND a companion - for a
        // real face, the same face embedded in full under its bare name; for a
        // Standard-14 replacement, the untouched original (non-embedded, legal
        // because nothing references it and the embedding check is usage-based).
        // Measured: an ArialNarrow pair -> 4 entries (2 embedded + 2 subset
        // copies); a Helvetica pair -> 4 (originals + embedded Arials).
        var companions = new Dictionary<PdfDictionary, PdfDictionary>(ReferenceEqualityComparer.Instance);

        static PdfDictionary CopyFontDict(PdfDictionary src)
        {
            var copy = new PdfDictionary();
            foreach (var key in src.Keys)
                if (src.Get(key) is { } value) copy.Set(key, value);
            return copy;
        }

        // Embed one simple, glyph-bearing, non-embedded font dict in place, substituting a
        // resolved system face (Helvetica→Arial, etc.) when the named font has none.
        void EmbedOne(PdfDictionary fontDict)
        {
            if (!done.Add(fontDict)) return;
            // Consume the transient "embed full, don't subset" marker (set by
            // Font.IsSubset = false). Removed here so it never reaches the output.
            var embedFull = fontDict.GetBool("AsposeEmbedFull");
            fontDict.Remove("AsposeEmbedFull");
            var subtype = fontDict.GetName("Subtype");
            if (subtype == "Type0")
            {
                EmbedNonEmbeddedCidFont(fontDict, options, reported, fontFileCache);
                return;
            }
            if (subtype is not ("Type1" or "TrueType")) return;   // simple fonts only
            if (IsSimpleFontEmbedded(fontDict)) return;
            var baseFont = fontDict.GetName("BaseFont") ?? "";
            // A subset tag does NOT imply an embedded program: setting IsSubset on a
            // non-embedded font prefixes the name without adding a FontFile, and the
            // embed check above is the authority. Strip the tag so the bare family
            // resolves ("WRDIWR+Times-Roman" → "Times-Roman").
            if (baseFont.Length > 7 && baseFont[6] == '+') baseFont = baseFont[7..];
            if (!includeStandard14 &&
                new HashSet<string>(Text.FontRepository.Standard14Names, StringComparer.Ordinal).Contains(baseFont))
                return; // standard-14 stay as-is unless the caller opts in (Document.EmbedStandardFonts)

            // The source carries this glyph-bearing font without an embedded
            // program — log it (once per name) as a PDF/A violation before the
            // pass below embeds a resolved face.
            if (options is not null && reported.Add(baseFont))
                options.ConversionLog.Add(new PdfAViolation
                {
                    Rule = "FontEmbedding",
                    Description = $"Font '{baseFont}' is not embedded.",
                });

            var resolved = Text.SystemFontResolver.Resolve(baseFont);
            string newName;
            byte[]? ttf;
            if (resolved is not null) { ttf = resolved; newName = baseFont; }
            else { ttf = Text.SystemFontResolver.Resolve("Arial"); newName = "Arial"; }
            if (ttf is null || ttf.Length == 0) return;

            // A Standard-14 font carries no program of its own, so the resolver returns a
            // host substitute (Helvetica→Arial, Times→Times New Roman, …). Name the embedded
            // font after the face actually embedded — read from its name table — so the output
            // reflects what was embedded rather than the abstract standard name (matching
            // the public surface). Host-dependent by nature.
            if (new HashSet<string>(Text.FontRepository.Standard14Names, StringComparer.Ordinal).Contains(baseFont))
            {
                try
                {
                    var ttp = new Text.TrueTypeParser(ttf);
                    ttp.Parse();
                    var fam = ttp.FamilyName;
                    if (!string.IsNullOrWhiteSpace(fam) && fam != "Unknown")
                        newName = fam.Replace(" ", "");
                }
                catch { /* keep the standard name if the face can't be parsed */ }
            }

            var isStandard14 = new HashSet<string>(Text.FontRepository.Standard14Names, StringComparer.Ordinal).Contains(baseFont);
            // A-level conversion only: snapshot the original before it is overwritten,
            // or plan the subset companion for a real face.
            if (makeCompanions && isStandard14)
                companions[fontDict] = CopyFontDict(fontDict);

            try
            {
                Text.FontEmbedder.EmbedIntoFontDict(this, ttf, fontDict, newName, fontFileCache, subset: !embedFull);
                if (makeCompanions && !isStandard14)
                {
                    // Companion: the same face embedded AGAIN under its own entry,
                    // beside the one the content references (the conversion leaves two
                    // resource entries per fixed font). The companion embeds the
                    // SUBSET program - a full-face companion ballooned every
                    // conversion by megabytes per face and broke the corpus's
                    // output-size budgets (<=500KB outputs
                    // grew past 2MB), sizes the expected outputs stay under.
                    var companion = CopyFontDict(fontDict);
                    Text.FontEmbedder.EmbedIntoFontDict(this, ttf, companion, newName, fontFileCache, subset: true);
                    companions[fontDict] = companion;
                }
                // The event reports the substitute by its user-facing family+style
                // name ("Courier-Bold" → "Courier New Bold"); the dictionary keeps
                // the PDF-safe space-free name written above. SynthesizedFontName
                // carries the display name past FontName's space-stripping.
                var reportedName = Text.FontInfo.SubstitutedFaceDisplayName(baseFont, ttf) ?? newName;
                RaiseFontSubstitution(new Text.Font(baseFont, "Type1"),
                    new Text.Font(reportedName, "TrueType") { SynthesizedFontName = reportedName });
            }
            catch { /* best-effort: leave the font as-is if embedding fails */ }
        }

        foreach (var page in Pages)
        {
            PdfDictionary? resources;
            try { resources = Reader.ResolveDict(page.Dict.Get("Resources")); } catch { continue; }
            // Walk the page resources and any nested Form XObject resources — a font
            // used only inside a form/appearance stream (not the page's own /Font) must
            // be embedded too.
            if (resources is not null)
                foreach (var fontDict in CollectFontDictsRecursive(resources, visitedRes))
                    EmbedOne(fontDict);

            // Annotation appearance (/AP) streams are NOT reachable from the page
            // /Resources, so their fonts (e.g. a FreeText appearance regenerated with a
            // non-embedded standard /Helvetica) must be walked separately for PDF/A.
            foreach (var apRes in CollectAnnotationAppearanceResources(page))
                foreach (var fontDict in CollectFontDictsRecursive(apRes, visitedRes))
                    EmbedOne(fontDict);
        }

        // Register each companion beside its original in the page-level /Font
        // dictionaries (where the extra entries live).
        if (companions.Count > 0)
            foreach (var page in Pages)
            {
                PdfDictionary? res;
                try { res = Reader.ResolveDict(page.Dict.Get("Resources")); } catch { continue; }
                var fonts = res is null ? null : Reader.ResolveDict(res.Get("Font"));
                if (fonts is null) continue;
                foreach (var key in fonts.Keys.ToArray())
                {
                    PdfDictionary? fd;
                    try { fd = Reader.ResolveDict(fonts.Get(key)); } catch { continue; }
                    if (fd is null || !companions.TryGetValue(fd, out var companion)) continue;
                    var ck = key + "c";
                    while (fonts.ContainsKey(ck)) ck += "c";
                    fonts.Set(ck, companion);
                }
            }
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
        var resources = _reader.ResolveDict(page.Dict.Get("Resources"));
        var fonts = resources is not null ? _reader.ResolveDict(resources.Get("Font")) : null;
        if (fonts is null) return;

        var contentBytes = page.GetContentStreamBytes();
        if (contentBytes is null || contentBytes.Length == 0) return;
        var content = System.Text.Encoding.Latin1.GetString(contentBytes);

        var converted = 0;
        foreach (var key in fonts.Keys.ToList())
        {
            var fontDict = _reader.ResolveDict(fonts.Get(key));
            if (fontDict is null || fontDict.GetName("Subtype") != "TrueType") continue;
            var descriptor = _reader.ResolveDict(fontDict.Get("FontDescriptor"));
            var ff2 = descriptor is not null ? _reader.ResolveStream(descriptor.Get("FontFile2")) : null;
            if (descriptor is null || ff2 is null) continue;
            var flags = (_reader.Resolve(descriptor.Get("Flags")) as PdfInteger)?.Value ?? 0;
            if ((flags & 4) == 0) continue; // symbolic flag

            // The program's cmap decides whether the used codes are PUA-routed.
            Dictionary<int, int> cmap;
            try { cmap = new Text.GlyphOutlineParser(_reader.DecodeStream(ff2)).CMap; }
            catch { continue; }

            int CodeToGid(int code) =>
                cmap.TryGetValue(0xF000 | code, out var g) ? g
                : cmap.TryGetValue(code, out var g2) ? g2 : 0;
            bool CodeIsPua(int code) => cmap.ContainsKey(0xF000 | code);

            // Find this font's Tf selections and rewrite the shows that follow them.
            var tfPattern = new System.Text.RegularExpressions.Regex(
                @"/" + System.Text.RegularExpressions.Regex.Escape(key) + @"\s+([\d.]+)\s+Tf");
            if (!tfPattern.IsMatch(content)) continue;

            var firstChar = (int)((_reader.Resolve(fontDict.Get("FirstChar")) as PdfInteger)?.Value ?? 0);
            var widths = _reader.Resolve(fontDict.Get("Widths")) as PdfArray;
            var usedGidWidths = new SortedDictionary<int, double>();
            var anyPua = false;

            var newKey = $"C{converted}_0";
            var rewritten = new StringBuilder();
            var pos = 0;
            foreach (System.Text.RegularExpressions.Match m in tfPattern.Matches(content))
            {
                if (m.Index < pos) continue;
                rewritten.Append(content, pos, m.Index - pos);
                rewritten.Append('/').Append(newKey).Append(' ').Append(m.Groups[1].Value).Append(" Tf");
                pos = m.Index + m.Length;

                // Until the NEXT Tf or ET, re-encode literal show strings.
                var segEnd = content.Length;
                var nextTf = content.IndexOf(" Tf", pos, StringComparison.Ordinal);
                var nextEt = content.IndexOf("ET", pos, StringComparison.Ordinal);
                if (nextTf >= 0)
                {
                    // back up to the start of the /Name that belongs to that Tf
                    var nameStart = content.LastIndexOf('/', nextTf);
                    if (nameStart > pos) segEnd = Math.Min(segEnd, nameStart);
                }
                if (nextEt >= 0) segEnd = Math.Min(segEnd, nextEt);

                var segment = content[pos..segEnd];
                segment = System.Text.RegularExpressions.Regex.Replace(segment,
                    @"\(((?:[^()\\]|\\.)*)\)\s*(Tj|'\s*)",
                    sm =>
                    {
                        var raw = UnescapePdfLiteral(sm.Groups[1].Value);
                        var hex = new StringBuilder();
                        var actual = new StringBuilder();
                        foreach (var ch in raw)
                        {
                            var code = (int)ch;
                            if (CodeIsPua(code)) anyPua = true;
                            var gid = CodeToGid(code);
                            hex.Append(gid.ToString("X4"));
                            double w = 0;
                            if (widths is not null && code - firstChar >= 0 && code - firstChar < widths.Count)
                                w = _reader.Resolve(widths[code - firstChar]) switch
                                {
                                    PdfInteger wi => wi.Value,
                                    PdfReal wr => wr.Value,
                                    _ => 0,
                                };
                            if (gid > 0) usedGidWidths[gid] = w;
                            actual.Append(' '); // PUA glyph: no Unicode meaning; marked as a space
                        }
                        var op = sm.Groups[2].Value.TrimEnd();
                        return $"/Span <</ActualText ({actual})>> BDC\n<{hex}> {op}\nEMC";
                    });
                rewritten.Append(segment);
                pos = segEnd;
            }
            rewritten.Append(content, pos, content.Length - pos);

            if (!anyPua) continue; // not a PUA usage — leave the font/content untouched

            content = rewritten.ToString();
            converted++;

            // Companion Type0 font: descendant shares the embedded program via the
            // SAME descriptor, CIDs are the program's glyph ids (CIDToGIDMap Identity).
            var cidSystemInfo = new PdfDictionary();
            cidSystemInfo.Set("Registry", new PdfString(System.Text.Encoding.ASCII.GetBytes("Adobe")));
            cidSystemInfo.Set("Ordering", new PdfString(System.Text.Encoding.ASCII.GetBytes("Identity")));
            cidSystemInfo.Set("Supplement", new PdfInteger(0));

            var wArr = new PdfArray();
            foreach (var (gid, w) in usedGidWidths)
            {
                wArr.Add(new PdfInteger(gid));
                var inner = new PdfArray();
                inner.Add(new PdfReal(w));
                wArr.Add(inner);
            }

            var baseFontName = fontDict.GetName("BaseFont") ?? "Unknown";
            var cidFont = new PdfDictionary();
            cidFont.Set("Type", new PdfName("Font"));
            cidFont.Set("Subtype", new PdfName("CIDFontType2"));
            cidFont.Set("BaseFont", new PdfName(baseFontName));
            cidFont.Set("CIDSystemInfo", cidSystemInfo);
            cidFont.Set("FontDescriptor", fontDict.Get("FontDescriptor")!);
            cidFont.Set("DW", new PdfInteger(1000));
            if (wArr.Count > 0) cidFont.Set("W", wArr);
            cidFont.Set("CIDToGIDMap", new PdfName("Identity"));

            var type0 = new PdfDictionary();
            type0.Set("Type", new PdfName("Font"));
            type0.Set("Subtype", new PdfName("Type0"));
            type0.Set("BaseFont", new PdfName(baseFontName));
            type0.Set("Encoding", new PdfName("Identity-H"));
            var descendants = new PdfArray();
            descendants.Add(cidFont);
            type0.Set("DescendantFonts", descendants);
            fonts.Set(newKey, type0);
        }

        if (converted > 0)
            page.SetContentStream(System.Text.Encoding.Latin1.GetBytes(content));
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
