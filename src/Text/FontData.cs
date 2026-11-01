namespace Aspose.Pdf.Text;

/// <summary>Represents a font with its name and type.</summary>
public sealed class FontData
{
    internal FontData(string name, FontType type, string? filePath = null)
    {
        FontName = name;
        Type = type;
        FilePath = filePath;
    }

    /// <summary>The font name.</summary>
    public string FontName { get; }

    /// <summary>The font type.</summary>
    public FontType Type { get; }

    /// <summary>File path if loaded from a file.</summary>
    public string? FilePath { get; }

    /// <summary>Why this face could not be embedded on the last save, or null when it
    /// embedded cleanly. It rides on the FontData rather than the Font because the
    /// embedding writer is handed the face program, not the public wrapper.</summary>
    internal string? LastEmbeddingError { get; set; }

    /// <summary>Whether a refused embedding is raised as a
    /// <see cref="FontEmbeddingException"/>. Reporting is the default; a caller opts out
    /// to let the save finish and read the reason afterwards.</summary>
    internal bool NotifyAboutEmbeddingError { get; set; } = true;

    /// <summary>True when the face's own licence forbids embedding it. OS/2 fsType
    /// (OpenType spec, "Type flags") states what a licensee may do: 0 installable, 8
    /// editable and 4 preview-and-print all allow a program in a document that is viewed
    /// and printed, which a PDF is; only 2 (restricted) forbids it. The remaining bits
    /// (no-subsetting, bitmap-only) do not bear on permission.</summary>
    internal static bool EmbeddingForbiddenByLicence(byte[]? program)
    {
        var (version, fsType) = ReadFsType(program);
        if (fsType < 0) return false;              // no OS/2 table: nothing forbids it
        // Only an OS/2 that POST-DATES the current type-flag definitions is taken as a
        // licence statement. Versions 0 and 1 predate them, and a bit set there does not
        // mean what the same bit means today (probed on the type flags: a version-1 face's
        // flag is ignored, a version-2 face's flag 2 is refused).
        if (version < FsTypeMeaningfulOs2Version) return false;
        return (fsType & 0x0F) == RestrictedLicenceFsType;
    }

    /// <summary>Record — and by default raise — the refusal to embed a face whose own
    /// licence does not permit it. Returns true when the caller must leave the program
    /// out and reference the face by name instead. <paramref name="document"/> is the
    /// document the face is about to enter: a conformance conversion or an explicit
    /// <see cref="Document.DisableFontLicenseVerifications"/> lifts the refusal.</summary>
    internal static bool RefuseEmbedding(FontData fontData, Document? document)
    {
        if (!EmbeddingForbiddenByLicence(fontData.TtfData)) return false;
        // A conformance conversion embeds regardless: the format's requirement outranks
        // the face's licence for as long as the conversion runs. A caller that turned
        // the verification off has taken the licence question on itself.
        if (document is { EmbeddingLicenceOverridden: true }) return false;
        if (document is { DisableFontLicenseVerifications: true }) return false;
        fontData.LastEmbeddingError =
            $"Font embedding is prohibited because of font license restrictions ({fontData.FontName})";
        if (fontData.NotifyAboutEmbeddingError)
            throw new FontEmbeddingException(fontData.LastEmbeddingError);
        return true;
    }

    /// <summary>The OS/2 version from which the fsType type flags carry their present
    /// meaning; an earlier table's flags are not read as a licence.</summary>
    private const int FsTypeMeaningfulOs2Version = 2;

    /// <summary>fsType 2 — the face may not be embedded at all.</summary>
    private const int RestrictedLicenceFsType = 2;

    private static int U16(byte[] d, int o) => (d[o] << 8) | d[o + 1];
    private static uint U32(byte[] d, int o) =>
        ((uint)d[o] << 24) | ((uint)d[o + 1] << 16) | ((uint)d[o + 2] << 8) | d[o + 3];

    /// <summary>The OS/2 table's version and fsType word, or (-1, -1) when the face
    /// carries no readable OS/2.</summary>
    private static (int version, int fsType) ReadFsType(byte[]? data)
    {
        if (data is not { Length: > 12 }) return (-1, -1);
        var baseOff = 0;
        if (data.Length >= 16 && data[0] == (byte)'t' && data[1] == (byte)'t'
            && data[2] == (byte)'c' && data[3] == (byte)'f')
        {
            baseOff = (int)U32(data, 12);
            if (baseOff < 0 || baseOff + 12 > data.Length) return (-1, -1);
        }
        var numTables = U16(data, baseOff + 4);
        for (var i = 0; i < numTables; i++)
        {
            var offset = baseOff + 12 + i * 16;
            if (offset + 16 > data.Length) break;
            if (System.Text.Encoding.ASCII.GetString(data, offset, 4) != "OS/2") continue;
            var tOffset = (int)U32(data, offset + 8);
            if (tOffset + 10 > data.Length) return (-1, -1);
            return (U16(data, tOffset), U16(data, tOffset + 8));
        }
        return (-1, -1);
    }

