using Aspose.Pdf.Core;
using Aspose.Pdf.Functions;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Annotations;

/// <summary>Caret-symbol style for <see cref="CaretAnnotation"/>.</summary>
public enum CaretSymbol
{
    /// <summary>No symbol.</summary>
    None = 0,
    /// <summary>Pilcrow / paragraph-mark symbol.</summary>
    Paragraph = 1,
}

public partial class CaretAnnotation : MarkupAnnotation
{
    internal CaretAnnotation(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }

    public CaretAnnotation(Document document) : base(document, new Rectangle(0, 0, 0, 0))
    {
        Dict.Set("Subtype", new PdfName("Caret"));
    }

    public CaretAnnotation(Page page, Rectangle rect) : base(page, rect)
    {
        Dict.Set("Subtype", new PdfName("Caret"));
    }

    public new AnnotationType AnnotationType => AnnotationType.Caret;

    /// <summary>The caret rectangle inset by the /RD entry; equals
    /// <see cref="Annotation.Rect"/> when /RD is absent.</summary>
    public Rectangle Frame
    {
        get
        {
            var r = Rect ?? new Rectangle(0, 0, 0, 0);
            var rd = InternalReader.Resolve(Dict.Get("RD")) as PdfArray;
            if (rd is null || rd.Count < 4) return new Rectangle(r.LLX, r.LLY, r.URX, r.URY);
            double left = G(rd[0]), top = G(rd[1]), right = G(rd[2]), bottom = G(rd[3]);
            return new Rectangle(r.LLX + left, r.LLY + bottom, r.URX - right, r.URY - top);
        }
        set
        {
            var r = Rect;
            if (value is null || r is null) { Dict.Remove("RD"); return; }
            var rd = new PdfArray();
            rd.Add(new PdfReal(value.LLX - r.LLX));
            rd.Add(new PdfReal(r.URY - value.URY));
            rd.Add(new PdfReal(r.URX - value.URX));
            rd.Add(new PdfReal(value.LLY - r.LLY));
            Dict.Set("RD", rd);
        }
    }

    /// <summary>Caret symbol style (/Sy entry).</summary>
    public CaretSymbol Symbol
    {
        get => Dict.GetName("Sy") == "P" ? CaretSymbol.Paragraph : CaretSymbol.None;
        set
        {
            if (value == CaretSymbol.Paragraph) Dict.Set("Sy", new PdfName("P"));
            else Dict.Remove("Sy");
        }
    }

    private static double G(PdfObject o) => o is PdfReal r ? r.Value : o is PdfInteger i ? i.Value : 0;
}

public partial class SoundAnnotation : MarkupAnnotation
{
    internal SoundAnnotation(PdfDictionary dict, PdfReader reader) : base(dict, reader)
    {
        _soundData = ParseSoundData(dict, reader);
        Icon = ParseIcon(dict);
    }

    public SoundAnnotation(Page page, Rectangle rect, string soundFile) : base(page, rect)
    {
        Dict.Set("Type", new PdfName("Annot"));
        Dict.Set("Subtype", new PdfName("Sound"));
        WriteIcon(SoundIcon.Speaker);
        _soundData = LoadAudioBytes(soundFile);
        AttachSoundStream(_soundData);
    }

    public SoundAnnotation(Page page, Rectangle rect, string soundFile, SoundSampleData soundSampleData)
        : base(page, rect)
    {
        Dict.Set("Type", new PdfName("Annot"));
        Dict.Set("Subtype", new PdfName("Sound"));
        WriteIcon(SoundIcon.Speaker);
        _soundData = LoadAudioBytes(soundFile);
        if (soundSampleData != null)
        {
            _soundData.Rate = (int)soundSampleData.SamplingRate;
            _soundData.Channels = soundSampleData.NumberOfSoundChannels;
            _soundData.Bits = soundSampleData.BitsPerChannel;
            _soundData.Encoding = soundSampleData.EncodingFormat switch
            {
                SoundSampleDataEncodingFormat.ALaw => SoundEncoding.ALaw,
                SoundSampleDataEncodingFormat.muLaw => SoundEncoding.MuLaw,
                SoundSampleDataEncodingFormat.Signed => SoundEncoding.Signed,
                _ => SoundEncoding.Raw,
            };
        }
        AttachSoundStream(_soundData);
    }

