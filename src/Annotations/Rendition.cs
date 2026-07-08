using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Annotations;

/// <summary>
/// A rendition object (PDF §13.2.3) — describes a media object to be played by a
/// rendition action. The base class exposes the members common to media and
/// selector renditions.
/// </summary>
public abstract class Rendition
{
    internal PdfDictionary Dict { get; }
    internal PdfReader? Reader { get; }

    internal Rendition(PdfDictionary dict, PdfReader? reader)
    {
        Dict = dict;
        Reader = reader;
    }

    /// <summary>The rendition name (/N entry) — used for viewer UI lists.</summary>
    public string? Name
    {
        get => ((Reader?.Resolve(Dict.Get("N")) ?? Dict.Get("N")) as PdfString)?.ToText();
        set
        {
            if (value is null) Dict.Remove("N");
            else Dict.Set("N", new PdfString(System.Text.Encoding.UTF8.GetBytes(value)));
        }
    }

    /// <summary>Wrap a rendition dictionary in its typed object (/S: MR = media, SR = selector).</summary>
    internal static Rendition? Create(PdfDictionary? dict, PdfReader? reader)
    {
        if (dict is null) return null;
        return dict.GetName("S") switch
        {
            "SR" => new SelectorRendition(dict, reader),
            _ => new MediaRendition(dict, reader),
        };
    }
}

/// <summary>A media rendition (PDF §13.2.3.2) — pairs a media clip with playback parameters.</summary>
public sealed class MediaRendition : Rendition
{
    internal MediaRendition(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }

    /// <summary>Create a media rendition playing <paramref name="mediaClip"/>.</summary>
    public MediaRendition(MediaClip mediaClip) : base(new PdfDictionary(), null)
    {
        Dict.Set("Type", new PdfName("Rendition"));
        Dict.Set("S", new PdfName("MR"));
        Dict.Set("C", mediaClip.Dict);
    }

    /// <summary>The media clip this rendition plays (/C entry).</summary>
    public MediaClip? MediaClip
        => Annotations.MediaClip.Create(Reader?.ResolveDict(Dict.Get("C")) ?? Dict.Get("C") as PdfDictionary, Reader);
}

/// <summary>A selector rendition (PDF §13.2.3.3) — chooses among alternative renditions.</summary>
public sealed class SelectorRendition : Rendition
{
    internal SelectorRendition(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
}

/// <summary>A media clip object (PDF §13.2.4) — the actual media data or section thereof.</summary>
public abstract class MediaClip
{
    internal PdfDictionary Dict { get; }
    internal PdfReader? Reader { get; }

    internal MediaClip(PdfDictionary dict, PdfReader? reader)
    {
        Dict = dict;
        Reader = reader;
    }

    /// <summary>Wrap a media-clip dictionary in its typed object (/S: MCD = data, MCS = section).</summary>
    internal static MediaClip? Create(PdfDictionary? dict, PdfReader? reader)
    {
        if (dict is null) return null;
        return dict.GetName("S") switch
        {
            "MCS" => new MediaClipSection(dict, reader),
            _ => new MediaClipData(dict, reader),
        };
    }
}

/// <summary>A media clip data object (PDF §13.2.4.2) — full media data via a file specification.</summary>
public sealed class MediaClipData : MediaClip
{
    internal MediaClipData(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }

    /// <summary>Create a media clip data object embedding <paramref name="mediaFile"/>.</summary>
    public MediaClipData(string mediaFile) : base(new PdfDictionary(), null)
    {
        Dict.Set("Type", new PdfName("MediaClip"));
        Dict.Set("S", new PdfName("MCD"));
        var spec = new FileSpecification(mediaFile);
        spec.MaterializeEmbeddedStream();
        Dict.Set("D", spec.Dict);
        Dict.Set("CT", new PdfString(System.Text.Encoding.ASCII.GetBytes(GuessContentType(mediaFile))));
        // Playback permission: allow the viewer to write the media to a temporary file,
        // required by most players for embedded content.
        var p = new PdfDictionary();
        p.Set("TF", new PdfString(System.Text.Encoding.ASCII.GetBytes("TEMPACCESS")));
        Dict.Set("P", p);
    }

    /// <summary>The file specification holding the media data (/D entry).</summary>
    public FileSpecification? Data
    {
        get
        {
            var d = Reader?.ResolveDict(Dict.Get("D")) ?? Dict.Get("D") as PdfDictionary;
            return d is null ? null : new FileSpecification(d, Reader);
        }
    }

    /// <summary>The clip's MIME type (/CT entry).</summary>
    public string? ContentType
        => ((Reader?.Resolve(Dict.Get("CT")) ?? Dict.Get("CT")) as PdfString)?.ToText();

    private static string GuessContentType(string file)
        => System.IO.Path.GetExtension(file).ToLowerInvariant() switch
        {
            ".swf" => "application/x-shockwave-flash",
            ".mp4" => "video/mp4",
            ".mp3" => "audio/mpeg",
            ".wav" => "audio/wav",
            ".avi" => "video/avi",
            ".mov" => "video/quicktime",
            _ => "application/octet-stream",
        };
}

/// <summary>A media clip section object (PDF §13.2.4.3) — a temporal section of another clip.</summary>
public sealed class MediaClipSection : MediaClip
{
    internal MediaClipSection(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
}

/// <summary>
/// A rendition action (PDF §12.6.4.14) — controls the playing of multimedia content.
/// </summary>
public sealed class RenditionAction : PdfAction
{
    internal RenditionAction(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }

    /// <summary>Create a rendition action playing <paramref name="rendition"/>.</summary>
    public RenditionAction(Rendition rendition) : base(NewDict(rendition)) { }

    private static PdfDictionary NewDict(Rendition rendition)
    {
        var dict = new PdfDictionary();
        dict.Set("Type", new PdfName("Action"));
        dict.Set("S", new PdfName("Rendition"));
        dict.Set("R", rendition.Dict);
        dict.Set("OP", new PdfInteger(0)); // play
        return dict;
    }

    /// <summary>The rendition to play (/R entry).</summary>
    public Rendition? Rendition
        => Annotations.Rendition.Create(
            Reader?.ResolveDict(Dict.Get("R")) ?? Dict.Get("R") as PdfDictionary, Reader);

    /// <summary>The operation to perform (/OP entry): 0 = play, 1 = stop, 2 = pause, 3 = resume, 4 = play-from-start.</summary>
    public int RenditionOperation
    {
        get => (int)Dict.GetInt("OP", 0);
        set => Dict.Set("OP", new PdfInteger(value));
    }
}
