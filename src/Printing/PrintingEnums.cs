namespace Aspose.Pdf.Printing
{
    /// <summary>How a printer prints on both sides of a sheet.</summary>
    public enum Duplex
    {
        /// <summary>The printer default setting.</summary>
        Default = -1,
        /// <summary>Single-sided printing.</summary>
        Simplex = 1,
        /// <summary>Double-sided printing, the sheet turning about its short edge.</summary>
        Vertical = 2,
        /// <summary>Double-sided printing, the sheet turning about its long edge.</summary>
        Horizontal = 3,
    }

    /// <summary>Which pages of a document a print job covers.</summary>
    public enum PrintRange
    {
        /// <summary>Every page.</summary>
        AllPages = 0,
        /// <summary>The selected part of the document.</summary>
        Selection = 1,
        /// <summary>The page range between <c>FromPage</c> and <c>ToPage</c>.</summary>
        SomePages = 2,
        /// <summary>The page currently on display.</summary>
        CurrentPage = 0x00400000,
    }

    /// <summary>The bin a printer draws paper from.</summary>
    public enum PaperSourceKind
    {
        /// <summary>The upper bin.</summary>
        Upper = 1,
        /// <summary>The lower bin.</summary>
        Lower = 2,
        /// <summary>The middle bin.</summary>
        Middle = 3,
        /// <summary>Manually fed paper.</summary>
        Manual = 4,
        /// <summary>The envelope bin.</summary>
        Envelope = 5,
        /// <summary>Manually fed envelopes.</summary>
        ManualFeed = 6,
        /// <summary>Automatically fed paper.</summary>
        AutomaticFeed = 7,
        /// <summary>Paper fed by a tractor.</summary>
        TractorFeed = 8,
        /// <summary>The small-format bin.</summary>
        SmallFormat = 9,
        /// <summary>The large-format bin.</summary>
        LargeFormat = 10,
        /// <summary>The large-capacity bin.</summary>
        LargeCapacity = 11,
        /// <summary>A paper cassette.</summary>
        Cassette = 14,
        /// <summary>The bin the form names.</summary>
        FormSource = 15,
        /// <summary>A printer-specific bin. Driver-defined kinds start here.</summary>
        Custom = 256 + 1,
    }

    /// <summary>A printer resolution expressed as a quality band rather than a dpi pair.</summary>
    public enum PrinterResolutionKind
    {
        /// <summary>High quality.</summary>
        High = -4,
        /// <summary>Medium quality.</summary>
        Medium = -3,
        /// <summary>Low quality.</summary>
        Low = -2,
        /// <summary>Draft quality.</summary>
        Draft = -1,
        /// <summary>The resolution is the <c>X</c>/<c>Y</c> dpi pair rather than a band.</summary>
        Custom = 0,
    }

    /// <summary>
    /// A standard paper size. The values are the Windows <c>DMPAPER_*</c> numbering, which
    /// <see cref="System.Drawing.Printing.PaperSize.RawKind"/> also carries, so the two
    /// enumerations convert by a plain cast.
    /// </summary>
    public enum PaperKind
    {
        /// <summary>A size the printer driver does not name; its dimensions stand on their own.</summary>
        Custom = 0,
        /// <summary>Letter, 8 1/2 x 11 in.</summary>
        Letter = 1,
        /// <summary>Letter small, 8 1/2 x 11 in.</summary>
        LetterSmall = 2,
        /// <summary>Tabloid, 11 x 17 in.</summary>
        Tabloid = 3,
        /// <summary>Ledger, 17 x 11 in.</summary>
        Ledger = 4,
        /// <summary>Legal, 8 1/2 x 14 in.</summary>
        Legal = 5,
        /// <summary>Statement, 5 1/2 x 8 1/2 in.</summary>
        Statement = 6,
        /// <summary>Executive, 7 1/4 x 10 1/2 in.</summary>
        Executive = 7,
        /// <summary>A3, 297 x 420 mm.</summary>
        A3 = 8,
        /// <summary>A4, 210 x 297 mm.</summary>
        A4 = 9,
        /// <summary>A4 small, 210 x 297 mm.</summary>
        A4Small = 10,
        /// <summary>A5, 148 x 210 mm.</summary>
        A5 = 11,
        /// <summary>B4 (JIS), 257 x 364 mm.</summary>
        B4 = 12,
        /// <summary>B5 (JIS), 182 x 257 mm.</summary>
        B5 = 13,
        /// <summary>Folio, 8 1/2 x 13 in.</summary>
        Folio = 14,
        /// <summary>Quarto, 215 x 275 mm.</summary>
        Quarto = 15,
        /// <summary>Standard, 10 x 14 in.</summary>
        Standard10x14 = 16,
        /// <summary>Standard, 11 x 17 in.</summary>
        Standard11x17 = 17,
        /// <summary>Note, 8 1/2 x 11 in.</summary>
        Note = 18,
        /// <summary>#9 envelope, 3 7/8 x 8 7/8 in.</summary>
        Number9Envelope = 19,
        /// <summary>#10 envelope, 4 1/8 x 9 1/2 in.</summary>
        Number10Envelope = 20,
        /// <summary>#11 envelope, 4 1/2 x 10 3/8 in.</summary>
        Number11Envelope = 21,
        /// <summary>#12 envelope, 4 3/4 x 11 in.</summary>
        Number12Envelope = 22,
        /// <summary>#14 envelope, 5 x 11 1/2 in.</summary>
        Number14Envelope = 23,
        /// <summary>C sheet, 17 x 22 in.</summary>
        CSheet = 24,
        /// <summary>D sheet, 22 x 34 in.</summary>
        DSheet = 25,
        /// <summary>E sheet, 34 x 44 in.</summary>
        ESheet = 26,
        /// <summary>DL envelope, 110 x 220 mm.</summary>
        DLEnvelope = 27,
        /// <summary>C5 envelope, 162 x 229 mm.</summary>
        C5Envelope = 28,
        /// <summary>C3 envelope, 324 x 458 mm.</summary>
        C3Envelope = 29,
        /// <summary>C4 envelope, 229 x 324 mm.</summary>
        C4Envelope = 30,
        /// <summary>C6 envelope, 114 x 162 mm.</summary>
        C6Envelope = 31,
        /// <summary>C65 envelope, 114 x 229 mm.</summary>
        C65Envelope = 32,
        /// <summary>B4 envelope, 250 x 353 mm.</summary>
        B4Envelope = 33,
        /// <summary>B5 envelope, 176 x 250 mm.</summary>
        B5Envelope = 34,
        /// <summary>B6 envelope, 176 x 125 mm.</summary>
        B6Envelope = 35,
        /// <summary>Italy envelope, 110 x 230 mm.</summary>
        ItalyEnvelope = 36,
        /// <summary>Monarch envelope, 3 7/8 x 7 1/2 in.</summary>
        MonarchEnvelope = 37,
        /// <summary>6 3/4 envelope, 3 5/8 x 6 1/2 in.</summary>
        PersonalEnvelope = 38,
        /// <summary>US standard fanfold, 14 7/8 x 11 in.</summary>
        USStandardFanfold = 39,
        /// <summary>German standard fanfold, 8 1/2 x 12 in.</summary>
        GermanStandardFanfold = 40,
        /// <summary>German legal fanfold, 8 1/2 x 13 in.</summary>
        GermanLegalFanfold = 41,
        /// <summary>B4 (ISO), 250 x 353 mm.</summary>
        IsoB4 = 42,
        /// <summary>Japanese postcard, 100 x 148 mm.</summary>
        JapanesePostcard = 43,
        /// <summary>Standard, 9 x 11 in.</summary>
        Standard9x11 = 44,
        /// <summary>Standard, 10 x 11 in.</summary>
        Standard10x11 = 45,
        /// <summary>Standard, 15 x 11 in.</summary>
        Standard15x11 = 46,
        /// <summary>Invitation envelope, 220 x 220 mm.</summary>
        InviteEnvelope = 47,
        /// <summary>Letter extra, 9 1/2 x 12 in.</summary>
        LetterExtra = 50,
        /// <summary>Legal extra, 9 1/2 x 15 in.</summary>
        LegalExtra = 51,
        /// <summary>Tabloid extra, 11.69 x 18 in.</summary>
        TabloidExtra = 52,
        /// <summary>A4 extra, 236 x 322 mm.</summary>
        A4Extra = 53,
        /// <summary>Letter transverse, 8 1/2 x 11 in.</summary>
        LetterTransverse = 54,
        /// <summary>A4 transverse, 210 x 297 mm.</summary>
        A4Transverse = 55,
        /// <summary>Letter extra transverse, 9 1/2 x 12 in.</summary>
        LetterExtraTransverse = 56,
        /// <summary>SuperA/SuperA/A4, 227 x 356 mm.</summary>
        APlus = 57,
        /// <summary>SuperB/SuperB/A3, 305 x 487 mm.</summary>
        BPlus = 58,
        /// <summary>Letter plus, 8 1/2 x 12.69 in.</summary>
        LetterPlus = 59,
        /// <summary>A4 plus, 210 x 330 mm.</summary>
        A4Plus = 60,
        /// <summary>A5 transverse, 148 x 210 mm.</summary>
        A5Transverse = 61,
        /// <summary>B5 (JIS) transverse, 182 x 257 mm.</summary>
        B5Transverse = 62,
        /// <summary>A3 extra, 322 x 445 mm.</summary>
        A3Extra = 63,
        /// <summary>A5 extra, 174 x 235 mm.</summary>
        A5Extra = 64,
        /// <summary>B5 (ISO) extra, 201 x 276 mm.</summary>
        B5Extra = 65,
        /// <summary>A2, 420 x 594 mm.</summary>
        A2 = 66,
        /// <summary>A3 transverse, 297 x 420 mm.</summary>
        A3Transverse = 67,
        /// <summary>A3 extra transverse, 322 x 445 mm.</summary>
        A3ExtraTransverse = 68,
        /// <summary>Japanese double postcard, 200 x 148 mm.</summary>
        JapaneseDoublePostcard = 69,
        /// <summary>A6, 105 x 148 mm.</summary>
        A6 = 70,
        /// <summary>Japanese Kaku #2 envelope.</summary>
        JapaneseEnvelopeKakuNumber2 = 71,
        /// <summary>Japanese Kaku #3 envelope.</summary>
        JapaneseEnvelopeKakuNumber3 = 72,
        /// <summary>Japanese Chou #3 envelope.</summary>
        JapaneseEnvelopeChouNumber3 = 73,
        /// <summary>Japanese Chou #4 envelope.</summary>
        JapaneseEnvelopeChouNumber4 = 74,
        /// <summary>Letter rotated, 11 x 8 1/2 in.</summary>
        LetterRotated = 75,
        /// <summary>A3 rotated, 420 x 297 mm.</summary>
        A3Rotated = 76,
        /// <summary>A4 rotated, 297 x 210 mm.</summary>
        A4Rotated = 77,
        /// <summary>A5 rotated, 210 x 148 mm.</summary>
        A5Rotated = 78,
        /// <summary>B4 (JIS) rotated, 364 x 257 mm.</summary>
        B4JisRotated = 79,
        /// <summary>B5 (JIS) rotated, 257 x 182 mm.</summary>
        B5JisRotated = 80,
        /// <summary>Japanese postcard rotated, 148 x 100 mm.</summary>
        JapanesePostcardRotated = 81,
        /// <summary>Japanese double postcard rotated, 148 x 200 mm.</summary>
        JapaneseDoublePostcardRotated = 82,
        /// <summary>A6 rotated, 148 x 105 mm.</summary>
        A6Rotated = 83,
        /// <summary>Japanese Kaku #2 envelope rotated.</summary>
        JapaneseEnvelopeKakuNumber2Rotated = 84,
        /// <summary>Japanese Kaku #3 envelope rotated.</summary>
        JapaneseEnvelopeKakuNumber3Rotated = 85,
        /// <summary>Japanese Chou #3 envelope rotated.</summary>
        JapaneseEnvelopeChouNumber3Rotated = 86,
        /// <summary>Japanese Chou #4 envelope rotated.</summary>
        JapaneseEnvelopeChouNumber4Rotated = 87,
        /// <summary>B6 (JIS), 128 x 182 mm.</summary>
        B6Jis = 88,
        /// <summary>B6 (JIS) rotated, 182 x 128 mm.</summary>
        B6JisRotated = 89,
        /// <summary>Standard, 12 x 11 in.</summary>
        Standard12x11 = 90,
        /// <summary>Japanese You #4 envelope.</summary>
        JapaneseEnvelopeYouNumber4 = 91,
        /// <summary>Japanese You #4 envelope rotated.</summary>
        JapaneseEnvelopeYouNumber4Rotated = 92,
        /// <summary>PRC 16K, 146 x 215 mm.</summary>
        Prc16K = 93,
        /// <summary>PRC 32K, 97 x 151 mm.</summary>
        Prc32K = 94,
        /// <summary>PRC 32K (big), 97 x 151 mm.</summary>
        Prc32KBig = 95,
        /// <summary>PRC #1 envelope, 102 x 165 mm.</summary>
        PrcEnvelopeNumber1 = 96,
        /// <summary>PRC #2 envelope, 102 x 176 mm.</summary>
        PrcEnvelopeNumber2 = 97,
        /// <summary>PRC #3 envelope, 125 x 176 mm.</summary>
        PrcEnvelopeNumber3 = 98,
        /// <summary>PRC #4 envelope, 110 x 208 mm.</summary>
        PrcEnvelopeNumber4 = 99,
        /// <summary>PRC #5 envelope, 110 x 220 mm.</summary>
        PrcEnvelopeNumber5 = 100,
        /// <summary>PRC #6 envelope, 120 x 230 mm.</summary>
        PrcEnvelopeNumber6 = 101,
        /// <summary>PRC #7 envelope, 160 x 230 mm.</summary>
        PrcEnvelopeNumber7 = 102,
        /// <summary>PRC #8 envelope, 120 x 309 mm.</summary>
        PrcEnvelopeNumber8 = 103,
        /// <summary>PRC #9 envelope, 229 x 324 mm.</summary>
        PrcEnvelopeNumber9 = 104,
        /// <summary>PRC #10 envelope, 324 x 458 mm.</summary>
        PrcEnvelopeNumber10 = 105,
        /// <summary>PRC 16K rotated, 215 x 146 mm.</summary>
        Prc16KRotated = 106,
        /// <summary>PRC 32K rotated, 151 x 97 mm.</summary>
        Prc32KRotated = 107,
        /// <summary>PRC 32K (big) rotated, 151 x 97 mm.</summary>
        Prc32KBigRotated = 108,
        /// <summary>PRC #1 envelope rotated, 165 x 102 mm.</summary>
        PrcEnvelopeNumber1Rotated = 109,
        /// <summary>PRC #2 envelope rotated, 176 x 102 mm.</summary>
        PrcEnvelopeNumber2Rotated = 110,
        /// <summary>PRC #3 envelope rotated, 176 x 125 mm.</summary>
        PrcEnvelopeNumber3Rotated = 111,
        /// <summary>PRC #4 envelope rotated, 208 x 110 mm.</summary>
        PrcEnvelopeNumber4Rotated = 112,
        /// <summary>PRC #5 envelope rotated, 220 x 110 mm.</summary>
        PrcEnvelopeNumber5Rotated = 113,
        /// <summary>PRC #6 envelope rotated, 230 x 120 mm.</summary>
        PrcEnvelopeNumber6Rotated = 114,
        /// <summary>PRC #7 envelope rotated, 230 x 160 mm.</summary>
        PrcEnvelopeNumber7Rotated = 115,
        /// <summary>PRC #8 envelope rotated, 309 x 120 mm.</summary>
        PrcEnvelopeNumber8Rotated = 116,
        /// <summary>PRC #9 envelope rotated, 324 x 229 mm.</summary>
        PrcEnvelopeNumber9Rotated = 117,
        /// <summary>PRC #10 envelope rotated, 458 x 324 mm.</summary>
        PrcEnvelopeNumber10Rotated = 118,
    }
}