    public new AnnotationType AnnotationType => AnnotationType.Sound;

    private SoundIcon _icon = SoundIcon.Speaker;
    public SoundIcon Icon
    {
        get => _icon;
        set
        {
            _icon = value;
            WriteIcon(value);
        }
    }

    private SoundData _soundData;
    public SoundData SoundData => _soundData;

    // ── Wire to /Sound stream (PDF 32000-1 §12.5.6.16, Table 175) ──

    private void WriteIcon(SoundIcon icon)
    {
        // /Name entry: "Speaker" (default) or "Mic" — Adobe-defined values.
        Dict.Set("Name", new PdfName(icon switch
        {
            SoundIcon.Mic => "Mic",
            _ => "Speaker",
        }));
    }

    private static SoundIcon ParseIcon(PdfDictionary dict)
    {
        var name = dict.GetName("Name");
        return name == "Mic" ? SoundIcon.Mic : SoundIcon.Speaker;
    }

    /// <summary>Build the /Sound stream from <paramref name="data"/> and
    /// attach it to this annotation's dictionary. The stream carries the
    /// audio bytes verbatim plus R/B/C/E sampling parameters per
    /// PDF 32000-1 Table 175. Round-tripped on save → load.</summary>
    private void AttachSoundStream(SoundData data)
    {
        var soundDict = new PdfDictionary();
        soundDict.Set("Type", new PdfName("Sound"));
        soundDict.Set("R", new PdfReal(data.Rate));
        soundDict.Set("C", new PdfInteger(data.Channels));
        soundDict.Set("B", new PdfInteger(data.Bits));
        soundDict.Set("E", new PdfName(SoundEncodingToPdfName(data.Encoding)));
        var bytes = ReadAllBytes(data.Contents);
        var stream = new PdfStream(soundDict, bytes);
        Dict.Set("Sound", stream);
    }

    private SoundData ParseSoundData(PdfDictionary dict, PdfReader reader)
    {
        var sd = new SoundData();
        var soundObj = reader.Resolve(dict.Get("Sound"));
        if (soundObj is not PdfStream stream) return sd;
        var sDict = stream.Dict;
        sd.Rate = (int)(reader.Resolve(sDict.Get("R")) switch
        {
            PdfReal r => r.Value,
            PdfInteger n => n.Value,
            _ => 11025.0,
        });
        sd.Channels = sDict.Get("C") is PdfInteger c ? (int)c.Value : 1;
        sd.Bits = sDict.Get("B") is PdfInteger b ? (int)b.Value : 8;
        sd.Encoding = sDict.GetName("E") switch
        {
            "Raw" => SoundEncoding.Raw,
            "Signed" => SoundEncoding.Signed,
            "muLaw" => SoundEncoding.MuLaw,
            "ALaw" => SoundEncoding.ALaw,
            _ => SoundEncoding.Raw,
        };
        sd.SetContents(reader.DecodeStream(stream));
        return sd;
    }

    private static string SoundEncodingToPdfName(SoundEncoding enc) => enc switch
    {
        SoundEncoding.Signed => "Signed",
        SoundEncoding.MuLaw => "muLaw",
        SoundEncoding.ALaw => "ALaw",
        _ => "Raw",
    };

    private static SoundData LoadAudioBytes(string soundFile)
    {
        var sd = new SoundData();
        if (!string.IsNullOrEmpty(soundFile) && System.IO.File.Exists(soundFile))
        {
            try
            {
                sd.SetContents(System.IO.File.ReadAllBytes(soundFile));
            }
            catch { }
        }
        return sd;
    }

