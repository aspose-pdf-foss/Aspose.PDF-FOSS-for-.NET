using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf;

/// <summary>
/// Represents the document information dictionary.
/// </summary>
public sealed class DocumentInfo
{
    private PdfDictionary? _dict;
    private readonly PdfReader? _reader;
    private readonly Document? _document;
    private bool _isDirty;
    private bool _modDateExplicitlySet;

    internal DocumentInfo(PdfDictionary? dict, PdfReader? reader = null, Document? document = null)
    {
        _dict = dict;
        _reader = reader;
        _document = document;
    }

    /// <summary>
    /// Creates a DocumentInfo instance for the given document.
    /// </summary>
    public DocumentInfo(Document document) : this(null, null, document)
    {
    }

    /// <summary>Whether any property has been modified since the last save/load.</summary>
    public bool IsDirty => _isDirty;

    /// <summary>
    /// Number of metadata entries in the underlying /Info dictionary. Empty
    /// when the document has no /Info dict.
    /// </summary>
    public int Count { get { EnsureDictForRead(); return _dict?.Count ?? 0; } }

    /// <summary>All metadata entry keys.</summary>
    public IEnumerable<string> Keys { get { EnsureDictForRead(); return _dict?.Keys ?? Enumerable.Empty<string>(); } }

    /// <summary>
    /// Whether the given key is one of the well-known PDF document information
    /// dictionary entries defined by PDF 32000-2 § 14.3.3 — Title, Author,
    /// Subject, Keywords, Creator, Producer, CreationDate, ModDate, Trapped.
    /// </summary>
    public static bool IsPredefinedKey(string key) =>
        key is "Title" or "Author" or "Subject" or "Keywords" or "Creator"
            or "Producer" or "CreationDate" or "ModDate" or "Trapped";

    /// <summary>Value surfaced for an absent predefined string entry: a NEW
    /// document reads them as empty strings, a LOADED document
    /// as null.</summary>
    private string? MissingString => _document?.IsNewDocument == true ? string.Empty : null;

    public string? Title
    {
        get => GetString("Title") ?? MissingString;
        set => SetString("Title", value);
    }

    public string? Author
    {
        get => GetString("Author") ?? MissingString;
        set => SetString("Author", value);
    }

    public string? Subject
    {
        get => GetString("Subject") ?? MissingString;
        set => SetString("Subject", value);
    }

    public string? Keywords
    {
        get => GetString("Keywords") ?? MissingString;
        set => SetString("Keywords", value);
    }

    public string? Creator
    {
        get => GetString("Creator") ?? MissingString;
        set => SetString("Creator", value);
    }

    public string? Producer
    {
        get => GetString("Producer") ?? MissingString;
        set => SetString("Producer", value);
    }

    public string? Trapped
    {
        get => GetName("Trapped");
        set
        {
            if (value is not null && value is not "True" and not "False" and not "Unknown")
                throw new ArgumentException(
                    "Trapped value must be 'True', 'False', 'Unknown', or null.",
                    nameof(value));
            SetName("Trapped", value);
        }
    }

    public DateTime CreationDate
    {
        get => ParseDate(GetString("CreationDate")) ?? DateTime.MinValue;
        set => SetDate("CreationDate", value == DateTime.MinValue ? null : value);
    }

    public DateTime ModDate
    {
        get => ParseDate(GetString("ModDate")) ?? DateTime.MinValue;
        set
        {
            SetDate("ModDate", value == DateTime.MinValue ? null : value);
            _modDateExplicitlySet = true;
        }
    }

    /// <summary>True when ModDate was assigned via the public setter on this
    /// DocumentInfo instance — Document.ToArray() consults this to decide whether
    /// to auto-stamp ModDate with the current UTC time. PDF convention is to
    /// update /ModDate on every save, but a user-supplied value wins.</summary>
    internal bool ModDateExplicitlySet => _modDateExplicitlySet;

    /// <summary>Auto-stamp ModDate during Save without flipping the
    /// "user set this" flag, so a second Save still re-stamps to the new
    /// current time instead of holding the previous Save's timestamp.</summary>
    internal void StampModDateOnSave(DateTime value)
        => SetDate("ModDate", value);

    private TimeSpan? _creationTimeZoneOverride;
    private TimeSpan? _modTimeZoneOverride;

