#nullable disable
using System;
using System.Collections;

namespace Aspose.Pdf;

public class WarningInfo
{
    public WarningInfo() { }

    public WarningInfo(WarningType type, string message)
    {
        WarningTypeProperty = type;
        WarningMessage = message;
    }

    public string WarningMessage { get; private set; }

    /// <summary>Warning category emitted by the saver/loader.</summary>
    public WarningType WarningTypeProperty { get; private set; }

    /// <summary>FOSS-legacy int-typed warning code.</summary>
    public int WarningType { get; }
}