    private static byte[] ReadAllBytes(System.IO.Stream s)
    {
        if (s is null) return System.Array.Empty<byte>();
        using var ms = new System.IO.MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>Sound annotations have no typed Visit overload on
    /// <see cref="AnnotationSelector"/>, so Accept here is a no-op kept for reflection
    /// parity.</summary>
    public override void Accept(AnnotationSelector visitor) { _ = visitor; }
}

public partial class MovieAnnotation : Annotation
{
    internal MovieAnnotation(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }

    public MovieAnnotation(Document document, string movieFile) : base(document, new Rectangle(0, 0, 0, 0))
    {
        Dict.Set("Type", new PdfName("Annot"));
        Dict.Set("Subtype", new PdfName("Movie"));
        File = MakeMovieFileSpec(movieFile);
    }

    public MovieAnnotation(Page page, Rectangle rect, string movieFile) : base(page, rect)
    {
        Dict.Set("Type", new PdfName("Annot"));
        Dict.Set("Subtype", new PdfName("Movie"));
        File = MakeMovieFileSpec(movieFile);
    }

    public new AnnotationType AnnotationType => AnnotationType.Movie;

    // Movie state lives in the required /Movie sub-dictionary (PDF 32000-1 §12.5.6.17,
    // Table 8.35) so it round-trips through save/open. The title is the standard
    // annotation /T entry (handled by the base Annotation.Title). Created lazily.
    private PdfDictionary MovieDict()
    {
        if (InternalReader.ResolveDict(Dict.Get("Movie")) is { } existing) return existing;
        var d = new PdfDictionary();
        Dict.Set("Movie", d);
        return d;
    }

    // A movie is *referenced* by path, not embedded like a file attachment, so build a
    // bare /Filespec preserving the full path verbatim in both /F and /UF.
    private static FileSpecification MakeMovieFileSpec(string movieFile)
    {
        var d = new PdfDictionary();
        d.Set("Type", new PdfName("Filespec"));
        d.Set("F", new PdfString(System.Text.Encoding.Latin1.GetBytes(movieFile ?? "")));
        d.Set("UF", Forms.Field.EncodePdfTextString(movieFile ?? ""));
        return FileSpecification.FromExistingDict(d, null);
    }

    private static double ToDouble(PdfObject? o) => o switch
    {
        PdfInteger i => i.Value,
        PdfReal r => r.Value,
        _ => 0,
    };

    /// <summary>Display aspect ratio (width × height in points) — the /Aspect entry.</summary>
    public Aspose.Pdf.Point? Aspect
    {
        get
        {
            if (InternalReader.ResolveDict(Dict.Get("Movie")) is { } m &&
                InternalReader.Resolve(m.Get("Aspect")) is PdfArray a && a.Count >= 2)
                return new Aspose.Pdf.Point(ToDouble(InternalReader.Resolve(a[0])),
                                            ToDouble(InternalReader.Resolve(a[1])));
            return null;
        }
        set
        {
            var m = MovieDict();
            if (value is null) { m.Remove("Aspect"); return; }
            m.Set("Aspect", new PdfArray([new PdfReal(value.X), new PdfReal(value.Y)]));
        }
    }

    /// <summary>The /F (file specification) entry of the /Movie dict — the movie data.</summary>
    public FileSpecification? File
    {
        get
        {
            if (InternalReader.ResolveDict(Dict.Get("Movie")) is { } m &&
                InternalReader.ResolveDict(m.Get("F")) is { } f)
                return FileSpecification.FromExistingDict(f, InternalReader);
            return null;
        }
        set
        {
            var m = MovieDict();
            if (value is null) { m.Remove("F"); return; }
            m.Set("F", value.Dict);
        }
    }

    /// <summary>True when the annotation should display a poster image — the /Poster entry.</summary>
    public bool Poster
    {
        get => InternalReader.ResolveDict(Dict.Get("Movie")) is { } m && m.GetBool("Poster");
        set => MovieDict().Set("Poster", value ? PdfBoolean.True : PdfBoolean.False);
    }

