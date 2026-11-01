namespace Aspose.Pdf.Text;

/// <summary>
/// One of the CJK faces a PDF may name without embedding its program (ISO 32000-1 9.7.4):
/// its character collection, the metrics its font descriptor gives, and the advances of the
/// CIDs that are not one em wide, written as a /W array (9.7.4.3) - a start CID followed by a
/// bracketed list of widths, or a first and last CID that share one width.
/// </summary>
internal sealed record PredefinedCidFace(
    string Name, string Ordering, int Supplement, string UnicodeCMap,
    int Flags, int[] FontBBox, int Ascent, int Descent, int CapHeight, int XHeight, int StemV,
    string Widths, (string Key, int Value)[] Extra);

/// <summary>The predefined CJK faces: the Japanese, Simplified and Traditional Chinese and
/// Korean faces of Adobe's Asian font packs, with their published metrics.</summary>
internal static class PredefinedCidFaces
{
    private const string Japan1 = "Japan1";
    private const string Gb1 = "GB1";
    private const string Cns1 = "CNS1";
    private const string Korea1 = "Korea1";

    // Font descriptor flags (ISO 32000-1 9.8.2, Table 123): a serif face is Serif | Symbolic.
    private const int Symbolic = 4;
    private const int SerifSymbolic = 6;

    private const int CjkLeading = 148;
    private const int EmWidth = 1000;
    private const int HalfEm = 500;

    private static (string Key, int Value)[] ChineseKoreanExtra(int stemH) =>
        [("StemH", stemH), ("Leading", CjkLeading), ("MaxWidth", EmWidth), ("MissingWidth", HalfEm)];

    public static readonly PredefinedCidFace[] All =
    [
        new("HeiseiMin-W3", Japan1, 2, "UniJIS-UCS2-H", SerifSymbolic, [-123, -257, 1001, 910], 723, -241, 709, 450, 69,
            "1 [250 333 408 500] 5 [500 833 778 180 333] 10 [333 500 564 250 333 250 278 500] 18 26 500 27 28 278 " +
            "29 31 564 32 [444 921 722 667] 36 [667 722 611 556 722] 41 [722 333 389 722 611 889 722] " +
            "48 [722 556 722 667 556 611 722] 55 [722 944 722] 58 [722 611 333 500 333 469 500 333 444 500 444 500 444 333 500] " +
            "73 [500 278] 75 [278 500 278 778 500] 80 82 500 83 [333 389 278 500] 87 [500 722 500] " +
            "90 [500 444 480 200 480 333] 97 [278] 99 [200] 101 [333 500] 103 [500 167] 107 [500] 109 [500 333] " +
            "111 [333 556] 113 [556 500] 117 [250] 119 [350 333 444] 123 [500] 126 [444 333] 128 137 333 " +
            "138 [1000 889 276 611 722 889 310 667 278] 147 [278 500 722 500 564 760 564 760] 157 158 300 " +
            "159 [500 300 750] 162 163 750 164 169 722 170 [667 611] 172 174 611 175 178 333 179 185 722 " +
            "187 191 722 192 [556 444] 194 203 444 204 207 278 208 214 500 216 222 500 " +
            "223 [556 722 611 500 389 980 444] 231 [500] 323 [500] 325 [500] 327 389 500",
            []),
        new("HeiseiKakuGo-W5", Japan1, 2, "UniJIS-UCS2-H", Symbolic, [-92, -250, 1010, 922], 752, -221, 737, 553, 114,
            "1 [277 305 500 668 668 906 727 305 445 445 508 668 305 379 305 539] 17 26 668 " +
            "27 [305 305 668 668 668 566 871 727 637 652 699 574 555 676 687 242 492 664 582 789 707 734 582 734 605 605 " +
            "641 668 727 945 609 609 574 445 668 445 668 668 590 555 609 547 602 574 391 609 582 234 277 539 234 895 582 " +
            "605 602 602 387 508 441 582 562 781 531 570 555 449 246 449 668] 231 632 500",
            [("StemH", 0)]),
        new("STSong-Light", Gb1, 0, "UniGB-UCS2-H", SerifSymbolic, [-25, -254, 1000, 880], 752, -271, 737, 553, 58,
            "1 [207 270 342 467 462 797 710 239 374] 10 [374 423 605 238 375 238 334 462] 18 26 462 27 28 238 " +
            "29 31 605 32 [344 748 684 560 695 739 563 511 729 793 318 312 666 526 896 758 772 544 772 628 465 607 " +
            "753 711 972 647 620 607 374 333 374 606 500 239 417 503 427 529 415 264 444 518 241 230 495 228 793 " +
            "527 524] 81 [524 504 338 336 277 517 450 652 466 452 407 370 258 370 605]",
            ChineseKoreanExtra(91)),
        new("MSung-Light", Cns1, 1, "UniCNS-UCS2-H", SerifSymbolic, [-160, -249, 1015, 888], 752, -271, 737, 553, 58,
            "1 2 250 3 [408 668 490 875 698 250 240] 10 [240 417 667 250 313 250 520 500] 18 26 500 27 28 250 " +
            "29 31 667 32 [396 921 677 615 719 760 625 552 771 802 354] 43 [354 781 604 927 750 823 563 823 729 542 " +
            "698 771 729 948 771 677 635 344 520 344 469 500 250 469 521 427 521 438 271 469 531 250] " +
            "75 [250 458 240 802 531 500 521] 82 [521 365 333 292 521 458 677 479 458 427 480 496 480 667]",
            ChineseKoreanExtra(45)),
        new("HYSMyeongJo-Medium", Korea1, 1, "UniKS-UCS2-H", SerifSymbolic, [0, -148, 1001, 880], 752, -271, 737, 553, 58,
            "1 [333 416] 3 [416 833 625 916 833 250 500] 10 11 500 12 [833 291 833 291 375 625] 18 26 625 27 28 333 " +
            "29 30 833 31 [916 500 1000 791 708] 36 [708 750 708 666 750 791 375 500 791 666 916 791 750 666 750 708 " +
            "666 791] 54 [791 750 1000 708] 58 [708 666 500 375 500] 63 64 500 65 [333 541 583 541 583] " +
            "70 [583 375 583] 73 [583 291 333 583 291 875 583] 80 82 583 83 [458 541 375 583] 87 [583 833 625] " +
            "90 [625 500 583] 93 94 583 95 [750]",
            [.. ChineseKoreanExtra(91), ("AvgWidth", HalfEm)]),
        new("HYGothic-Medium", Korea1, 1, "UniKS-UCS2-H", SerifSymbolic, [-6, -145, 1003, 880], 752, -142, 737, 553, 58,
            "1 94 500",
            ChineseKoreanExtra(0)),
    ];
}
