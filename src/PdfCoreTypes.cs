#nullable disable

using System;
using System.Collections;

namespace Aspose.Pdf
{
    public class DocumentCollection : IEnumerable
    {
        public int Count { get; }
        public void Add(Document doc) { }
        public IEnumerator GetEnumerator() => throw new NotImplementedException();
    }

    public class InterruptMonitor
    {
        public void Interrupt() { }
    }

    public interface IWarningCallback
    {
        ReturnAction Warning(WarningInfo warning);
    }

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

    public enum ReturnAction { Continue, Abort }

    // Rotation — was in Aspose.Pdf.Annotations, moved here to match the public API
    public enum Rotation { None = 0, on90 = 90, on180 = 180, on270 = 270, on360 = 360 }

    public enum Permissions
    {
        PrintDocument = 4,
        ModifyContent = 8,
        ExtractContent = 16,
        ModifyTextAnnotations = 32,
        FillForm = 256,
        ExtractContentWithDisabilities = 512,
        AssembleDocument = 1024,
        PrintingQuality = 2048
    }

    public enum PdfAStandardVersion { Auto, PDF_A_1A, PDF_A_1B, PDF_A_2A, PDF_A_2B, PDF_A_2U, PDF_A_3A, PDF_A_3B, PDF_A_3U, PDF_A_4, PDF_A_4E, PDF_A_4F }
    public enum PrintScaling { None, AppDefault }
    public enum LaunchActionOperation { None, Open, Print }
    public enum ImageFilterType { Jpeg2000, Flate, Jpeg, CCITTFax }

    /// <summary>Identifies which password (if any) is in effect on an
    /// encrypted PDF.</summary>
    public enum PasswordType
    {
        /// <summary>The PDF is not encrypted.</summary>
        None,
        /// <summary>The user (open) password was supplied.</summary>
        User,
        /// <summary>The owner (edit) password was supplied.</summary>
        Owner,
        /// <summary>Encrypted PDF that has not been decrypted; password
        /// type cannot yet be determined.</summary>
        Inaccessible,
    }

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
}
