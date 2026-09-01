#nullable disable
using System;
using System.Collections;

namespace Aspose.Pdf;

/// <summary>Categories of warnings emitted during save/load.</summary>
public enum WarningType
{
    SourceFileCorruption = 0,
    DataLoss = 1,
    InvalidInputStreamType = 2,
    MajorFormattingLoss = 3,
    MinorFormattingLoss = 4,
    CompatibilityIssue = 5,
    UnexpectedContent = 6,
}
