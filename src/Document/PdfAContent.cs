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
    /// <summary>Scan the page and form-XObject content for the probed
    /// implementation limits and log an unconvertable problem per offending stream or
    /// string; PDF/A-1 also flags (and truncates) object-level out-of-range fractional
    /// reals in the form dictionaries.</summary>
    /// <summary>Palette slot count the flat-colour image pass always
    /// allocates - the palette is zero-padded to this size, so hival is 255 even for
    /// an image with far fewer distinct colours (measured).</summary>
    private const int FlatDctPaletteSlots = 256;

    /// <summary>Re-encode every ≤256-colour DCTDecode DeviceRGB image in the page's
    /// XObject resources as /Indexed /DeviceRGB with a 256-slot palette. See the
    /// conversion step 12c note for the measured behaviour.</summary>
    private static byte[] FlatDctFlate(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var z = new System.IO.Compression.ZLibStream(ms, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
            z.Write(data, 0, data.Length);
        return ms.ToArray();
    }

    private void PalettizeFlatDctImages(Page page)
    {
        var res = _reader.ResolveDict(page.Dict.Get("Resources"));
        var xobjects = res is null ? null : _reader.ResolveDict(res.Get("XObject"));
        if (xobjects is null) return;
        foreach (var key in xobjects.Keys.ToArray())
        {
            if (_reader.Resolve(xobjects.Get(key)) is not PdfStream img) continue;
            if (img.Dict.GetName("Subtype") != "Image") continue;
            if (img.Dict.GetName("Filter") != "DCTDecode") continue;
            if (img.Dict.GetName("ColorSpace") != "DeviceRGB") continue;
            if (img.Dict.ContainsKey("SMask") || img.Dict.GetBool("ImageMask")) continue;
            var w = (int)img.Dict.GetInt("Width", 0);
            var h = (int)img.Dict.GetInt("Height", 0);
            if (w <= 0 || h <= 0) continue;

            byte[] pixels; int jw, jh, comp;
            try { (pixels, jw, jh, comp) = IO.Filters.JpegDecoder.Decode(img.RawData); }
            catch { continue; }
            if (jw != w || jh != h || (comp != 3 && comp != 1)) continue;
            if (comp == 1)
            {
                // Grayscale-decoded JPEG: expand to RGB so the palette carries the
                // same DeviceRGB triples an RGB source would give.
                var expanded = new byte[w * h * 3];
                for (var i = 0; i < w * h; i++)
                {
                    expanded[i * 3] = pixels[i];
                    expanded[i * 3 + 1] = pixels[i];
                    expanded[i * 3 + 2] = pixels[i];
                }
                pixels = expanded;
            }

            var palette = new Dictionary<int, int>();
            var indices = new byte[w * h];
            var flat = true;
            for (var i = 0; i < w * h; i++)
            {
                var packed = (pixels[i * 3] << 16) | (pixels[i * 3 + 1] << 8) | pixels[i * 3 + 2];
                if (!palette.TryGetValue(packed, out var idx))
                {
                    if (palette.Count == FlatDctPaletteSlots) { flat = false; break; }
                    idx = palette.Count;
                    palette[packed] = idx;
                }
                indices[i] = (byte)idx;
            }
            if (!flat) continue;

            var palBytes = new byte[FlatDctPaletteSlots * 3];
            foreach (var kv in palette)
            {
                palBytes[kv.Value * 3] = (byte)(kv.Key >> 16);
                palBytes[kv.Value * 3 + 1] = (byte)(kv.Key >> 8);
                palBytes[kv.Value * 3 + 2] = (byte)kv.Key;
            }
            var palFlate = FlatDctFlate(palBytes);
            var palDict = new PdfDictionary();
            palDict.Set("Filter", new PdfName("FlateDecode"));
            palDict.Set("Length", new PdfInteger(palFlate.Length));

            var csArray = new PdfArray();
            csArray.Add(new PdfName("Indexed"));
            csArray.Add(new PdfName("DeviceRGB"));
            csArray.Add(new PdfInteger(FlatDctPaletteSlots - 1));
            csArray.Add(new PdfStream(palDict, palFlate));

            var idxFlate = FlatDctFlate(indices);
            img.Dict.Set("ColorSpace", csArray);
            img.Dict.Set("Filter", new PdfName("FlateDecode"));
            img.Dict.Set("Length", new PdfInteger(idxFlate.Length));
            img.ReplaceData(idxFlate);
        }
    }

    /// <summary>Bracket <paramref name="page"/>'s content in q…Q and round
    /// path coordinates beyond the PDF/A-1 ±32767 real-value limit to integers.
    /// A page with inline images is wrapped at the byte level and its
    /// coordinates left untouched: materialising such a stream through the
    /// typed operator list would drop the inline-image binary payload.</summary>
    private void NormalizePdfA1PageContent(Page page)
    {
        const double limit = short.MaxValue; // 32767
        static bool OutOfRange(double v) => Math.Abs(v) >= limit && v != Math.Truncate(v);
        static double Clamp(double v) => OutOfRange(v) ? Math.Round(v) : v;

        // Pre-scan: does any path coordinate exceed the PDF/A-1 real limit? Pages
        // with inline images must not be re-serialised through the typed operator
        // list at all (its BI token carries no binary payload).
        var needsCoordFix = false;
        var hasInline = false;
        var ops = page.Contents;
        foreach (var op in ops)
        {
            switch (op)
            {
                case Operators.BI:
                    hasInline = true;
                    break;
                case Operators.MoveTo m when OutOfRange(m.X) || OutOfRange(m.Y):
                case Operators.LineTo l when OutOfRange(l.X) || OutOfRange(l.Y):
                case Operators.CurveTo c when OutOfRange(c.X1) || OutOfRange(c.Y1)
                    || OutOfRange(c.X2) || OutOfRange(c.Y2) || OutOfRange(c.X3) || OutOfRange(c.Y3):
                    needsCoordFix = true;
                    break;
            }
            if (hasInline) break;
        }

        if (hasInline || !needsCoordFix)
        {
            // Byte-level wrap: keeps the original stream bytes verbatim (their
            // operator text usually compresses tighter than a re-serialisation,
            // and inline-image payloads survive untouched).
            var bytes = page.GetContentStreamBytes() ?? [];
            var head = Encoding.ASCII.GetBytes("q\n");
            var tail = Encoding.ASCII.GetBytes("\nQ");
            var merged = new byte[head.Length + bytes.Length + tail.Length];
            head.CopyTo(merged, 0);
            bytes.CopyTo(merged, head.Length);
            tail.CopyTo(merged, head.Length + bytes.Length);
            page.SetContentStream(merged);
            return;
        }

        ops.Insert(1, new Operators.GSave());
        ops.Add(new Operators.GRestore());
        // The collection is materialised by the insert above, so the enumerator
        // yields the live operator instances; coordinate edits persist through
        // the flush-on-save.
        foreach (var op in ops)
            switch (op)
            {
                case Operators.MoveTo m:
                    m.X = Clamp(m.X); m.Y = Clamp(m.Y);
                    break;
                case Operators.LineTo l:
                    l.X = Clamp(l.X); l.Y = Clamp(l.Y);
                    break;
                case Operators.CurveTo c:
                    c.X1 = Clamp(c.X1); c.Y1 = Clamp(c.Y1);
                    c.X2 = Clamp(c.X2); c.Y2 = Clamp(c.Y2);
                    c.X3 = Clamp(c.X3); c.Y3 = Clamp(c.Y3);
                    break;
            }
    }

    /// <summary>True when the page's content actually paints with transparency:
    /// an /ExtGState carrying a non-opaque alpha (/ca or /CA &lt; 1), a soft mask
    /// other than /None, or a blend mode other than Normal/Compatible. The bare
    /// page /Group declaration does not count — opaque content composites the
    /// same with or without it.</summary>
    private bool PageUsesTransparency(Page page)
    {
        var res = _reader.ResolveDict(page.Dict.Get("Resources"))
                  ?? _reader.ResolveDict(FindInheritedRaw(page.Dict, "Resources"));
        var extGStates = res is not null ? _reader.ResolveDict(res.Get("ExtGState")) : null;
        if (extGStates is null) return false;

        static double NumberOr(PdfObject? o, double fallback) => o switch
        {
            PdfReal r => r.Value,
            PdfInteger i => i.Value,
            _ => fallback,
        };

        foreach (var key in extGStates.Keys)
        {
            var gs = _reader.ResolveDict(extGStates.Get(key));
            if (gs is null) continue;
            if (NumberOr(_reader.Resolve(gs.Get("ca")), 1.0) < 1.0) return true;
            if (NumberOr(_reader.Resolve(gs.Get("CA")), 1.0) < 1.0) return true;
            if (gs.Get("SMask") is { } sm && (_reader.Resolve(sm) as PdfName)?.Value != "None") return true;
            var bm = gs.GetName("BM");
            if (bm is not (null or "Normal" or "Compatible")) return true;
        }
        return false;
    }

    /// <summary>Flatten a transparent page for PDF/X-1a: render the page and replace
    /// its content with a single DeviceCMYK image resource named <c>Im0</c> drawn over
    /// the full page box. Text, vectors and the transparency they used all bake into
    /// the raster; the page keeps no fonts and no /Group.</summary>
    private void FlattenPageToCmykImage(Page page)
    {
        // Raster density for flattened pages: matches the harness render DPI, and at
        // A4 sizes stays well under the PDF/A-1 implementation limits.
        const int flattenDpi = 150;
        var rgba = new Devices.SoftwarePageRenderer().RenderPage(page, flattenDpi);

        // RGB -> naive DeviceCMYK (K = 1-max, remaining channels scaled by 1-K):
        // the standard device conversion; X-1a only requires the DATA be CMYK.
        var cmyk = new byte[rgba.Width * rgba.Height * 4];
        for (int i = 0, o = 0; o < cmyk.Length; i += 4, o += 4)
        {
            double r = rgba.Data[i] / 255.0, g = rgba.Data[i + 1] / 255.0, b = rgba.Data[i + 2] / 255.0;
            double k = 1 - Math.Max(r, Math.Max(g, b));
            double denom = 1 - k;
            cmyk[o] = (byte)Math.Round(255 * (denom <= 0 ? 0 : (1 - r - k) / denom));
            cmyk[o + 1] = (byte)Math.Round(255 * (denom <= 0 ? 0 : (1 - g - k) / denom));
            cmyk[o + 2] = (byte)Math.Round(255 * (denom <= 0 ? 0 : (1 - b - k) / denom));
            cmyk[o + 3] = (byte)Math.Round(255 * k);
        }

        var imgDict = new PdfDictionary();
        imgDict.Set("Type", new PdfName("XObject"));
        imgDict.Set("Subtype", new PdfName("Image"));
        imgDict.Set("Width", new PdfInteger(rgba.Width));
        imgDict.Set("Height", new PdfInteger(rgba.Height));
        imgDict.Set("ColorSpace", new PdfName("DeviceCMYK"));
        imgDict.Set("BitsPerComponent", new PdfInteger(8));
        var imgNum = AllocateObjectNumber();
        AddNewObject(imgNum, new PdfStream(imgDict, cmyk));

        var xObjects = new PdfDictionary();
        xObjects.Set("Im0", new PdfIndirectRef(imgNum, 0));
        var resources = new PdfDictionary();
        resources.Set("XObject", xObjects);
        page.Dict.Set("Resources", resources);

        var box = page.Rect;
        var content = string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "q {0:0.###} 0 0 {1:0.###} 0 0 cm /Im0 Do Q", box.Width, box.Height);
        var csNum = AllocateObjectNumber();
        AddNewObject(csNum, new PdfStream(new PdfDictionary(), Encoding.ASCII.GetBytes(content)));
        page.Dict.Set("Contents", new PdfIndirectRef(csNum, 0));
        page.Dict.Remove("Group");
    }

    /// <summary>Resolve backslash escapes in a PDF literal-string body.</summary>
    private static string UnescapePdfLiteral(string s)
    {
        var sb = new StringBuilder(s.Length);
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (c != '\\' || i + 1 >= s.Length) { sb.Append(c); continue; }
            var n = s[++i];
            switch (n)
            {
                case 'n': sb.Append('\n'); break;
                case 'r': sb.Append('\r'); break;
                case 't': sb.Append('\t'); break;
                case 'b': sb.Append('\b'); break;
                case 'f': sb.Append('\f'); break;
                case '(': case ')': case '\\': sb.Append(n); break;
                default:
                    if (n is >= '0' and <= '7')
                    {
                        var oct = n - '0';
                        for (var k = 0; k < 2 && i + 1 < s.Length && s[i + 1] is >= '0' and <= '7'; k++)
                            oct = oct * 8 + (s[++i] - '0');
                        sb.Append((char)oct);
                    }
                    else sb.Append(n);
                    break;
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Remove text show operators whose every shown code is a certain .notdef
    /// reference: a control-range byte (&lt; 0x20) that the font's /Encoding maps to
    /// no glyph name. PDF/A prohibits any reference to the .notdef glyph; the
    /// violation is logged always, the operator is deleted only when
    /// <paramref name="strip"/> (ConvertErrorAction.Delete). Composite (Type0)
    /// fonts use multi-byte codes and are skipped. Applies to the page content
    /// and, recursively, to every reachable Form XObject.
    /// </summary>
    private void RemoveNotdefGlyphShows(Page page, PdfFormatConversionOptions options, bool strip)
    {
        var resources = _reader.ResolveDict(page.Dict.Get("Resources"));
        if (resources is null) return;

        var pageBytes = page.GetContentStreamBytes();
        if (pageBytes is { Length: > 0 }
            && RewriteNotdefShows(pageBytes, resources, options, page.Number, strip) is { } rewritten)
            page.SetContentStream(rewritten);

        RemoveNotdefGlyphShowsInForms(resources, options, page.Number, strip,
            new HashSet<PdfDictionary>());
    }

    private void RemoveNotdefGlyphShowsInForms(PdfDictionary resources,
        PdfFormatConversionOptions options, int pageNumber, bool strip, HashSet<PdfDictionary> visited)
    {
        var xobjects = _reader.ResolveDict(resources.Get("XObject"));
        if (xobjects is null) return;
        foreach (var key in xobjects.Keys.ToList())
        {
            if (_reader.Resolve(xobjects.Get(key)) is not PdfStream form
                || form.Dict.GetName("Subtype") != "Form"
                || !visited.Add(form.Dict))
                continue;
            var formRes = _reader.ResolveDict(form.Dict.Get("Resources")) ?? resources;
            byte[] data;
            try { data = _reader.DecodeStream(form); } catch { continue; }
            if (RewriteNotdefShows(data, formRes, options, pageNumber, strip) is { } rewritten)
            {
                form.Dict.Remove("Filter");
                form.Dict.Remove("DecodeParms");
                form.Dict.Set("Length", new PdfInteger(rewritten.Length));
                form.ReplaceData(rewritten);
            }
            RemoveNotdefGlyphShowsInForms(formRes, options, pageNumber, strip, visited);
        }
    }

    /// <summary>Token-level scan of one content stream for Tj / TJ operators whose every
    /// shown byte is an unmapped control code (a certain .notdef reference). Logs one
    /// violation per offending operator; when <paramref name="strip"/>, splices the
    /// operator (operands included) out of the stream. Returns null when nothing changed
    /// (or the stream carries inline images, whose binary payload this tokenizer does
    /// not model).</summary>
    private byte[]? RewriteNotdefShows(byte[] contentBytes, PdfDictionary resources,
        PdfFormatConversionOptions options, int pageNumber, bool strip)
    {
        var fonts = _reader.ResolveDict(resources.Get("Font"));
        if (fonts is null) return null;

        // code→glyph-name table per font resource; null = skip the font. Only Type1
        // faces qualify: their glyph lookup is NAME-keyed through the encoding, so a
        // control code with no name is a certain .notdef reference. A TrueType font
        // (esp. a subset with no /Encoding) addresses glyphs through its internal
        // cmap where low codes can be REAL glyphs, and composite (Type0) fonts use
        // multi-byte codes — no verdict is possible from the font dict alone.
        var encodings = new Dictionary<string, string?[]?>(StringComparer.Ordinal);
        string?[]? EncodingFor(string fontName)
        {
            if (encodings.TryGetValue(fontName, out var cached)) return cached;
            string?[]? names = null;
            if (_reader.ResolveDict(fonts.Get(fontName)) is { } fontDict
                && fontDict.GetName("Subtype") is "Type1" or "MMType1")
                names = Devices.SoftwarePageRenderer.ResolveEncoding(fontDict, _reader);
            encodings[fontName] = names;
            return names;
        }

        var text = System.Text.Encoding.Latin1.GetString(contentBytes);
        var deletions = new List<(int start, int end)>();
        string? lastName = null;      // most recent /Name token (Tf operand)
        string? currentFont = null;   // font selected by the last Tf
        int operandStart = -1;        // offset of the first operand token since the last operator
        var strings = new List<byte[]>(); // string operands gathered since the last operator
        var pos = 0;

        void BeginOperand(int at) { if (operandStart < 0) operandStart = at; }
        void EndOperator() { operandStart = -1; strings.Clear(); }

        while (pos < text.Length)
        {
            var c = text[pos];
            if (char.IsWhiteSpace(c)) { pos++; continue; }
            if (c is '[' or ']' or '{' or '}') { BeginOperand(pos); pos++; continue; }
            if (c == '%') // comment to end-of-line
            {
                while (pos < text.Length && text[pos] != '\n' && text[pos] != '\r') pos++;
                continue;
            }
            if (c == '(') // literal string, with escapes and balanced parens
            {
                BeginOperand(pos);
                var end = pos + 1;
                var depth = 1;
                while (end < text.Length && depth > 0)
                {
                    var sc = text[end];
                    if (sc == '\\') end++;
                    else if (sc == '(') depth++;
                    else if (sc == ')') depth--;
                    end++;
                }
                strings.Add(DecodeLiteralStringBytes(text, pos + 1, end - 1));
                pos = end; continue;
            }
            if (c == '<')
            {
                if (pos + 1 < text.Length && text[pos + 1] == '<') // dict
                { BeginOperand(pos); pos += 2; continue; }
                BeginOperand(pos);
                var end = text.IndexOf('>', pos + 1);
                if (end < 0) end = text.Length - 1;
                strings.Add(DecodeHexStringBytes(text, pos + 1, end));
                pos = end + 1; continue;
            }
            if (c == '>' && pos + 1 < text.Length && text[pos + 1] == '>')
            { BeginOperand(pos); pos += 2; continue; }
            if (c == '/') // name token
            {
                BeginOperand(pos);
                var end = pos + 1;
                while (end < text.Length && !char.IsWhiteSpace(text[end])
                       && text[end] is not ('/' or '(' or ')' or '<' or '>' or '[' or ']' or '{' or '}' or '%'))
                    end++;
                lastName = text[(pos + 1)..end];
                pos = end; continue;
            }

            // Regular token (number or operator).
            {
                var end = pos;
                while (end < text.Length && !char.IsWhiteSpace(text[end])
                       && text[end] is not ('/' or '(' or ')' or '<' or '>' or '[' or ']' or '{' or '}' or '%'))
                    end++;
                // A stray delimiter byte (an unbalanced ')' or a lone '>') yields an
                // empty token with end == pos — skip the byte or the scan never advances.
                if (end == pos) { pos++; continue; }
                var token = text[pos..end];
                var isNumber = char.IsAsciiDigit(token[0]) || token[0] is '+' or '-' or '.';
                if (isNumber) { BeginOperand(pos); pos = end; continue; }

                switch (token)
                {
                    case "BI":
                        return null; // inline image: bail out, keep original bytes
                    case "Tf":
                        currentFont = lastName;
                        break;
                    case "Tj" or "TJ" when strings.Count > 0 && currentFont is not null
                        && EncodingFor(currentFont) is { } names:
                    {
                        var sawCode = false;
                        var allNotdef = true;
                        foreach (var s in strings)
                            foreach (var b in s)
                            {
                                sawCode = true;
                                if (b >= 0x20 || names[b] is not (null or ".notdef"))
                                { allNotdef = false; break; }
                            }
                        if (sawCode && allNotdef)
                        {
                            options.ConversionLog.Add(new PdfAViolation
                            {
                                Rule = "NotdefGlyph",
                                Description = $"Page {pageNumber} text show operator references only the .notdef glyph"
                                    + (strip ? " — operator removed." : "."),
                                PageNumber = pageNumber,
                            });
                            if (strip && operandStart >= 0)
                                deletions.Add((operandStart, end));
                        }
                        break;
                    }
                }
                EndOperator();
                pos = end;
            }
        }

        if (deletions.Count == 0) return null;

        var output = new List<byte>(contentBytes.Length);
        var copyFrom = 0;
        foreach (var (start, end) in deletions)
        {
            for (var i = copyFrom; i < start; i++) output.Add(contentBytes[i]);
            output.Add((byte)' '); // keep neighbouring tokens separated
            copyFrom = end;
        }
        for (var i = copyFrom; i < contentBytes.Length; i++) output.Add(contentBytes[i]);
        return output.ToArray();
    }

    /// <summary>Decode the raw bytes of a literal PDF string body (between the outer
    /// parens, exclusive) — escapes and octal sequences per PDF 32000 §7.3.4.2.</summary>
    private static byte[] DecodeLiteralStringBytes(string text, int start, int end)
    {
        var bytes = new List<byte>(end - start);
        for (var i = start; i < end && i < text.Length; i++)
        {
            var ch = text[i];
            if (ch != '\\') { bytes.Add((byte)ch); continue; }
            if (++i >= end) break;
            var e = text[i];
            switch (e)
            {
                case 'n': bytes.Add((byte)'\n'); break;
                case 'r': bytes.Add((byte)'\r'); break;
                case 't': bytes.Add((byte)'\t'); break;
                case 'b': bytes.Add((byte)'\b'); break;
                case 'f': bytes.Add((byte)'\f'); break;
                case '\r': if (i + 1 < end && text[i + 1] == '\n') i++; break; // line continuation
                case '\n': break;
                case >= '0' and <= '7':
                {
                    var oct = e - '0';
                    for (var k = 0; k < 2 && i + 1 < end && text[i + 1] is >= '0' and <= '7'; k++)
                        oct = oct * 8 + (text[++i] - '0');
                    bytes.Add((byte)oct);
                    break;
                }
                default: bytes.Add((byte)e); break;
            }
        }
        return bytes.ToArray();
    }

    /// <summary>Decode the raw bytes of a hex PDF string body (between &lt; and &gt;,
    /// exclusive); an odd trailing digit is padded with 0 per PDF 32000 §7.3.4.3.</summary>
    private static byte[] DecodeHexStringBytes(string text, int start, int end)
    {
        var bytes = new List<byte>((end - start) / 2 + 1);
        var hi = -1;
        for (var i = start; i < end && i < text.Length; i++)
        {
            var ch = text[i];
            var v = ch switch
            {
                >= '0' and <= '9' => ch - '0',
                >= 'A' and <= 'F' => ch - 'A' + 10,
                >= 'a' and <= 'f' => ch - 'a' + 10,
                _ => -1,
            };
            if (v < 0) continue;
            if (hi < 0) hi = v;
            else { bytes.Add((byte)(hi * 16 + v)); hi = -1; }
        }
        if (hi >= 0) bytes.Add((byte)(hi * 16));
        return bytes.ToArray();
    }

    /// <summary>
    /// Rewrite paint operators executed under a FULLY transparent graphics state
    /// (ExtGState /ca 0 for fills, /CA 0 for strokes) into no-ops, so the PDF/A-1
    /// alpha neutralisation (ca/CA → 1) cannot turn invisible content into opaque
    /// paint. Applies to the page content and, recursively, to every reachable
    /// Form XObject (each against its own resources). Streams with inline images
    /// keep their bytes untouched.
    /// </summary>
    private void SuppressAlphaZeroPaint(Page page)
    {
        var resources = _reader.ResolveDict(page.Dict.Get("Resources"));
        if (resources is null) return;
        var visited = new HashSet<PdfDictionary>();

        var pageBytes = page.GetContentStreamBytes();
        if (pageBytes is { Length: > 0 }
            && RewriteAlphaZeroPaint(pageBytes, resources) is { } rewritten)
            page.SetContentStream(rewritten);

        SuppressAlphaZeroPaintInForms(resources, visited);
    }

    private void SuppressAlphaZeroPaintInForms(PdfDictionary resources, HashSet<PdfDictionary> visited)
    {
        var xobjects = _reader.ResolveDict(resources.Get("XObject"));
        if (xobjects is null) return;
        foreach (var key in xobjects.Keys.ToList())
        {
            if (_reader.Resolve(xobjects.Get(key)) is not PdfStream form
                || form.Dict.GetName("Subtype") != "Form"
                || !visited.Add(form.Dict))
                continue;
            var formRes = _reader.ResolveDict(form.Dict.Get("Resources")) ?? resources;
            byte[] data;
            try { data = _reader.DecodeStream(form); } catch { continue; }
            if (RewriteAlphaZeroPaint(data, formRes) is { } rewritten)
            {
                form.Dict.Remove("Filter");
                form.Dict.Remove("DecodeParms");
                form.Dict.Set("Length", new PdfInteger(rewritten.Length));
                form.ReplaceData(rewritten);
            }
            SuppressAlphaZeroPaintInForms(formRes, visited);
        }
    }

    /// <summary>Token-level rewrite of one content stream: paint operators active under
    /// an alpha-0 ExtGState become <c>n</c> (or drop just the dead half of a fill+stroke).
    /// Returns null when nothing needed changing (or the stream carries inline images,
    /// whose binary payload this tokenizer does not model).</summary>
    private byte[]? RewriteAlphaZeroPaint(byte[] contentBytes, PdfDictionary resources)
    {
        var extGStates = _reader.ResolveDict(resources.Get("ExtGState"));
        if (extGStates is null) return null;

        var fillZero = new HashSet<string>(StringComparer.Ordinal);
        var strokeZero = new HashSet<string>(StringComparer.Ordinal);
        var fillSet = new HashSet<string>(StringComparer.Ordinal);   // gs entries that SET ca (any value)
        var strokeSet = new HashSet<string>(StringComparer.Ordinal); // gs entries that SET CA
        foreach (var key in extGStates.Keys)
        {
            var gs = _reader.ResolveDict(extGStates.Get(key));
            if (gs is null) continue;
            if (gs.Get("ca") is not null)
            {
                fillSet.Add(key);
                if (AlphaValue(gs.Get("ca")) == 0.0) fillZero.Add(key);
            }
            if (gs.Get("CA") is not null)
            {
                strokeSet.Add(key);
                if (AlphaValue(gs.Get("CA")) == 0.0) strokeZero.Add(key);
            }
        }
        if (fillZero.Count == 0 && strokeZero.Count == 0) return null;

        var text = System.Text.Encoding.Latin1.GetString(contentBytes);
        var output = new StringBuilder(text.Length);
        var stack = new Stack<(bool fill0, bool stroke0)>();
        bool fill0 = false, stroke0 = false;
        string? lastName = null;
        var changed = false;
        var pos = 0;

        while (pos < text.Length)
        {
            var c = text[pos];
            // Delimiters and non-token content are copied verbatim.
            if (char.IsWhiteSpace(c) || c is '[' or ']' or '{' or '}')
            { output.Append(c); pos++; continue; }
            if (c == '%') // comment to end-of-line
            {
                var eol = pos;
                while (eol < text.Length && text[eol] != '\n' && text[eol] != '\r') eol++;
                output.Append(text, pos, eol - pos); pos = eol; continue;
            }
            if (c == '(') // literal string, with escapes and balanced parens
            {
                var end = pos + 1;
                var depth = 1;
                while (end < text.Length && depth > 0)
                {
                    var sc = text[end];
                    if (sc == '\\') end++;
                    else if (sc == '(') depth++;
                    else if (sc == ')') depth--;
                    end++;
                }
                output.Append(text, pos, end - pos); pos = end; continue;
            }
            if (c == '<')
            {
                if (pos + 1 < text.Length && text[pos + 1] == '<') // dict
                { output.Append("<<"); pos += 2; continue; }
                var end = text.IndexOf('>', pos + 1);
                if (end < 0) end = text.Length - 1;
                output.Append(text, pos, end - pos + 1); pos = end + 1; continue;
            }
            if (c == '>' && pos + 1 < text.Length && text[pos + 1] == '>')
            { output.Append(">>"); pos += 2; continue; }
            if (c == '/') // name token
            {
                var end = pos + 1;
                while (end < text.Length && !char.IsWhiteSpace(text[end])
                       && text[end] is not ('/' or '(' or ')' or '<' or '>' or '[' or ']' or '{' or '}' or '%'))
                    end++;
                lastName = text[(pos + 1)..end];
                output.Append(text, pos, end - pos); pos = end; continue;
            }

            // Regular token (number or operator).
            {
                var end = pos;
                while (end < text.Length && !char.IsWhiteSpace(text[end])
                       && text[end] is not ('/' or '(' or ')' or '<' or '>' or '[' or ']' or '{' or '}' or '%'))
                    end++;
                var token = text[pos..end];
                string? replacement = null;
                switch (token)
                {
                    case "BI":
                        return null; // inline image: bail out, keep original bytes
                    case "q":
                        stack.Push((fill0, stroke0));
                        break;
                    case "Q":
                        if (stack.Count > 0) (fill0, stroke0) = stack.Pop();
                        break;
                    case "gs" when lastName is not null:
                        if (fillZero.Contains(lastName)) fill0 = true;
                        else if (fillSet.Contains(lastName)) fill0 = false;
                        if (strokeZero.Contains(lastName)) stroke0 = true;
                        else if (strokeSet.Contains(lastName)) stroke0 = false;
                        break;
                    case "f" or "F" or "f*" when fill0:
                    case "S" or "s" when stroke0:
                    case "B" or "B*" or "b" or "b*" when fill0 && stroke0:
                        replacement = "n";
                        break;
                    case "B" or "B*" when fill0: replacement = "S"; break;
                    case "b" or "b*" when fill0: replacement = "s"; break;
                    case "B" or "B*" or "b" or "b*" when stroke0: replacement = "f"; break;
                }
                if (replacement is not null) { output.Append(replacement); changed = true; }
                else output.Append(token);
                pos = end;
            }
        }

        return changed ? System.Text.Encoding.Latin1.GetBytes(output.ToString()) : null;
    }

    /// <summary>
    /// Neutralise transparency declared in graphics-state (ExtGState) dictionaries reachable
    /// from <paramref name="container"/> (a page or Form XObject): soft masks, constant alpha
    /// below 1, and non-Normal blend modes are all prohibited by PDF/A-1. Soft masks are set
    /// to /None, alpha to 1 and blend mode to /Normal so the content renders opaquely instead
    /// of failing validation. Recurses into nested Form XObjects; the visited set guards
    /// against shared dictionaries and reference cycles.
    /// </summary>
    private void NeutralizeExtGStateTransparency(PdfDictionary container,
        PdfFormatConversionOptions options, int pageNumber, bool fix, HashSet<PdfDictionary> visited)
    {
        var resources = _reader.ResolveDict(container.Get("Resources"));
        if (resources is null) return;

        var extGStates = _reader.ResolveDict(resources.Get("ExtGState"));
        if (extGStates is not null)
        {
            foreach (var key in extGStates.Keys.ToList())
            {
                var gs = _reader.ResolveDict(extGStates.Get(key));
                if (gs is null || !visited.Add(gs)) continue;

                var changed = false;
                var smask = gs.Get("SMask");
                if (smask is not null && smask is not PdfName { Value: "None" })
                {
                    changed = true;
                    if (fix) gs.Set("SMask", new PdfName("None"));
                }
                if (IsAlphaBelowOne(gs.Get("ca")))
                {
                    changed = true;
                    if (fix) gs.Set("ca", new PdfReal(1));
                }
                if (IsAlphaBelowOne(gs.Get("CA")))
                {
                    changed = true;
                    if (fix) gs.Set("CA", new PdfReal(1));
                }
                var bm = gs.GetName("BM");
                if (bm is not null && bm != "Normal" && bm != "Compatible")
                {
                    changed = true;
                    if (fix) gs.Set("BM", new PdfName("Normal"));
                }

                if (changed)
                    options.ConversionLog.Add(new PdfAViolation
                    {
                        Rule = "Transparency",
                        Description = $"Page {pageNumber} ExtGState '{key}' transparency neutralized for PDF/A-1.",
                        PageNumber = pageNumber,
                    });
            }
        }

        // Recurse into Form XObjects, whose own resources may carry transparency.
        var xobjects = _reader.ResolveDict(resources.Get("XObject"));
        if (xobjects is null) return;
        foreach (var key in xobjects.Keys.ToList())
        {
            if (_reader.Resolve(xobjects.Get(key)) is PdfStream { } form
                && form.Dict.GetName("Subtype") == "Form"
                && visited.Add(form.Dict))
            {
                NeutralizeExtGStateTransparency(form.Dict, options, pageNumber, fix, visited);
            }
        }
    }

    private static bool IsAlphaBelowOne(PdfObject? value) => value switch
    {
        PdfReal r => r.Value < 1.0,
        PdfInteger i => i.Value < 1,
        _ => false,
    };

    private static double AlphaValue(PdfObject? value) => value switch
    {
        PdfReal r => r.Value,
        PdfInteger i => i.Value,
        _ => 1.0,
    };

    /// <summary>Walk a content stream tracking the current fill alpha (set by <c>/GS gs</c>
    /// against the resources' ExtGState /ca, saved/restored by q/Q) and, for every image
    /// XObject drawn while that alpha is below 1, bake the alpha into a constant DeviceGray
    /// soft mask on the image (unless it already carries a mask). This preserves the image's
    /// composited appearance once the prohibited ExtGState alpha is neutralised for PDF/A-1.
    /// Recurses into invoked Form XObjects, carrying the alpha active at their draw.</summary>
    private void MaskConstantAlphaImages(byte[] content, PdfDictionary resources,
        double initialAlpha, HashSet<PdfDictionary> visitedForms)
    {
        var extg = _reader.ResolveDict(resources.Get("ExtGState"));
        var xobjects = _reader.ResolveDict(resources.Get("XObject"));

        var lexer = new IO.PdfLexer(content);
        var stack = new Stack<double>();
        var curAlpha = initialAlpha;
        string? lastName = null;
        // Form name -> the alpha active where it was invoked (last wins; a form drawn only
        // opaquely stays opaque). Recursed after the scan so lexer state is untouched.
        var formAlpha = new Dictionary<string, double>(StringComparer.Ordinal);

        while (true)
        {
            var t = lexer.NextToken();
            if (t.Kind == IO.TokenKind.Eof) break;
            if (t.Kind == IO.TokenKind.Keyword && t.StringValue == "BI")
            {
                SkipInlineImage(lexer, new HashSet<string>());
                lastName = null;
                continue;
            }
            if (t.Kind == IO.TokenKind.Name) { lastName = t.StringValue; continue; }
            if (t.Kind != IO.TokenKind.Keyword) continue;

            switch (t.StringValue)
            {
                case "q":
                    stack.Push(curAlpha);
                    break;
                case "Q":
                    if (stack.Count > 0) curAlpha = stack.Pop();
                    break;
                case "gs":
                    if (lastName is not null && extg is not null &&
                        _reader.ResolveDict(extg.Get(lastName)) is { } gs)
                        curAlpha = AlphaValue(gs.Get("ca"));
                    break;
                case "Do":
                    if (lastName is not null && xobjects is not null &&
                        _reader.ResolveStream(xobjects.Get(lastName)) is { } xs)
                    {
                        var sub = xs.Dict.GetName("Subtype");
                        if (sub == "Image")
                        {
                            if (curAlpha < 1.0 - 1e-6) AttachConstantSoftMask(xs.Dict, curAlpha);
                        }
                        else if (sub == "Form" && curAlpha < 1.0 - 1e-6)
                        {
                            formAlpha[lastName] = curAlpha;
                        }
                    }
                    break;
            }
            lastName = null;
        }

        if (xobjects is null) return;
        foreach (var (name, alpha) in formAlpha)
        {
            var xs = _reader.ResolveStream(xobjects.Get(name));
            if (xs is null || xs.Dict.GetName("Subtype") != "Form") continue;
            if (!visitedForms.Add(xs.Dict)) continue;
            var formContent = _reader.DecodeStream(xs);
            if (formContent.Length == 0) continue;
            var formRes = _reader.ResolveDict(xs.Dict.Get("Resources")) ?? resources;
            MaskConstantAlphaImages(formContent, formRes, alpha, visitedForms);
        }
    }

    /// <summary>Attach a 1×1 constant DeviceGray <c>/SMask</c> of value <paramref name="alpha"/>
    /// to an image XObject so it composites at that opacity. No-op if the image already carries
    /// a soft mask or stencil mask (its existing transparency is preserved as-is).</summary>
    private void AttachConstantSoftMask(PdfDictionary imgDict, double alpha)
    {
        if (imgDict.Get("SMask") is not null || imgDict.Get("Mask") is not null) return;

        var smDict = new PdfDictionary();
        smDict.Set("Type", new PdfName("XObject"));
        smDict.Set("Subtype", new PdfName("Image"));
        smDict.Set("Width", new PdfInteger(1));
        smDict.Set("Height", new PdfInteger(1));
        smDict.Set("ColorSpace", new PdfName("DeviceGray"));
        smDict.Set("BitsPerComponent", new PdfInteger(8));
        var data = new byte[] { (byte)Math.Round(Math.Clamp(alpha, 0.0, 1.0) * 255.0) };
        smDict.Set("Length", new PdfInteger(data.Length));

        var objNum = AllocateObjectNumber();
        AddNewObject(objNum, new PdfStream(smDict, data));
        imgDict.Set("SMask", new PdfIndirectRef(objNum, 0));
    }

    /// <summary>Report clause-6.2.11.8 violations: an Identity-encoded Type0 font
    /// whose shown text contains the 2-byte code 0000 references the .notdef glyph.
    /// One problem per font (its Type0 dict's object number as ObjectID, the first
    /// page it is seen on, the BaseFont name without its subset prefix).</summary>
    private void ReportNotdefGlyphReferences(PdfFormatConversionOptions options)
    {
        var reported = new HashSet<int>();
        int pageCount;
        try { pageCount = Pages.Count; } catch { return; }
        for (var p = 1; p <= pageCount; p++)
        {
            Page page;
            try { page = Pages[p]; } catch { continue; }

            // Font resources, honouring page-tree inheritance of /Resources.
            var resources = ResolveInheritedResources(page.Dict);
            var fonts = resources is null ? null : _reader.ResolveDict(resources.Get("Font"));
            if (fonts is null) continue;

            // resource name -> (Type0 dict object number, display name) for
            // Identity-encoded Type0 fonts on this page.
            var identityFonts = new Dictionary<string, (int Num, string Name)>(StringComparer.Ordinal);
            foreach (var key in fonts.Keys)
            {
                var raw = fonts.Get(key);
                var fd = _reader.ResolveDict(raw);
                if (fd is null || fd.GetName("Subtype") != "Type0") continue;
                if (fd.GetName("Encoding") is not ("Identity-H" or "Identity-V")) continue;
                var num = raw is PdfIndirectRef ir ? ir.ObjectNumber : FindObjectNumber(fd);
                if (num <= 0) continue;
                identityFonts[key] = (num, StripSubsetPrefix(fd.GetName("BaseFont") ?? string.Empty));
            }
            if (identityFonts.Count == 0) continue;

            string? currentFont = null;
            System.Collections.Generic.IEnumerable<Operator> ops;
            try { ops = page.Contents; } catch { continue; }
            foreach (var op in ops)
            {
                if (op is Aspose.Pdf.Operators.SelectFont sf) { currentFont = sf.FontName; continue; }
                if (op is not Aspose.Pdf.Operators.TextShowOperator show || currentFont is null) continue;
                if (!identityFonts.TryGetValue(currentFont, out var info) || reported.Contains(info.Num)) continue;
                if (!HasAlignedCidZero(show.Text)) continue;
                reported.Add(info.Num);
                options.ConversionLog.Add(new Optimization.PdfAViolation
                {
                    Rule = "FontNotdefGlyph",
                    Clause = "6.2.11.8",
                    Description = $"Character references .notdef glyph in font \"{info.Name}\"",
                    PageNumber = p,
                    ObjectId = info.Num.ToString(),
                });
            }
        }
    }
}