    /// <summary>Rotation in degrees applied to the movie's playback area — the /Rotate entry.</summary>
    public int Rotate
    {
        get => InternalReader.ResolveDict(Dict.Get("Movie")) is { } m ? (int)m.GetInt("Rotate") : 0;
        set => MovieDict().Set("Rotate", new PdfInteger(value));
    }
}

public partial class ScreenAnnotation : Annotation
{
    internal ScreenAnnotation(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }

    /// <summary>Create a screen annotation referencing <paramref name="mediaFile"/>.</summary>
    public ScreenAnnotation(Page page, Rectangle rect, string mediaFile) : base(page, rect)
    {
        Dict.Set("Type", new PdfName("Annot"));
        Dict.Set("Subtype", new PdfName("Screen"));
        if (!string.IsNullOrEmpty(mediaFile))
        {
            // The activation action: a rendition action playing a media rendition whose
            // clip embeds the media file (PDF §12.6.4.14 / §13.2). The embedded stream
            // rides in the clip's file specification.
            var action = new RenditionAction(new MediaRendition(new MediaClipData(mediaFile)));
            Dict.Set("A", action.Dict);
        }
    }

    /// <summary>Always <see cref="AnnotationType.Screen"/>.</summary>
    public new AnnotationType AnnotationType => AnnotationType.Screen;

    /// <summary>Render-launch action (/A entry), or null when none is set.</summary>
    public new PdfAction? Action
    {
        get
        {
            var aDict = InternalReader.ResolveDict(Dict.Get("A"));
            return aDict is null ? null : PdfAction.Create(aDict, InternalReader);
        }
    }

    /// <summary>Title/label shown in the viewer chrome (/T entry).</summary>
    public new string? Title
    {
        get => (InternalReader.Resolve(Dict.Get("T")) as PdfString)?.ToText();
        set
        {
            if (value is null) Dict.Remove("T");
            else Dict.Set("T", new PdfString(System.Text.Encoding.UTF8.GetBytes(value)));
        }
    }
}

public partial class RichMediaAnnotation : Annotation
{
    internal RichMediaAnnotation(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }

    public RichMediaAnnotation(Page page, Rectangle rect) : base(page, rect)
    {
        Dict.Set("Type", new PdfName("Annot"));
        Dict.Set("Subtype", new PdfName("RichMedia"));
    }

    public new AnnotationType AnnotationType => AnnotationType.RichMedia;

    private ContentType? _type;
    private ActivationEvent? _activateOn;

    /// <summary>Media kind. For a loaded annotation this is read from the first
    /// /RichMediaContent configuration's /Subtype (/Video, /Sound).</summary>
    public ContentType Type
    {
        get
        {
            if (_type is not null) return _type.Value;
            return FirstConfiguration()?.GetName("Subtype") switch
            {
                "Video" => ContentType.Video,
                "Sound" or "Audio" => ContentType.Audio,
                _ => ContentType.Unknown,
            };
        }
        set => _type = value;
    }

    /// <summary>Activation trigger. For a loaded annotation this is read from
    /// /RichMediaSettings/Activation/Condition (/XA click, /PO page-open, /PV page-visible).</summary>
    public ActivationEvent ActivateOn
    {
        get
        {
            if (_activateOn is not null) return _activateOn.Value;
            var settings = Resolve(Dict.Get("RichMediaSettings")) as PdfDictionary;
            var activation = settings is null ? null : Resolve(settings.Get("Activation")) as PdfDictionary;
            return activation?.GetName("Condition") switch
            {
                "PO" => ActivationEvent.PageOpen,
                "PV" => ActivationEvent.PageVisible,
                _ => ActivationEvent.Click,
            };
        }
        set => _activateOn = value;
    }

    public string? CustomFlashVariables { get; set; }

    private byte[]? _content;
    private string? _contentName;
    private byte[]? _customPlayer;
    private byte[]? _poster;
    private readonly Dictionary<string, byte[]> _customData = new();

    /// <summary>The main media asset. For a loaded annotation the bytes come from the
    /// instance's /Asset embedded file (falling back to the first named asset).</summary>
    public System.IO.Stream? Content
    {
        get
        {
            if (_content is not null) return new System.IO.MemoryStream(_content, writable: false);
            var data = ReadContentAssetBytes();
            return data is null ? null : new System.IO.MemoryStream(data, writable: false);
        }
    }

