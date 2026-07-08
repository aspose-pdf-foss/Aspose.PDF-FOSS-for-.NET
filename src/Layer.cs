#nullable disable

using System.Collections.Generic;

namespace Aspose.Pdf
{
    /// <summary>
    /// A PDF layer (Optional Content Group) as exposed through
    /// <see cref="Page.Layers"/>. A layer is either <em>detached</em> — freshly
    /// constructed via <see cref="Layer(string,string)"/> and populated through
    /// <see cref="Contents"/> before being added to a page — or <em>bound</em> to
    /// an existing <see cref="OptionalContentGroup"/> on a page, in which case all
    /// state (visibility, lock, contents) round-trips through that group.
    /// A detached layer added through <c>page.Layers.Add(...)</c> is authored onto
    /// the page (registered as an OCG, content injected) when the document is saved.
    /// </summary>
    public class Layer
    {
        // Detached (authoring) state — used until the layer is attached to a page.
        private string _id;
        private string _name;
        private DefaultState _defaultState = DefaultState.Visible;
        private bool _locked;
        private readonly List<Operator> _contents = new();

        // Bound state — set once the layer is materialised from / authored onto a page.
        private OptionalContentGroup _ocg;
        private List<Layer> _owner;

        /// <summary>Create a new, detached layer. Add content via
        /// <see cref="Contents"/>, then attach it with <c>page.Layers.Add(layer)</c>.</summary>
        public Layer(string id, string name) { _id = id; _name = name; }

        /// <summary>Materialise a layer bound to an existing page OCG.</summary>
        internal Layer(OptionalContentGroup ocg, List<Layer> owner)
        {
            _ocg = ocg;
            _owner = owner;
        }

        internal OptionalContentGroup Ocg => _ocg;
        internal bool IsBound => _ocg is not null;
        // Deleted/flattened layers are marked rather than removed from the owning
        // facade list immediately, so that `foreach (var l in page.Layers) l.Flatten()`
        // does not invalidate the enumerator. The stale entries are purged the next
        // time the Layers collection is accessed.
        internal bool IsRemoved { get; private set; }
        internal DefaultState PendingDefaultState => _defaultState;
        internal bool PendingLocked => _locked;

        internal void BindTo(OptionalContentGroup ocg, List<Layer> owner)
        {
            _ocg = ocg;
            _owner = owner;
        }

        /// <summary>The layer identifier (page-resource property name once attached).</summary>
        public string Id => _ocg?.Id ?? _id;

        /// <summary>The layer's human-readable name.</summary>
        public string Name => _ocg?.Name ?? _name;

        /// <summary>Operators that make up this layer's content. For a detached
        /// layer this is the authoring buffer that is injected (wrapped in
        /// BDC/EMC markers) when the layer is added to a page; for a bound layer
        /// it reflects the operators parsed from the layer's content blocks.</summary>
        public List<Operator> Contents
        {
            get
            {
                if (_ocg is not null && _contents.Count == 0)
                {
                    foreach (var op in _ocg.ContentOperators())
                        _contents.Add(op);
                }
                return _contents;
            }
        }

        /// <summary>Default visibility state. Changes on a bound layer persist on save.</summary>
        public DefaultState DefaultState
        {
            get => _ocg?.DefaultState ?? _defaultState;
            set { if (_ocg is not null) _ocg.DefaultState = value; else _defaultState = value; }
        }

        /// <summary>Whether the layer is locked (cannot be toggled by the viewer).</summary>
        public bool Locked => _ocg?.Locked ?? _locked;

        /// <summary>Lock this layer. Persists on save.</summary>
        public void Lock() { if (_ocg is not null) _ocg.Lock(); else _locked = true; }

        /// <summary>Unlock this layer. Persists on save.</summary>
        public void Unlock() { if (_ocg is not null) _ocg.Unlock(); else _locked = false; }

        /// <summary>Remove this layer and its content from the page. The layer is
        /// removed from the owning <see cref="Page.Layers"/> collection immediately.</summary>
        public void Delete()
        {
            if (_ocg is null) return;
            _ocg.Delete();
            IsRemoved = true;
            _owner?.Remove(this);
        }

        /// <summary>Flatten this layer's content into the unconditional page content.
        /// The layer is marked removed but not pulled from the owning
        /// <see cref="Page.Layers"/> list until that list is next accessed, so
        /// <c>foreach (var l in page.Layers) l.Flatten(...)</c> stays valid.</summary>
        public void Flatten(bool cleanupContentStream)
        {
            if (_ocg is null) return;
            _ocg.Flatten(cleanupContentStream);
            IsRemoved = true;
        }

        /// <summary>Save this layer's content as a standalone single-page PDF stream.</summary>
        public void Save(System.IO.Stream outputStream) { if (_ocg is not null) _ocg.Save(outputStream); }

        /// <summary>Save this layer's content as a standalone single-page PDF file.</summary>
        public void Save(string outputPath) { if (_ocg is not null) _ocg.Save(outputPath); }
    }
}
