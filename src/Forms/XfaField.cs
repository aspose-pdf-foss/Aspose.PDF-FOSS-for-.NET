namespace Aspose.Pdf.Forms;

/// <summary>
/// Represents a single field of an XFA form addressed by its template SOM path
/// (e.g. <c>form1[0].#subform[0].TextField1[0]</c>). Obtain an instance via
/// <see cref="GetInstance(XFA, string)"/>; <see cref="Value"/> returns the field's
/// bound datasets value. This surface matches the Aspose.Pdf public API.
/// </summary>
public sealed class XfaField
{
    private readonly XFA _xfa;
    private readonly string _somPath;

    private XfaField(XFA xfa, string somPath)
    {
        _xfa = xfa;
        _somPath = somPath;
    }

    /// <summary>
    /// Return an <see cref="XfaField"/> for the given template SOM path within
    /// <paramref name="xfa"/>. The path is resolved lazily when <see cref="Value"/>
    /// is read, applying the XFA template-to-data binding.
    /// </summary>
    public static XfaField GetInstance(XFA xfa, string somPath)
        => new XfaField(xfa, somPath);

    /// <summary>The field's value from the XFA datasets, resolved through the
    /// template-to-data binding of its SOM path; empty string when unbound/absent.</summary>
    public string Value => _xfa.ResolveFieldValueBySom(_somPath) ?? string.Empty;
}