    private PdfObject? Resolve(PdfObject? obj) =>
        InternalReader is null ? obj : InternalReader.Resolve(obj);

    private PdfDictionary? FirstConfiguration()
    {
        var content = Resolve(Dict.Get("RichMediaContent")) as PdfDictionary;
        var configs = content is null ? null : Resolve(content.Get("Configurations")) as PdfArray;
        return configs is { Count: > 0 } ? Resolve(configs[0]) as PdfDictionary : null;
    }

    private byte[]? ReadContentAssetBytes()
    {
        var config = FirstConfiguration();
        var instances = config is null ? null : Resolve(config.Get("Instances")) as PdfArray;
        var instance = instances is { Count: > 0 } ? Resolve(instances[0]) as PdfDictionary : null;
        var asset = instance is null ? null : Resolve(instance.Get("Asset")) as PdfDictionary;
        if (asset is null)
        {
            // No instance asset: fall back to the first entry of the /Assets name tree.
            var content = Resolve(Dict.Get("RichMediaContent")) as PdfDictionary;
            var assets = content is null ? null : Resolve(content.Get("Assets")) as PdfDictionary;
            var names = assets is null ? null : Resolve(assets.Get("Names")) as PdfArray;
            if (names is { Count: > 1 })
                asset = Resolve(names[1]) as PdfDictionary;
        }
        if (asset is null) return null;
        var ef = Resolve(asset.Get("EF")) as PdfDictionary;
        var stream = ef is null ? null : Resolve(ef.Get("F")) as PdfStream;
        if (stream is null) return null;
        return InternalReader is not null ? InternalReader.DecodeStream(stream) : stream.RawData;
    }

    public System.IO.Stream? CustomPlayer
    {
        get => _customPlayer is null ? null : new System.IO.MemoryStream(_customPlayer, writable: false);
        set
        {
            if (value is null) { _customPlayer = null; return; }
            using var ms = new System.IO.MemoryStream();
            value.CopyTo(ms);
            _customPlayer = ms.ToArray();
        }
    }

    public void SetContent(string fileName, System.IO.Stream audio)
    {
        if (audio is null) throw new ArgumentNullException(nameof(audio));
        using var ms = new System.IO.MemoryStream();
        audio.CopyTo(ms);
        _content = ms.ToArray();
        _contentName = string.IsNullOrEmpty(fileName) ? "Content" : fileName;
        var ext = System.IO.Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
        Type = ext switch
        {
            ".mp3" or ".wav" or ".m4a" or ".ogg" or ".aac" => ContentType.Audio,
            ".mp4" or ".mov" or ".avi" or ".webm" or ".mkv" => ContentType.Video,
            _ => ContentType.Unknown,
        };
    }

    public void SetPoster(System.IO.Stream imageStream)
    {
        if (imageStream is null) { _poster = null; return; }
        using var ms = new System.IO.MemoryStream();
        imageStream.CopyTo(ms);
        _poster = ms.ToArray();
    }

    public void AddCustomData(string name, System.IO.Stream data)
    {
        if (name is null) throw new ArgumentNullException(nameof(name));
        if (data is null) throw new ArgumentNullException(nameof(data));
        using var ms = new System.IO.MemoryStream();
        data.CopyTo(ms);
        _customData[name] = ms.ToArray();
    }

