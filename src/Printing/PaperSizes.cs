namespace Aspose.Pdf.Printing
{
    /// <summary>
    /// The standard paper sizes, one per <see cref="PaperKind"/>.
    /// </summary>
    /// <remarks>
    /// Dimensions are in hundredths of an inch and are the Windows <c>DMPAPER_*</c> table's
    /// own values, so a size taken from here and one a printer driver reports for the same
    /// kind agree. The metric sizes are their millimetre definition converted at 25.4 mm to
    /// the inch and rounded (A4, 210 x 297 mm, lands on 827 x 1169); the US sizes are their
    /// fractional-inch definition truncated, which is why Monarch, 3 7/8 in wide, is 387 and
    /// not 388. Each entry is a distinct instance: callers mutate the sizes they are handed.
    /// </remarks>
    public static class PaperSizes
    {
        /// <summary>Letter, 8 1/2 x 11 in.</summary>
        public static readonly PaperSize Letter = new(PaperKind.Letter, 850, 1100);
        /// <summary>Letter small, 8 1/2 x 11 in.</summary>
        public static readonly PaperSize LetterSmall = new(PaperKind.LetterSmall, 850, 1100);
        /// <summary>Tabloid, 11 x 17 in.</summary>
        public static readonly PaperSize Tabloid = new(PaperKind.Tabloid, 1100, 1700);
        /// <summary>Ledger, 17 x 11 in.</summary>
        public static readonly PaperSize Ledger = new(PaperKind.Ledger, 1700, 1100);
        /// <summary>Legal, 8 1/2 x 14 in.</summary>
        public static readonly PaperSize Legal = new(PaperKind.Legal, 850, 1400);
        /// <summary>Statement, 5 1/2 x 8 1/2 in.</summary>
        public static readonly PaperSize Statement = new(PaperKind.Statement, 550, 850);
        /// <summary>Executive, 7 1/4 x 10 1/2 in.</summary>
        public static readonly PaperSize Executive = new(PaperKind.Executive, 725, 1050);
        /// <summary>A3, 297 x 420 mm.</summary>
        public static readonly PaperSize A3 = new(PaperKind.A3, 1169, 1654);
        /// <summary>A4, 210 x 297 mm.</summary>
        public static readonly PaperSize A4 = new(PaperKind.A4, 827, 1169);
        /// <summary>A4 small, 210 x 297 mm.</summary>
        public static readonly PaperSize A4Small = new(PaperKind.A4Small, 827, 1169);
        /// <summary>A5, 148 x 210 mm.</summary>
        public static readonly PaperSize A5 = new(PaperKind.A5, 583, 827);
        /// <summary>B4 (JIS), 257 x 364 mm.</summary>
        public static readonly PaperSize B4 = new(PaperKind.B4, 1012, 1433);
        /// <summary>B5 (JIS), 182 x 257 mm.</summary>
        public static readonly PaperSize B5 = new(PaperKind.B5, 717, 1012);
        /// <summary>Folio, 8 1/2 x 13 in.</summary>
        public static readonly PaperSize Folio = new(PaperKind.Folio, 850, 1300);
        /// <summary>Quarto, 215 x 275 mm.</summary>
        public static readonly PaperSize Quarto = new(PaperKind.Quarto, 846, 1083);
        /// <summary>Standard, 10 x 14 in.</summary>
        public static readonly PaperSize Standard10x14 = new(PaperKind.Standard10x14, 1000, 1400);
        /// <summary>Standard, 11 x 17 in.</summary>
        public static readonly PaperSize Standard11x17 = new(PaperKind.Standard11x17, 1100, 1700);
        /// <summary>Note, 8 1/2 x 11 in.</summary>
        public static readonly PaperSize Note = new(PaperKind.Note, 850, 1100);
        /// <summary>#9 envelope, 3 7/8 x 8 7/8 in.</summary>
        public static readonly PaperSize Number9Envelope = new(PaperKind.Number9Envelope, 387, 887);
        /// <summary>#10 envelope, 4 1/8 x 9 1/2 in.</summary>
        public static readonly PaperSize Number10Envelope = new(PaperKind.Number10Envelope, 412, 950);
        /// <summary>#11 envelope, 4 1/2 x 10 3/8 in.</summary>
        public static readonly PaperSize Number11Envelope = new(PaperKind.Number11Envelope, 450, 1037);
        /// <summary>#12 envelope, 4 3/4 x 11 in.</summary>
        public static readonly PaperSize Number12Envelope = new(PaperKind.Number12Envelope, 475, 1100);
        /// <summary>#14 envelope, 5 x 11 1/2 in.</summary>
        public static readonly PaperSize Number14Envelope = new(PaperKind.Number14Envelope, 500, 1150);
        /// <summary>C sheet, 17 x 22 in.</summary>
        public static readonly PaperSize CSheet = new(PaperKind.CSheet, 1700, 2200);
        /// <summary>D sheet, 22 x 34 in.</summary>
        public static readonly PaperSize DSheet = new(PaperKind.DSheet, 2200, 3400);
        /// <summary>E sheet, 34 x 44 in.</summary>
        public static readonly PaperSize ESheet = new(PaperKind.ESheet, 3400, 4400);
        /// <summary>DL envelope, 110 x 220 mm.</summary>
        public static readonly PaperSize DLEnvelope = new(PaperKind.DLEnvelope, 433, 866);
        /// <summary>C5 envelope, 162 x 229 mm.</summary>
        public static readonly PaperSize C5Envelope = new(PaperKind.C5Envelope, 638, 902);
        /// <summary>C3 envelope, 324 x 458 mm.</summary>
        public static readonly PaperSize C3Envelope = new(PaperKind.C3Envelope, 1276, 1803);
        /// <summary>C4 envelope, 229 x 324 mm.</summary>
        public static readonly PaperSize C4Envelope = new(PaperKind.C4Envelope, 902, 1276);
        /// <summary>C6 envelope, 114 x 162 mm.</summary>
        public static readonly PaperSize C6Envelope = new(PaperKind.C6Envelope, 449, 638);
        /// <summary>C65 envelope, 114 x 229 mm.</summary>
        public static readonly PaperSize C65Envelope = new(PaperKind.C65Envelope, 449, 902);
        /// <summary>B4 envelope, 250 x 353 mm.</summary>
        public static readonly PaperSize B4Envelope = new(PaperKind.B4Envelope, 984, 1390);
        /// <summary>B5 envelope, 176 x 250 mm.</summary>
        public static readonly PaperSize B5Envelope = new(PaperKind.B5Envelope, 693, 984);
        /// <summary>B6 envelope, 176 x 125 mm.</summary>
        public static readonly PaperSize B6Envelope = new(PaperKind.B6Envelope, 693, 492);
        /// <summary>Italy envelope, 110 x 230 mm.</summary>
        public static readonly PaperSize ItalyEnvelope = new(PaperKind.ItalyEnvelope, 433, 906);
        /// <summary>Monarch envelope, 3 7/8 x 7 1/2 in.</summary>
        public static readonly PaperSize MonarchEnvelope = new(PaperKind.MonarchEnvelope, 387, 750);
        /// <summary>6 3/4 envelope, 3 5/8 x 6 1/2 in.</summary>
        public static readonly PaperSize PersonalEnvelope = new(PaperKind.PersonalEnvelope, 362, 650);
        /// <summary>US standard fanfold, 14 7/8 x 11 in.</summary>
        public static readonly PaperSize USStandardFanfold = new(PaperKind.USStandardFanfold, 1487, 1100);
        /// <summary>German standard fanfold, 8 1/2 x 12 in.</summary>
        public static readonly PaperSize GermanStandardFanfold = new(PaperKind.GermanStandardFanfold, 850, 1200);
        /// <summary>German legal fanfold, 8 1/2 x 13 in.</summary>
        public static readonly PaperSize GermanLegalFanfold = new(PaperKind.GermanLegalFanfold, 850, 1300);
        /// <summary>B4 (ISO), 250 x 353 mm.</summary>
        public static readonly PaperSize IsoB4 = new(PaperKind.IsoB4, 984, 1390);
        /// <summary>Japanese postcard, 100 x 148 mm.</summary>
        public static readonly PaperSize JapanesePostcard = new(PaperKind.JapanesePostcard, 394, 583);
        /// <summary>Standard, 9 x 11 in.</summary>
        public static readonly PaperSize Standard9x11 = new(PaperKind.Standard9x11, 900, 1100);
        /// <summary>Standard, 10 x 11 in.</summary>
        public static readonly PaperSize Standard10x11 = new(PaperKind.Standard10x11, 1000, 1100);
        /// <summary>Standard, 15 x 11 in.</summary>
        public static readonly PaperSize Standard15x11 = new(PaperKind.Standard15x11, 1500, 1100);
        /// <summary>Invitation envelope, 220 x 220 mm.</summary>
        public static readonly PaperSize InviteEnvelope = new(PaperKind.InviteEnvelope, 866, 866);
        /// <summary>Letter extra, 9 1/2 x 12 in.</summary>
        public static readonly PaperSize LetterExtra = new(PaperKind.LetterExtra, 950, 1200);
        /// <summary>Legal extra, 9 1/2 x 15 in.</summary>
        public static readonly PaperSize LegalExtra = new(PaperKind.LegalExtra, 950, 1500);
        /// <summary>Tabloid extra, 11.69 x 18 in.</summary>
        public static readonly PaperSize TabloidExtra = new(PaperKind.TabloidExtra, 1169, 1800);
        /// <summary>A4 extra, 236 x 322 mm.</summary>
        public static readonly PaperSize A4Extra = new(PaperKind.A4Extra, 929, 1268);
        /// <summary>Letter transverse, 8 1/2 x 11 in.</summary>
        public static readonly PaperSize LetterTransverse = new(PaperKind.LetterTransverse, 850, 1100);
        /// <summary>A4 transverse, 210 x 297 mm.</summary>
        public static readonly PaperSize A4Transverse = new(PaperKind.A4Transverse, 827, 1169);
        /// <summary>Letter extra transverse, 9 1/2 x 12 in.</summary>
        public static readonly PaperSize LetterExtraTransverse = new(PaperKind.LetterExtraTransverse, 950, 1200);
        /// <summary>SuperA/SuperA/A4, 227 x 356 mm.</summary>
        public static readonly PaperSize APlus = new(PaperKind.APlus, 894, 1402);
        /// <summary>SuperB/SuperB/A3, 305 x 487 mm.</summary>
        public static readonly PaperSize BPlus = new(PaperKind.BPlus, 1201, 1917);
        /// <summary>Letter plus, 8 1/2 x 12.69 in.</summary>
        public static readonly PaperSize LetterPlus = new(PaperKind.LetterPlus, 850, 1269);
        /// <summary>A4 plus, 210 x 330 mm.</summary>
        public static readonly PaperSize A4Plus = new(PaperKind.A4Plus, 827, 1299);
        /// <summary>A5 transverse, 148 x 210 mm.</summary>
        public static readonly PaperSize A5Transverse = new(PaperKind.A5Transverse, 583, 827);
        /// <summary>B5 (JIS) transverse, 182 x 257 mm.</summary>
        public static readonly PaperSize B5Transverse = new(PaperKind.B5Transverse, 717, 1012);
        /// <summary>A3 extra, 322 x 445 mm.</summary>
        public static readonly PaperSize A3Extra = new(PaperKind.A3Extra, 1268, 1752);
        /// <summary>A5 extra, 174 x 235 mm.</summary>
        public static readonly PaperSize A5Extra = new(PaperKind.A5Extra, 685, 925);
        /// <summary>B5 (ISO) extra, 201 x 276 mm.</summary>
        public static readonly PaperSize B5Extra = new(PaperKind.B5Extra, 791, 1087);
        /// <summary>A2, 420 x 594 mm.</summary>
        public static readonly PaperSize A2 = new(PaperKind.A2, 1654, 2339);
        /// <summary>A3 transverse, 297 x 420 mm.</summary>
        public static readonly PaperSize A3Transverse = new(PaperKind.A3Transverse, 1169, 1654);
        /// <summary>A3 extra transverse, 322 x 445 mm.</summary>
        public static readonly PaperSize A3ExtraTransverse = new(PaperKind.A3ExtraTransverse, 1268, 1752);
        /// <summary>Japanese double postcard, 200 x 148 mm.</summary>
        public static readonly PaperSize JapaneseDoublePostcard = new(PaperKind.JapaneseDoublePostcard, 787, 583);
        /// <summary>A6, 105 x 148 mm.</summary>
        public static readonly PaperSize A6 = new(PaperKind.A6, 413, 583);
        /// <summary>Japanese Kaku #2 envelope, 240 x 332 mm.</summary>
        public static readonly PaperSize JapaneseEnvelopeKakuNumber2 = new(PaperKind.JapaneseEnvelopeKakuNumber2, 945, 1307);
        /// <summary>Japanese Kaku #3 envelope, 216 x 277 mm.</summary>
        public static readonly PaperSize JapaneseEnvelopeKakuNumber3 = new(PaperKind.JapaneseEnvelopeKakuNumber3, 850, 1091);
        /// <summary>Japanese Chou #3 envelope, 120 x 235 mm.</summary>
        public static readonly PaperSize JapaneseEnvelopeChouNumber3 = new(PaperKind.JapaneseEnvelopeChouNumber3, 472, 925);
        /// <summary>Japanese Chou #4 envelope, 90 x 205 mm.</summary>
        public static readonly PaperSize JapaneseEnvelopeChouNumber4 = new(PaperKind.JapaneseEnvelopeChouNumber4, 354, 807);
        /// <summary>Letter rotated, 11 x 8 1/2 in.</summary>
        public static readonly PaperSize LetterRotated = new(PaperKind.LetterRotated, 1100, 850);
        /// <summary>A3 rotated, 420 x 297 mm.</summary>
        public static readonly PaperSize A3Rotated = new(PaperKind.A3Rotated, 1654, 1169);
        /// <summary>A4 rotated, 297 x 210 mm.</summary>
        public static readonly PaperSize A4Rotated = new(PaperKind.A4Rotated, 1169, 827);
        /// <summary>A5 rotated, 210 x 148 mm.</summary>
        public static readonly PaperSize A5Rotated = new(PaperKind.A5Rotated, 827, 583);
        /// <summary>B4 (JIS) rotated, 364 x 257 mm.</summary>
        public static readonly PaperSize B4JisRotated = new(PaperKind.B4JisRotated, 1433, 1012);
        /// <summary>B5 (JIS) rotated, 257 x 182 mm.</summary>
        public static readonly PaperSize B5JisRotated = new(PaperKind.B5JisRotated, 1012, 717);
        /// <summary>Japanese postcard rotated, 148 x 100 mm.</summary>
        public static readonly PaperSize JapanesePostcardRotated = new(PaperKind.JapanesePostcardRotated, 583, 394);
        /// <summary>Japanese double postcard rotated, 148 x 200 mm.</summary>
        public static readonly PaperSize JapaneseDoublePostcardRotated = new(PaperKind.JapaneseDoublePostcardRotated, 583, 787);
        /// <summary>A6 rotated, 148 x 105 mm.</summary>
        public static readonly PaperSize A6Rotated = new(PaperKind.A6Rotated, 583, 413);
        /// <summary>Japanese Kaku #2 envelope rotated, 332 x 240 mm.</summary>
        public static readonly PaperSize JapaneseEnvelopeKakuNumber2Rotated = new(PaperKind.JapaneseEnvelopeKakuNumber2Rotated, 1307, 945);
        /// <summary>Japanese Kaku #3 envelope rotated, 277 x 216 mm.</summary>
        public static readonly PaperSize JapaneseEnvelopeKakuNumber3Rotated = new(PaperKind.JapaneseEnvelopeKakuNumber3Rotated, 1091, 850);
        /// <summary>Japanese Chou #3 envelope rotated, 235 x 120 mm.</summary>
        public static readonly PaperSize JapaneseEnvelopeChouNumber3Rotated = new(PaperKind.JapaneseEnvelopeChouNumber3Rotated, 925, 472);
        /// <summary>Japanese Chou #4 envelope rotated, 205 x 90 mm.</summary>
        public static readonly PaperSize JapaneseEnvelopeChouNumber4Rotated = new(PaperKind.JapaneseEnvelopeChouNumber4Rotated, 807, 354);
        /// <summary>B6 (JIS), 128 x 182 mm.</summary>
        public static readonly PaperSize B6Jis = new(PaperKind.B6Jis, 504, 717);
        /// <summary>B6 (JIS) rotated, 182 x 128 mm.</summary>
        public static readonly PaperSize B6JisRotated = new(PaperKind.B6JisRotated, 717, 504);
        /// <summary>Standard, 12 x 11 in.</summary>
        public static readonly PaperSize Standard12x11 = new(PaperKind.Standard12x11, 1200, 1100);
        /// <summary>Japanese You #4 envelope, 105 x 235 mm.</summary>
        public static readonly PaperSize JapaneseEnvelopeYouNumber4 = new(PaperKind.JapaneseEnvelopeYouNumber4, 413, 925);
        /// <summary>Japanese You #4 envelope rotated, 235 x 105 mm.</summary>
        public static readonly PaperSize JapaneseEnvelopeYouNumber4Rotated = new(PaperKind.JapaneseEnvelopeYouNumber4Rotated, 925, 413);
        /// <summary>PRC 16K, 146 x 215 mm.</summary>
        public static readonly PaperSize Prc16K = new(PaperKind.Prc16K, 575, 846);
        /// <summary>PRC 32K, 97 x 151 mm.</summary>
        public static readonly PaperSize Prc32K = new(PaperKind.Prc32K, 382, 594);
        /// <summary>PRC 32K (big), 97 x 151 mm.</summary>
        public static readonly PaperSize Prc32KBig = new(PaperKind.Prc32KBig, 382, 594);
        /// <summary>PRC #1 envelope, 102 x 165 mm.</summary>
        public static readonly PaperSize PrcEnvelopeNumber1 = new(PaperKind.PrcEnvelopeNumber1, 402, 650);
        /// <summary>PRC #2 envelope, 102 x 176 mm.</summary>
        public static readonly PaperSize PrcEnvelopeNumber2 = new(PaperKind.PrcEnvelopeNumber2, 402, 693);
        /// <summary>PRC #3 envelope, 125 x 176 mm.</summary>
        public static readonly PaperSize PrcEnvelopeNumber3 = new(PaperKind.PrcEnvelopeNumber3, 492, 693);
        /// <summary>PRC #4 envelope, 110 x 208 mm.</summary>
        public static readonly PaperSize PrcEnvelopeNumber4 = new(PaperKind.PrcEnvelopeNumber4, 433, 819);
        /// <summary>PRC #5 envelope, 110 x 220 mm.</summary>
        public static readonly PaperSize PrcEnvelopeNumber5 = new(PaperKind.PrcEnvelopeNumber5, 433, 866);
        /// <summary>PRC #6 envelope, 120 x 230 mm.</summary>
        public static readonly PaperSize PrcEnvelopeNumber6 = new(PaperKind.PrcEnvelopeNumber6, 472, 906);
        /// <summary>PRC #7 envelope, 160 x 230 mm.</summary>
        public static readonly PaperSize PrcEnvelopeNumber7 = new(PaperKind.PrcEnvelopeNumber7, 630, 906);
        /// <summary>PRC #8 envelope, 120 x 309 mm.</summary>
        public static readonly PaperSize PrcEnvelopeNumber8 = new(PaperKind.PrcEnvelopeNumber8, 472, 1217);
        /// <summary>PRC #9 envelope, 229 x 324 mm.</summary>
        public static readonly PaperSize PrcEnvelopeNumber9 = new(PaperKind.PrcEnvelopeNumber9, 902, 1276);
        /// <summary>PRC #10 envelope, 324 x 458 mm.</summary>
        public static readonly PaperSize PrcEnvelopeNumber10 = new(PaperKind.PrcEnvelopeNumber10, 1276, 1803);
        /// <summary>PRC 16K rotated, 215 x 146 mm.</summary>
        public static readonly PaperSize Prc16KRotated = new(PaperKind.Prc16KRotated, 846, 575);
        /// <summary>PRC 32K rotated, 151 x 97 mm.</summary>
        public static readonly PaperSize Prc32KRotated = new(PaperKind.Prc32KRotated, 594, 382);
        /// <summary>PRC 32K (big) rotated, 151 x 97 mm.</summary>
        public static readonly PaperSize Prc32KBigRotated = new(PaperKind.Prc32KBigRotated, 594, 382);
        /// <summary>PRC #1 envelope rotated, 165 x 102 mm.</summary>
        public static readonly PaperSize PrcEnvelopeNumber1Rotated = new(PaperKind.PrcEnvelopeNumber1Rotated, 650, 402);
        /// <summary>PRC #2 envelope rotated, 176 x 102 mm.</summary>
        public static readonly PaperSize PrcEnvelopeNumber2Rotated = new(PaperKind.PrcEnvelopeNumber2Rotated, 693, 402);
        /// <summary>PRC #3 envelope rotated, 176 x 125 mm.</summary>
        public static readonly PaperSize PrcEnvelopeNumber3Rotated = new(PaperKind.PrcEnvelopeNumber3Rotated, 693, 492);
        /// <summary>PRC #4 envelope rotated, 208 x 110 mm.</summary>
        public static readonly PaperSize PrcEnvelopeNumber4Rotated = new(PaperKind.PrcEnvelopeNumber4Rotated, 819, 433);
        /// <summary>PRC #5 envelope rotated, 220 x 110 mm.</summary>
        public static readonly PaperSize PrcEnvelopeNumber5Rotated = new(PaperKind.PrcEnvelopeNumber5Rotated, 866, 433);
        /// <summary>PRC #6 envelope rotated, 230 x 120 mm.</summary>
        public static readonly PaperSize PrcEnvelopeNumber6Rotated = new(PaperKind.PrcEnvelopeNumber6Rotated, 906, 472);
        /// <summary>PRC #7 envelope rotated, 230 x 160 mm.</summary>
        public static readonly PaperSize PrcEnvelopeNumber7Rotated = new(PaperKind.PrcEnvelopeNumber7Rotated, 906, 630);
        /// <summary>PRC #8 envelope rotated, 309 x 120 mm.</summary>
        public static readonly PaperSize PrcEnvelopeNumber8Rotated = new(PaperKind.PrcEnvelopeNumber8Rotated, 1217, 472);
        /// <summary>PRC #9 envelope rotated, 324 x 229 mm.</summary>
        public static readonly PaperSize PrcEnvelopeNumber9Rotated = new(PaperKind.PrcEnvelopeNumber9Rotated, 1276, 902);
        /// <summary>PRC #10 envelope rotated, 458 x 324 mm.</summary>
        public static readonly PaperSize PrcEnvelopeNumber10Rotated = new(PaperKind.PrcEnvelopeNumber10Rotated, 1803, 1276);
    }
}
