using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Annotations.JavascriptExtensions;

public static partial class FieldDateTimeFormatter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class DateFormatState
{
    public List<Aspose.Pdf.Annotations.JavascriptExtensions.FieldDateTimeFormatter.FormatToken> allTokens = null!;
    public List<Aspose.Pdf.Annotations.JavascriptExtensions.FieldDateTimeFormatter.ComponentToken> components = null!;
    public int[] rawVals = null!;
    // Per-token final numeric values
    public int?[] finalVals = null!;
    // Locate the first month and first day component indices
    public int monthIdx;
    public int dayIdx;
    // Auto-correct: if first month > 12 and first day <= 12, swap
    public bool didSwap;
    // Render output
    public System.Text.StringBuilder sb = null!;
    public int compIdx;
    public string dateFormat = default!;
    public string dateValue = default!;
}
}