    /// <summary>Timezone offset stored on the CreationDate metadata, or
    /// <see cref="TimeSpan.Zero"/> if absent.</summary>
    public TimeSpan CreationTimeZone
    {
        get => _creationTimeZoneOverride ?? ParseTimeZone(GetString("CreationDate"));
        set
        {
            _creationTimeZoneOverride = value;
            ReencodeDateTimeZone("CreationDate", value);
        }
    }

    /// <summary>Timezone offset stored on the ModDate metadata, or
    /// <see cref="TimeSpan.Zero"/> if absent.</summary>
    public TimeSpan ModTimeZone
    {
        get => _modTimeZoneOverride ?? ParseTimeZone(GetString("ModDate"));
        set
        {
            _modTimeZoneOverride = value;
            ReencodeDateTimeZone("ModDate", value);
        }
    }

    /// <summary>Rewrite an already-stored date entry's PDF date string so it
    /// carries the timezone offset. The date components are preserved; only the
    /// trailing <c>Z</c>/offset is replaced. No-op when the entry isn't set yet —
    /// the offset is then applied later by <see cref="SetDate"/> when the date is
    /// assigned (it consults the stored override).</summary>
    private void ReencodeDateTimeZone(string key, TimeSpan tz)
    {
        EnsureDictForRead();
        var dt = ParseDate(GetString(key));
        if (dt is null) return;
        EnsureDict();
        _dict?.Set(key, new PdfString(Encoding.Latin1.GetBytes(FormatPdfDate(dt.Value, tz))));
        FlushDirty();
    }

    /// <summary>Remove every entry from the /Info dictionary.</summary>
    public void Clear()
    {
        if (_dict is null) return;
        foreach (var key in _dict.Keys.ToList())
            _dict.Remove(key);
        _isDirty = true;
    }

    /// <summary>Remove every non-predefined (custom) entry. Predefined keys (Title, Author, …) stay.</summary>
    public void ClearCustomData()
    {
        if (_dict is null) return;
        foreach (var key in _dict.Keys.ToList())
        {
            if (!IsPredefinedKey(key))
                _dict.Remove(key);
        }
        _isDirty = true;
    }

    /// <summary>Remove the entry at <paramref name="key"/>.</summary>
    public void Remove(string key)
    {
        if (key is null || _dict is null) return;
        if (_dict.Remove(key)) _isDirty = true;
    }

    /// <summary>
    /// Set a custom metadata property.
    /// </summary>
    public void SetCustom(string key, string value) => SetString(key, value);

    /// <summary>
    /// Adds a custom metadata property. Alias for <see cref="SetCustom"/>;
    /// matches the Aspose.Pdf DocumentInfo.Add(string, string) public surface.
    /// </summary>
    public void Add(string key, string value) => SetString(key, value);

    /// <summary>
    /// Whether the underlying /Info dictionary has an entry for the given key.
    /// </summary>
    public bool ContainsKey(string key) => _dict?.ContainsKey(key) ?? false;

    /// <summary>
    /// Get a custom metadata property.
    /// </summary>
    public string? GetCustom(string key) => GetString(key);

    /// <summary>
    /// Indexer access for any property — well-known ones (Title, Author, etc.)
    /// or custom keys. Matches the Aspose.Pdf DocumentInfo.this[string] surface.
    /// </summary>
    public string? this[string key]
    {
        get => GetString(key);
        set
        {
            if (value is null)
                _dict?.Remove(key);
            else
                SetString(key, value);
        }
    }

    internal void ClearDirty() => _isDirty = false;

    /// <summary>Raw string view of the entry at <paramref name="key"/> — used by
    /// PdfFileInfo to expose CreationDate / ModDate in PDF native format
    /// (<c>D:YYYYMMDDHHmmSSOHH'mm'</c>) rather than re-formatted from the parsed
    /// DateTime, which loses the original timezone offset and digit grouping.</summary>
    internal string? GetRawString(string key) => GetString(key);

    /// <summary>Set the raw string entry at <paramref name="key"/> verbatim.</summary>
    internal void SetRawString(string key, string? value) => SetString(key, value);

