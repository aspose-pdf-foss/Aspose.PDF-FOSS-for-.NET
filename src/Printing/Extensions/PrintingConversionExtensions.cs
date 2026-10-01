using System;
using NativeDuplex = System.Drawing.Printing.Duplex;
using NativeMargins = System.Drawing.Printing.Margins;
using NativePageSettings = System.Drawing.Printing.PageSettings;
using NativePaperSize = System.Drawing.Printing.PaperSize;
using NativePaperSource = System.Drawing.Printing.PaperSource;
using NativePrinterResolution = System.Drawing.Printing.PrinterResolution;
using NativePrinterResolutionKind = System.Drawing.Printing.PrinterResolutionKind;
using NativePrinterSettings = System.Drawing.Printing.PrinterSettings;
using NativePrintRange = System.Drawing.Printing.PrintRange;

namespace Aspose.Pdf.Printing
{
    /// <summary>Converts between this library's <see cref="PaperSize"/> and the
    /// System.Drawing one a printer driver speaks.</summary>
    /// <remarks>
    /// Both sides measure in hundredths of an inch and number their kinds the same way, so
    /// each conversion is a field-for-field copy. The native type only lets width, height
    /// and name be set while its kind is custom, which is what its three-argument
    /// constructor leaves it as - so the dimensions go in through the constructor and the
    /// kind is stamped on afterwards.
    /// </remarks>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public static class PaperSizeExtensions
    {
        /// <summary>Copies an Aspose paper size into a System.Drawing one.</summary>
        public static NativePaperSize ToNativePaperSize(this PaperSize paperSize)
        {
            if (paperSize is null) throw new ArgumentNullException(nameof(paperSize));
            return new NativePaperSize(paperSize.PaperName ?? string.Empty, paperSize.Width, paperSize.Height)
            {
                RawKind = (int)paperSize.Kind,
            };
        }

        /// <summary>Copies a System.Drawing paper size into an Aspose one.</summary>
        public static PaperSize ToAsposePaperSize(this NativePaperSize nativeSize)
        {
            if (nativeSize is null) throw new ArgumentNullException(nameof(nativeSize));
            return new PaperSize(nativeSize.PaperName, nativeSize.Width, nativeSize.Height)
            {
                Kind = (PaperKind)nativeSize.RawKind,
            };
        }
    }

    /// <summary>Converts between this library's <see cref="PaperSource"/> and the
    /// System.Drawing one.</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public static class PaperSourceExtensions
    {
        /// <summary>Copies an Aspose paper source into a System.Drawing one.</summary>
        public static NativePaperSource ToNativePaperSource(this PaperSource paperSource)
        {
            if (paperSource is null) throw new ArgumentNullException(nameof(paperSource));
            return new NativePaperSource
            {
                RawKind = paperSource.RawKind,
                SourceName = paperSource.SourceName,
            };
        }

        /// <summary>Copies a System.Drawing paper source into an Aspose one.</summary>
        public static PaperSource ToAsposePaperSource(this NativePaperSource nativeSource)
        {
            if (nativeSource is null) throw new ArgumentNullException(nameof(nativeSource));
            return new PaperSource
            {
                RawKind = nativeSource.RawKind,
                SourceName = nativeSource.SourceName,
            };
        }
    }

    /// <summary>Converts between this library's <see cref="PrinterResolution"/> and the
    /// System.Drawing one.</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public static class PrinterResolutionExtensions
    {
        /// <summary>Copies an Aspose printer resolution into a System.Drawing one.</summary>
        public static NativePrinterResolution ToNativePrinterResolution(this PrinterResolution printerResolution)
        {
            if (printerResolution is null) throw new ArgumentNullException(nameof(printerResolution));
            return new NativePrinterResolution
            {
                Kind = (NativePrinterResolutionKind)printerResolution.Kind,
                X = printerResolution.X,
                Y = printerResolution.Y,
            };
        }

        /// <summary>Copies a System.Drawing printer resolution into an Aspose one.</summary>
        public static PrinterResolution ToAsposePrinterResolution(this NativePrinterResolution nativeResolution)
        {
            if (nativeResolution is null) throw new ArgumentNullException(nameof(nativeResolution));
            return new PrinterResolution
            {
                Kind = (PrinterResolutionKind)nativeResolution.Kind,
                X = nativeResolution.X,
                Y = nativeResolution.Y,
            };
        }
    }