    /// <summary>Serialise the stored media buffers into the annotation dictionary:
    /// /RichMediaContent (assets name tree + configuration/instance) and
    /// /RichMediaSettings (activation), so the media survives save → reload.</summary>
    public void Update()
    {
        var assets = new List<(string Name, PdfDictionary Spec)>();

        static PdfDictionary MakeSpec(string name, byte[] data)
        {
            var efStreamDict = new PdfDictionary();
            efStreamDict.Set("Type", new PdfName("EmbeddedFile"));
            var paramsDict = new PdfDictionary();
            paramsDict.Set("Size", new PdfInteger(data.Length));
            efStreamDict.Set("Params", paramsDict);
            var efDict = new PdfDictionary();
            efDict.Set("F", new PdfStream(efStreamDict, data));
            var spec = new PdfDictionary();
            spec.Set("Type", new PdfName("Filespec"));
            spec.Set("F", new PdfString(System.Text.Encoding.Latin1.GetBytes(name)));
            spec.Set("UF", Forms.Field.EncodePdfTextString(name));
            spec.Set("EF", efDict);
            return spec;
        }

        PdfDictionary? contentSpec = null;
        if (_content is not null)
        {
            contentSpec = MakeSpec(_contentName ?? "Content", _content);
            assets.Add((_contentName ?? "Content", contentSpec));
        }
        if (_customPlayer is not null)
            assets.Add(("CustomPlayer.swf", MakeSpec("CustomPlayer.swf", _customPlayer)));
        if (_poster is not null)
            assets.Add(("Poster", MakeSpec("Poster", _poster)));
        foreach (var kv in _customData)
            assets.Add((kv.Key, MakeSpec(kv.Key, kv.Value)));

        if (assets.Count > 0)
        {
            var subtype = Type switch
            {
                ContentType.Video => "Video",
                ContentType.Audio => "Sound",
                _ => "Flash",
            };

            // /Assets is a name tree: keys must be lexically sorted.
            assets.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            var names = new PdfArray();
            foreach (var (name, spec) in assets)
            {
                names.Add(new PdfString(System.Text.Encoding.Latin1.GetBytes(name)));
                names.Add(spec);
            }
            var assetsDict = new PdfDictionary();
            assetsDict.Set("Names", names);

            var instance = new PdfDictionary();
            instance.Set("Type", new PdfName("RichMediaInstance"));
            instance.Set("Subtype", new PdfName(subtype));
            if (contentSpec is not null)
                instance.Set("Asset", contentSpec);
            if (!string.IsNullOrEmpty(CustomFlashVariables))
            {
                var flashParams = new PdfDictionary();
                flashParams.Set("Type", new PdfName("RichMediaParams"));
                flashParams.Set("FlashVars", new PdfString(System.Text.Encoding.Latin1.GetBytes(CustomFlashVariables)));
                instance.Set("Params", flashParams);
            }
            var instances = new PdfArray();
            instances.Add(instance);

            var config = new PdfDictionary();
            config.Set("Type", new PdfName("RichMediaConfiguration"));
            config.Set("Subtype", new PdfName(subtype));
            config.Set("Instances", instances);
            var configs = new PdfArray();
            configs.Add(config);

            var content = new PdfDictionary();
            content.Set("Assets", assetsDict);
            content.Set("Configurations", configs);
            Dict.Set("RichMediaContent", content);
        }

        var condition = ActivateOn switch
        {
            ActivationEvent.PageOpen => "PO",
            ActivationEvent.PageVisible => "PV",
            _ => "XA",
        };
        var activation = new PdfDictionary();
        activation.Set("Condition", new PdfName(condition));
        var settings = new PdfDictionary();
        settings.Set("Activation", activation);
        Dict.Set("RichMediaSettings", settings);
    }

    public enum ActivationEvent
    {
        Click = 0,
        PageOpen = 1,
        PageVisible = 2,
    }

    public enum ContentType
    {
        Unknown = 0,
        Audio = 1,
        Video = 2,
    }
}

/// <summary>
/// Fallback annotation class for annotation subtypes that have no dedicated model
/// (e.g. vendor-specific subtypes such as <c>/BatesN</c>). It exposes the common
/// annotation surface (rectangle, colour, appearance, flags) inherited from
/// <see cref="Annotation"/> and round-trips its dictionary unchanged, so an
/// un-modelled annotation survives load → edit → save and stays castable via
/// <c>annot as GenericAnnotation</c>.
/// </summary>
public sealed class GenericAnnotation : Annotation
{
    internal GenericAnnotation(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }
}