    /// <summary>Lazily bind to the document's existing /Info dictionary for read access,
    /// without creating one. Needed because <c>new DocumentInfo(doc)</c> starts with a null
    /// dict; without this, getters would always report empty metadata even when the file
    /// has an Info dict.</summary>
    private void EnsureDictForRead()
    {
        if (_dict is null && _document is not null)
            _dict = _document.ResolveExistingInfoDict();
    }

    private string? GetString(string key)
    {
        EnsureDictForRead();
        var obj = _dict?.Get(key);
        return obj switch
        {
            PdfString s => s.ToText(),
            PdfName n => n.Value,
            // Custom /Info entries are commonly integers or booleans (e.g.
            // viewer-preference shadows like Duplex=2, NumberOfPrintingPasses).
            // The string indexer is contract-typed string?, so surface a
            // culture-invariant string view rather than null.
            PdfInteger i => i.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            PdfReal r => r.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            PdfBoolean b => b.Value ? "true" : "false",
            _ => null,
        };
    }

    private string? GetName(string key)
    {
        EnsureDictForRead();
        var obj = _dict?.Get(key);
        return obj is PdfName n ? n.Value : null;
    }

    private void SetString(string key, string? value)
    {
        EnsureDict();
        if (_dict is null) return;

        if (value is null)
            _dict.Remove(key);
        else
            _dict.Set(key, new PdfString(Encoding.Latin1.GetBytes(value)));
        FlushDirty();
    }

    private void SetName(string key, string? value)
    {
        EnsureDict();
        if (_dict is null) return;

        if (value is null)
            _dict.Remove(key);
        else
            _dict.Set(key, new PdfName(value));
        FlushDirty();
    }

    private void SetDate(string key, DateTime? value)
    {
        EnsureDict();
        if (_dict is null) return;

        if (value is null)
        {
            _dict.Remove(key);
        }
        else
        {
            // Carry a timezone offset already set via the CreationTimeZone /
            // ModTimeZone setter so assigning the date *after* the offset still
            // emits it (the offset setters also re-encode when the date is set first).
            var tz = key switch
            {
                "CreationDate" => _creationTimeZoneOverride,
                "ModDate" => _modTimeZoneOverride,
                _ => null,
            };
            _dict.Set(key, new PdfString(Encoding.Latin1.GetBytes(FormatPdfDate(value.Value, tz))));
        }
        FlushDirty();
    }

    /// <summary>Format a <see cref="DateTime"/> as a PDF date string
    /// (<c>D:YYYYMMDDHHmmSS</c>) with the timezone suffix — <c>Z</c> for a zero /
    /// absent offset, otherwise <c>OHH'mm'</c> per PDF 32000-2 § 7.9.4.</summary>
    private static string FormatPdfDate(DateTime d, TimeSpan? tz)
    {
        var body = $"D:{d.Year:D4}{d.Month:D2}{d.Day:D2}{d.Hour:D2}{d.Minute:D2}{d.Second:D2}";
        if (tz is null || tz.Value == TimeSpan.Zero)
            return body + "Z";
        var t = tz.Value;
        var sign = t.Ticks < 0 ? '-' : '+';
        return $"{body}{sign}{Math.Abs(t.Hours):D2}'{Math.Abs(t.Minutes):D2}'";
    }

    /// <summary>The standard document-information text entries (§ 14.3.3) seeded as empty
    /// strings when a from-scratch document's /Info dict is first created.</summary>
    private static readonly string[] StandardTextEntries =
        { "Title", "Author", "Subject", "Keywords", "Creator", "Producer" };

    private void EnsureDict()
    {
        if (_dict is null && _document is not null)
        {
            var (dict, _) = _document.EnsureInfoDict();
            _dict = dict;

            // A from-scratch document's /Info, the first time it is materialised, carries
            // the standard text entries as empty strings — so a field left unset (the test
            // sets only Title, say) reads back as "" rather than absent after the document
            // is saved and reopened, matching Aspose.Pdf. The actual setter that
            // triggered this overwrites its own key right after. Loaded documents are left
            // untouched: an absent entry on an externally-authored file still reads as null.
            if (_document.IsNewDocument && dict.Count == 0)
            {
                foreach (var key in StandardTextEntries)
                    dict.Set(key, new PdfString(Array.Empty<byte>()));
            }
        }
    }

    private void FlushDirty()
    {
        // Immediately flush — mirrors the TS behavior where isDirty resets after set
        _isDirty = false;
    }