    /// <summary>Raw TTF data when loaded from a file. Lazy-loaded and shared
    /// across every <see cref="FontData"/> that names the same file: the bytes
    /// are immutable font data, so a process-wide by-path cache turns thousands
    /// of independent look-ups of the same face (e.g. every table cell resolving
    /// "Arial") into a single read instead of one full copy per instance.</summary>
    internal byte[]? TtfData => _ttfData ??= FilePath is not null && Type != FontType.Type1 ? SingleFace(LoadTtf(FilePath)) : null;
    private byte[]? _ttfData;

    // A face opened from a TrueType collection (simsun.ttc, msgothic.ttc) is its first face as
    // a font program of its own: the collection's bytes are no font program, so embedded as
    // one they neither subset nor conform. The name-table lookup picks its face itself.
    private static byte[] SingleFace(byte[] data) =>
        data.Length > 4 && data[0] == 't' && data[1] == 't' && data[2] == 'c' && data[3] == 'f'
            ? CjkFallbackFont.NormalizeToSfnt(data, 0) : data;

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte[]> _ttfByPath = new();

    private static byte[] LoadTtf(string path)
        => _ttfByPath.GetOrAdd(path, System.IO.File.ReadAllBytes);

    internal void SetTtfData(byte[] data) => _ttfData = data;

    /// <summary>The bytes of the file the face was opened from, whatever its kind.</summary>
    internal byte[]? FileBytes => TtfData ?? (FilePath is not null ? LoadTtf(FilePath) : null);

    /// <summary>The AFM file that goes with a Type 1 program, when one was given or lies
    /// beside it under the same name.</summary>
    internal string? MetricsFilePath { get; set; }

    /// <summary>The Type 1 program this face was opened from, with its metrics; null for
    /// any other face, or a file that is not a readable PFB or PFA. A Type 1 program is
    /// not TrueType data, and handing it to the TrueType embedder set every glyph as 0.</summary>
    internal Type1FontFile? Type1
    {
        get
        {
            if (_type1Read) return _type1;
            _type1Read = true;
            if (Type != FontType.Type1 || FilePath is null) return null;
            var metrics = MetricsFilePath ?? System.IO.Path.ChangeExtension(FilePath, ".afm");
            _type1 = Type1FontFile.TryRead(LoadTtf(FilePath),
                System.IO.File.Exists(metrics) ? System.IO.File.ReadAllText(metrics, Compat.Latin1) : null);
            return _type1;
        }
    }
    private Type1FontFile? _type1;
    private bool _type1Read;

    /// <summary>The predefined CJK face this font names, declared without a program; null for
    /// any other face.</summary>
    internal PredefinedCidFont? PredefinedCid { get; set; }

    /// <summary>
    /// Measure the width of a string at the given font size in points.
    /// Uses actual TrueType glyph widths (raw, not rounded) when available.
    /// </summary>
    public double MeasureString(string text, double fontSize)
    {
        EnsureRawMetrics();
        if (_rawGlyphWidths is not null && _upm > 0)
        {
            // Use raw (unrounded) glyph widths for highest precision.
            double total = 0;
            foreach (var ch in text)
            {
                int idx = ch < 256 ? ch : '?';
                total += _rawGlyphWidths[idx];
            }
            return total * fontSize / _upm;
        }
        if (Type1 is { } type1) return type1.Measure(text, fontSize);
        if (PredefinedCid is { } cid) return cid.Measure(text, fontSize);
        // Fallback for Type1/unknown without TTF data
        return text.Length * fontSize * 0.5;
    }

    private int[]? _rawGlyphWidths; // raw TTF widths in font units (not scaled to 1/1000)
    private int _upm; // unitsPerEm

    private void EnsureRawMetrics()
    {
        if (_rawGlyphWidths is not null) return;
        if (TtfData is not { Length: > 12 }) return;
        var (glyphWidths, upm) = FontRepository.ReadTtfRawMetrics(TtfData);
        _rawGlyphWidths = glyphWidths;
        _upm = upm;
    }
}
