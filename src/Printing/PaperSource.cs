namespace Aspose.Pdf.Printing
{
    /// <summary>The paper bin a print job draws from.</summary>
    public sealed class PaperSource
    {
        /// <summary>Creates an unnamed source.</summary>
        public PaperSource()
        {
            SourceName = string.Empty;
        }

        /// <summary>Creates a source of the given kind and name.</summary>
        public PaperSource(PaperSourceKind kind, string name)
        {
            Kind = kind;
            SourceName = name;
        }

        /// <summary>
        /// The bin this source names. Drivers number their own bins from
        /// <see cref="PaperSourceKind.Custom"/> upwards, and every one of those reads back
        /// as <see cref="PaperSourceKind.Custom"/>; <see cref="RawKind"/> keeps the number.
        /// </summary>
        public PaperSourceKind Kind
        {
            get => RawKind >= (int)PaperSourceKind.Custom
                ? PaperSourceKind.Custom
                : (PaperSourceKind)RawKind;
            set => RawKind = (int)value;
        }

        /// <summary>The bin number as the driver states it.</summary>
        public int RawKind { get; set; }

        /// <summary>The bin's name.</summary>
        public string SourceName { get; set; }

        /// <inheritdoc/>
        public override string ToString()
            => "[PaperSource " + SourceName + " Kind=" + Kind + "]";
    }

    /// <summary>The standard paper sources, one per <see cref="PaperSourceKind"/>.</summary>
    /// <remarks>Each entry is a distinct instance: callers mutate the sources they are handed.</remarks>
    public static class PaperSources
    {
        /// <summary>The upper bin.</summary>
        public static readonly PaperSource Upper = new(PaperSourceKind.Upper, "Upper");
        /// <summary>The lower bin.</summary>
        public static readonly PaperSource Lower = new(PaperSourceKind.Lower, "Lower");
        /// <summary>The middle bin.</summary>
        public static readonly PaperSource Middle = new(PaperSourceKind.Middle, "Middle");
        /// <summary>Manually fed paper.</summary>
        public static readonly PaperSource Manual = new(PaperSourceKind.Manual, "Manual");
        /// <summary>The envelope bin.</summary>
        public static readonly PaperSource Envelope = new(PaperSourceKind.Envelope, "Envelope");
        /// <summary>Manually fed envelopes.</summary>
        public static readonly PaperSource ManualFeedEnvelope = new(PaperSourceKind.ManualFeed, "Manual feed envelope");
        /// <summary>Automatically fed paper.</summary>
        public static readonly PaperSource AutomaticFeed = new(PaperSourceKind.AutomaticFeed, "Automatic feed");
        /// <summary>Paper fed by a tractor.</summary>
        public static readonly PaperSource TractorFeed = new(PaperSourceKind.TractorFeed, "Tractor feed");
        /// <summary>The small-format bin.</summary>
        public static readonly PaperSource SmallFormat = new(PaperSourceKind.SmallFormat, "Small format");
        /// <summary>The large-format bin.</summary>
        public static readonly PaperSource LargeFormat = new(PaperSourceKind.LargeFormat, "Large format");
        /// <summary>The large-capacity bin.</summary>
        public static readonly PaperSource LargeCapacity = new(PaperSourceKind.LargeCapacity, "Large capacity");
        /// <summary>A paper cassette.</summary>
        public static readonly PaperSource Cassette = new(PaperSourceKind.Cassette, "Cassette");
        /// <summary>The bin the form names.</summary>
        public static readonly PaperSource FormSource = new(PaperSourceKind.FormSource, "Form source");
    }
}
