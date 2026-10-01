using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Annotations;

internal sealed partial class ContentRedactor
{
    /// <summary>The resources a content stream draws with, copied the first time a redacted image
    /// or form joins them: the original may be shared with other pages or forms, which must go on
    /// drawing what they drew.</summary>
    private sealed class ResourceCopy(PdfReader reader, PdfDictionary? original)
    {
        private int _added;
        // Whether the copy has its own XObject and Properties dictionaries yet (the originals' may
        // be shared too).
        private bool _xobjectsCopied;
        private bool _propertiesCopied;

        /// <summary>The copy, once one was needed; null while the stream draws with the original.</summary>
        public PdfDictionary? Copy { get; private set; }

        public PdfDictionary? Current => Copy ?? original;

        public PdfDictionary? Dict(string key) => reader.ResolveDict(Current?.Get(key));

        public PdfStream? XObject(string name) => reader.Resolve(Dict("XObject")?.Get(name)) as PdfStream;

        public Dictionary<string, PdfDictionary> Fonts() => Named("Font");

        public Dictionary<string, PdfDictionary> ExtGStates() => Named("ExtGState");

        private Dictionary<string, PdfDictionary> Named(string key)
        {
            var named = new Dictionary<string, PdfDictionary>();
            if (Dict(key) is { } dict)
                foreach (var name in dict.Keys)
                    if (reader.ResolveDict(dict.Get(name)) is { } value)
                        named[name] = value;
            return named;
        }

        /// <summary>Add a redacted image or form under a name no other XObject has; its name.</summary>
        public string AddXObject(PdfStream stream)
        {
            var xobjects = CopiedXObjects();
            string name;
            do name = "Rd" + ++_added; while (xobjects.ContainsKey(name));
            xobjects.Set(name, stream);
            return name;
        }

        public PdfDictionary? Property(string name) => reader.ResolveDict(Dict("Properties")?.Get(name));

        /// <summary>Put <paramref name="properties"/> under <paramref name="name"/> in the copy.</summary>
        public void SetProperty(string name, PdfDictionary properties)
        {
            Copy ??= Shallow(original);
            if (!_propertiesCopied)
            {
                Copy.Set("Properties", Shallow(reader.ResolveDict(Copy.Get("Properties"))));
                _propertiesCopied = true;
            }
            ((PdfDictionary)Copy.Get("Properties")!).Set(name, properties);
        }

        public void RemoveXObject(string name) => CopiedXObjects().Remove(name);

        public void RenameXObject(string from, string to)
        {
            var xobjects = CopiedXObjects();
            xobjects.Set(to, xobjects.Get(from)!);
            xobjects.Remove(from);
        }

        private PdfDictionary CopiedXObjects()
        {
            Copy ??= Shallow(original);
            if (!_xobjectsCopied)
            {
                Copy.Set("XObject", Shallow(reader.ResolveDict(Copy.Get("XObject"))));
                _xobjectsCopied = true;
            }
            return (PdfDictionary)Copy.Get("XObject")!;
        }
    }

    private static PdfDictionary Shallow(PdfDictionary? source)
    {
        var copy = new PdfDictionary();
        if (source is not null)
            foreach (var key in source.Keys)
                copy.Set(key, source.Get(key)!);
        return copy;
    }
}