    /// <summary>Converts between this library's <see cref="PrinterSettings"/> and the
    /// System.Drawing one a print job is submitted with.</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public static class PrinterSettingsExtensions
    {
        /// <summary>Copies Aspose printer settings into a System.Drawing set.</summary>
        public static NativePrinterSettings ToNativePrinterSettings(this PrinterSettings printerSettings)
        {
            if (printerSettings is null) throw new ArgumentNullException(nameof(printerSettings));
            var native = new NativePrinterSettings();
            native.Assign(printerSettings);
            return native;
        }

        /// <summary>Writes Aspose printer settings over an existing System.Drawing set, so a
        /// job can be configured without discarding what the driver already put there.</summary>
        internal static void Assign(this NativePrinterSettings nativeSettings, PrinterSettings printerSettings)
        {
            // The page bounds are validated against each other, so the window comes first:
            // ToPage cannot be raised past a MaximumPage that is still at its old value.
            nativeSettings.MinimumPage = printerSettings.MinimumPage;
            nativeSettings.MaximumPage = printerSettings.MaximumPage;
            nativeSettings.FromPage = printerSettings.FromPage;
            nativeSettings.ToPage = printerSettings.ToPage;
            nativeSettings.Collate = printerSettings.Collate;
            nativeSettings.Copies = checked((short)printerSettings.Copies);
            nativeSettings.Duplex = (NativeDuplex)printerSettings.Duplex;
            nativeSettings.PrintRange = (NativePrintRange)printerSettings.PrintRange;
            nativeSettings.PrintToFile = printerSettings.PrintToFile;
            // Both names reject a blank: the native settings treat "no name" as a state of
            // their own rather than as an empty string, and assigning one would raise
            // rather than clear it. Leaving them alone says the same thing.
            if (!string.IsNullOrEmpty(printerSettings.PrintFileName))
                nativeSettings.PrintFileName = printerSettings.PrintFileName;
            if (!string.IsNullOrEmpty(printerSettings.PrinterName))
                nativeSettings.PrinterName = printerSettings.PrinterName;
        }

        /// <summary>Copies System.Drawing printer settings into an Aspose set.</summary>
        public static PrinterSettings ToAsposePrinterSettings(this NativePrinterSettings nativeSettings)
        {
            if (nativeSettings is null) throw new ArgumentNullException(nameof(nativeSettings));
            return new PrinterSettings
            {
                Collate = nativeSettings.Collate,
                Copies = nativeSettings.Copies,
                Duplex = (Duplex)nativeSettings.Duplex,
                MinimumPage = nativeSettings.MinimumPage,
                MaximumPage = nativeSettings.MaximumPage,
                FromPage = nativeSettings.FromPage,
                ToPage = nativeSettings.ToPage,
                PrinterName = nativeSettings.PrinterName,
                PrintFileName = nativeSettings.PrintFileName,
                PrintRange = (PrintRange)nativeSettings.PrintRange,
                PrintToFile = nativeSettings.PrintToFile,
            };
        }
    }

    /// <summary>Converts between this library's <see cref="PageSettings"/> and the
    /// System.Drawing one.</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public static class PageSettingsExtensions
    {
        /// <summary>Copies Aspose page settings into a System.Drawing set.</summary>
        public static NativePageSettings ToNativePageSettings(this PageSettings pageSettings)
        {
            if (pageSettings is null) throw new ArgumentNullException(nameof(pageSettings));
            var native = pageSettings.PrinterSettings is null
                ? new NativePageSettings()
                : new NativePageSettings(pageSettings.PrinterSettings.ToNativePrinterSettings());
            native.Assign(pageSettings);
            return native;
        }

        /// <summary>
        /// Writes Aspose page settings over an existing System.Drawing set. The paper, source
        /// resolution and margins are only written when the caller assigned them, leaving the
        /// driver's own where they did not. Colour and orientation are always written. A page
        /// settings object reports zero margins until told otherwise, but the reference does not
        /// print with what it reports: a page given no margins lands one inch in, the driver's
        /// default - measured on its own XPS prints of a colour page, both as drawing commands
        /// and as an image, and of a Letter page configured through the printer settings alone.
        /// </summary>
        internal static void Assign(this NativePageSettings nativeSettings, PageSettings pageSettings)
        {
            // Like every property below, orientation and colour override the settings they are
            // laid over only when assigned: a job whose printer defaults ask for landscape and whose
            // own page settings never mention orientation prints landscape, as the reference prints it.
            if (!pageSettings.IsDefaultColor)
                nativeSettings.Color = pageSettings.Color;
            if (!pageSettings.IsDefaultLandscape)
                nativeSettings.Landscape = pageSettings.Landscape;
            var margins = pageSettings.Margins;
            if (!pageSettings.IsDefaultMargins && margins is not null)
                nativeSettings.Margins = new NativeMargins(margins.Left, margins.Right, margins.Top, margins.Bottom);
            if (!pageSettings.IsDefaultPaperSize && pageSettings.PaperSize is not null)
                nativeSettings.PaperSize = pageSettings.PaperSize.ToNativePaperSize();
            if (!pageSettings.IsDefaultPaperSource && pageSettings.PaperSource is not null)
                nativeSettings.PaperSource = pageSettings.PaperSource.ToNativePaperSource();
            if (!pageSettings.IsDefaultPrinterResolution && pageSettings.PrinterResolution is not null)
                nativeSettings.PrinterResolution = pageSettings.PrinterResolution.ToNativePrinterResolution();
        }

        /// <summary>Copies System.Drawing page settings into an Aspose set.</summary>
        public static PageSettings ToAsposePageSettings(this NativePageSettings nativeSettings)
        {
            if (nativeSettings is null) throw new ArgumentNullException(nameof(nativeSettings));
            return new PageSettings
            {
                Color = nativeSettings.Color,
                Landscape = nativeSettings.Landscape,
                Margins = new Devices.Margins(
                    nativeSettings.Margins.Left,
                    nativeSettings.Margins.Right,
                    nativeSettings.Margins.Top,
                    nativeSettings.Margins.Bottom),
                PaperSize = nativeSettings.PaperSize.ToAsposePaperSize(),
                PaperSource = nativeSettings.PaperSource.ToAsposePaperSource(),
                PrinterResolution = nativeSettings.PrinterResolution.ToAsposePrinterResolution(),
                PrinterSettings = nativeSettings.PrinterSettings.ToAsposePrinterSettings(),
            };
        }
    }
}
