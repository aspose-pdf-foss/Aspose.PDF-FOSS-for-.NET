using System.Globalization;
using System.Text;
using System.Xml;

namespace Aspose.Pdf.Forms.Xfa;

internal static partial class XfaRenderer
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class OccurrenceExpandState
{
    public System.Xml.XmlDocument owner = null!;
    public System.Xml.XmlElement clone = null!;
    public HashSet<System.Xml.XmlElement> used = null!;
    // A NON-TRIVIAL form packet records the instance set a viewer last saved —
    // under it, a data-less min-0 subform absent from the packet stays removed.
    // Without such a record the merge runs from scratch and every subform gets
    // its <occur initial> instances (the spec default is 1 even when min is 0 —
    // Designer sections like optional bordered tables render once, empty).
    public bool packetRecords;
    public XmlElement root = default!;
    public XmlElement? dataRoot = null;
    public List<XmlElement> groups = default!;
    public XmlElement? formRoot = null;
    public string name = null!;
    public System.Xml.XmlElement? occ;
    public int max;
    public int min;
    public List<System.Xml.XmlElement> avail = null!;
    public bool deepMatched;
    // Explicit <occur initial> (clamped up to min); the spec default is 1.
    public int initial;
    public int n;
    // Form-packet instances: when this subform's instanceManager appears in the
    // form DOM, honour the recorded instance count (never shrinking below the
    // data-driven count — stale packets must not drop bound data). Only an
    // OCCUR-LESS subform takes the boost: without <occur> the template alone
    // clamps to one instance and the packet is the only record of user-added
    // repeats, while a subform with an explicit <occur> already resolves its
    // count from the data (a packet layered on top double-counts).
    public List<System.Xml.XmlElement> fInst = null!;
    public bool packetAuthoritative;
    public List<System.Xml.XmlElement> instances = null!;
}
}
