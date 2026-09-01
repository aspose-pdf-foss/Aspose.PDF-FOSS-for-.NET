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
/// <summary>
/// Document-level JavaScript scripts (PDF spec §12.6.4.16). Walks the
    /// /Names/JavaScript name tree in the document catalog and exposes
    /// scripts by name.
    /// </summary>
    public sealed class JavaScriptCollection
{
        private readonly Document _doc;
        private List<string>? _keys;
        private Dictionary<string, string>? _scripts;

        internal JavaScriptCollection(Document doc) => _doc = doc;

        /// <summary>Script names in lexical order.</summary>
        public IList<string> Keys
        {
            get
            {
                EnsureLoaded();
                return _keys!;
            }
        }

        /// <summary>
        /// Get or set the JavaScript source for the named script. Setting
        /// null is equivalent to <see cref="Remove"/>.
        /// </summary>
        public string? this[string name]
        {
            get
            {
                EnsureLoaded();
                return _scripts!.TryGetValue(name, out var src) ? src : null;
            }
            set
            {
                if (value is null) { Remove(name); return; }
                EnsureLoaded();
                _scripts![name] = value;
                if (!_keys!.Contains(name))
                {
                    var insertAt = _keys!.Count;
                    for (var i = 0; i < _keys.Count; i++)
                    {
                        if (string.CompareOrdinal(_keys[i], name) > 0) { insertAt = i; break; }
                    }
                    _keys.Insert(insertAt, name);
                }
                WriteBack();
            }
        }

        /// <summary>Remove the named JavaScript entry. Returns true if it existed.</summary>
        public bool Remove(string key)
        {
            EnsureLoaded();
            if (!_scripts!.Remove(key)) return false;
            _keys!.Remove(key);
            WriteBack();
            return true;
        }

        private void EnsureLoaded()
        {
            if (_keys is not null) return;
            _keys = new List<string>();
            _scripts = new Dictionary<string, string>(StringComparer.Ordinal);
            var reader = _doc.Reader;
            var catalog = reader.Catalog;
            var names = reader.ResolveDict(catalog.Get("Names"));
            if (names is null) return;
            var jsTree = reader.ResolveDict(names.Get("JavaScript"));
            if (jsTree is null) return;
            CollectFromNameTree(jsTree, reader);
        }

        private void CollectFromNameTree(Aspose.Pdf.Core.PdfDictionary node, Aspose.Pdf.IO.PdfReader reader)
        {
            // Either /Names array (leaf) or /Kids array (intermediate).
            var namesArr = reader.Resolve(node.Get("Names")) as Aspose.Pdf.Core.PdfArray;
            if (namesArr is not null)
            {
                for (int i = 0; i + 1 < namesArr.Count; i += 2)
                {
                    var key = (reader.Resolve(namesArr[i]) as Aspose.Pdf.Core.PdfString)?.ToText();
                    if (key is null) continue;
                    var actionDict = reader.ResolveDict(namesArr[i + 1]);
                    var jsObj = actionDict is null ? null : reader.Resolve(actionDict.Get("JS"));
                    string? src = jsObj switch
                    {
                        Aspose.Pdf.Core.PdfString s => s.ToText(),
                        Aspose.Pdf.Core.PdfStream st => System.Text.Encoding.UTF8.GetString(reader.DecodeStream(st)),
                        _ => null,
                    };
                    _keys!.Add(key);
                    if (src is not null) _scripts![key] = src;
                }
            }
            var kidsArr = reader.Resolve(node.Get("Kids")) as Aspose.Pdf.Core.PdfArray;
            if (kidsArr is not null)
            {
                foreach (var kid in kidsArr)
                {
                    var kidDict = reader.ResolveDict(kid);
                    if (kidDict is not null) CollectFromNameTree(kidDict, reader);
                }
            }
        }

        private void WriteBack()
        {
            var reader = _doc.Reader;
            var catalog = reader.Catalog;
            var names = reader.ResolveDict(catalog.Get("Names"));
            if (names is null)
            {
                names = new Aspose.Pdf.Core.PdfDictionary();
                catalog.Set("Names", names);
            }
            if (_scripts!.Count == 0)
            {
                names.Remove("JavaScript");
                return;
            }
            // Flat /Names array, lexically ordered (PDF 32000-1 § 7.9.6). Use
            // inline action dicts (rather than indirect refs to newly-allocated
            // objects) so a subsequent in-process EnsureLoaded — which reads
            // through the reader's xref — can still resolve them; new objects
            // aren't visible to PdfReader.Resolve until Save runs.
            var arr = new Aspose.Pdf.Core.PdfArray();
            foreach (var key in _keys!)
            {
                var actionDict = new Aspose.Pdf.Core.PdfDictionary();
                actionDict.Set("Type", new Aspose.Pdf.Core.PdfName("Action"));
                actionDict.Set("S", new Aspose.Pdf.Core.PdfName("JavaScript"));
                actionDict.Set("JS", new Aspose.Pdf.Core.PdfString(
                    System.Text.Encoding.Latin1.GetBytes(_scripts![key])));
                arr.Add(new Aspose.Pdf.Core.PdfString(System.Text.Encoding.Latin1.GetBytes(key)));
                arr.Add(actionDict);
            }
            var jsTree = new Aspose.Pdf.Core.PdfDictionary();
            jsTree.Set("Names", arr);
            names.Set("JavaScript", jsTree);
        }
    }
}