    /// <summary>
    /// Parses the timezone offset from a PDF date string such as
    /// <c>D:20210307123456-07'00'</c>. Returns <see cref="TimeSpan.Zero"/>
    /// for <c>Z</c> or absent / unparseable offset.
    /// </summary>
    internal static TimeSpan ParseTimeZone(string? dateStr)
    {
        if (string.IsNullOrEmpty(dateStr)) return TimeSpan.Zero;
        var s = dateStr;
        if (s.StartsWith("D:", StringComparison.Ordinal)) s = s[2..];
        s = s.TrimEnd('\x00', '\x01', '\'');

        if (s.Length <= 14) return TimeSpan.Zero;
        var tz = s[14..];
        if (tz.Length == 0 || tz[0] == 'Z') return TimeSpan.Zero;

        var sign = tz[0] == '-' ? -1 : 1;
        if (tz[0] != '+' && tz[0] != '-') return TimeSpan.Zero;
        try
        {
            var hh = tz.Length >= 3 ? int.Parse(tz[1..3]) : 0;
            var mmIdx = tz.IndexOf('\'');
            int mm = 0;
            if (tz.Length >= 5)
            {
                var mmStart = mmIdx > 0 && mmIdx + 1 < tz.Length ? mmIdx + 1 : 3;
                if (mmStart + 2 <= tz.Length)
                    mm = int.Parse(tz.Substring(mmStart, 2));
            }
            return new TimeSpan(sign * hh, sign * mm, 0);
        }
        catch
        {
            return TimeSpan.Zero;
        }
    }

    internal static DateTime? ParseDate(string? dateStr)
    {
        if (string.IsNullOrEmpty(dateStr)) return null;

        // PDF date format: D:YYYYMMDDHHmmSSOHH'mm'
        var s = dateStr;
        if (s.StartsWith("D:", StringComparison.Ordinal))
            s = s[2..];

        // Remove any control characters
        s = s.TrimEnd('\x00', '\x01').Trim();

        if (s.Length < 4) return null;

        // PDF native numeric format — the leading four characters are the year.
        // Only attempt it when they actually are digits, so a human-readable date
        // like "21/02/2006" falls through to the lenient parse below instead of
        // throwing on int.Parse("21/0").
        if (StartsWithDigits(s, 4))
        {
            try
            {
                var year = int.Parse(s[..4]);
                var month = s.Length >= 6 ? int.Parse(s[4..6]) : 1;
                var day = s.Length >= 8 ? int.Parse(s[6..8]) : 1;
                var hour = s.Length >= 10 ? int.Parse(s[8..10]) : 0;
                var minute = s.Length >= 12 ? int.Parse(s[10..12]) : 0;
                var second = s.Length >= 14 ? int.Parse(s[12..14]) : 0;

                return new DateTime(year, month, day, hour, minute, second, DateTimeKind.Utc);
            }
            catch
            {
                // fall through to the lenient human-readable parse
            }
        }

        // Fallback for producers that write human-readable dates in the /Info
        // dictionary (e.g. "21/02/2006" or "21/02/2006 5:22:55 PM"). Try
        // day-first formats before month-first, then a generic invariant parse.
        var human = ParseHumanDate(s);
        if (human is not null) return human;

        return null;
    }

    private static bool StartsWithDigits(string s, int n)
    {
        if (s.Length < n) return false;
        for (var i = 0; i < n; i++)
            if (!char.IsDigit(s[i])) return false;
        return true;
    }

    private static readonly string[] HumanDateFormats =
    {
        "dd/MM/yyyy h:mm:ss tt", "dd/MM/yyyy hh:mm:ss tt", "dd/MM/yyyy HH:mm:ss",
        "dd/MM/yyyy H:mm:ss", "dd/MM/yyyy", "d/M/yyyy h:mm:ss tt", "d/M/yyyy",
        "MM/dd/yyyy h:mm:ss tt", "MM/dd/yyyy hh:mm:ss tt", "MM/dd/yyyy HH:mm:ss", "MM/dd/yyyy",
        "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd",
    };

    private static DateTime? ParseHumanDate(string s)
    {
        if (System.DateTime.TryParseExact(s, HumanDateFormats,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var dt))
            return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
        if (System.DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var any))
            return DateTime.SpecifyKind(any, DateTimeKind.Utc);
        return null;
    }
}
