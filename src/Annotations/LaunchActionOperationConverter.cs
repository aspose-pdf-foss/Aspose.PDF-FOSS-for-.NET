#nullable disable
using System;
using System.Collections;

namespace Aspose.Pdf;

public static class LaunchActionOperationConverter
{
    public const string strOpen = "open";
    public const string strPrint = "print";

    /// <summary>The /Win launch-parameter /O values are the lowercase
    /// keywords "open" and "print", not the enum member names.</summary>
    public static string ToString(LaunchActionOperation op) =>
        op == LaunchActionOperation.Print ? strPrint : strOpen;

    public static LaunchActionOperation ToEnum(string value) =>
        string.Equals(value, strPrint, System.StringComparison.OrdinalIgnoreCase)
            ? LaunchActionOperation.Print
            : LaunchActionOperation.Open;
}
