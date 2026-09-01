using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Stamps;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

public sealed partial class HeaderFooter
{
    /// <summary>Render the report header's DATA REGION: nested inline-block percentage
    /// columns of label/value rows (labels bold, right-aligned in the box their css
    /// gives them), background bands, checkbox rows, and grey-framed fieldsets whose
    /// legend rides the frame. Returns the height
    /// consumed; draw:false only measures (columns bottom-align on the tallest sibling).</summary>
    // ── report-dialect geometry ─────────────────────────────────────────────
    // Every value is an empirical constant of this band layout, holding
    // wherever the document repeats the shape.
    // Units: Pt = points, Em = fraction of the run's font size,
    // Frac = fraction of the enclosing box's width.
    /// <summary>Fieldset geometry of a report region: legend drop above the box, legend-to-first-row
    /// pitch, bottom pad, gap to the next element, and the check-row pitch (points).</summary>
    private const double FsLegendDrop = 4.41, FsLegendToRow = 16.16;
    private const double FsPadBottom = 10.4, FsGap = 6.0, CheckRowPitch = 14.34;
    private const double RptFontPt = 9.75;            // div { font-size: small } = 13 css px

    private const double RptRowPitchPt = 12.75;       // the small face's 17 css px normal line

    private const double RptSpaceEm = 0.278;          // one collapsed space between inline blocks

    private const double RptLabelMarginEm = 0.5;      // label { margin-right: .5em }

    private const double RptLabelBoxFrac = 0.40;      // label { width: 40% }

    private const double RptFieldsetLabelFrac = 0.30; // fieldset label { width: 30% }

    private const double RptBandDescentPt = 2.34;     // a row's background rect reaches this far under the baseline

    private const double RptCheckboxIndentPt = 4.1;   // column left → checkbox square

    private const double RptCheckboxSizePt = 7.75;    // the square's white fill side

    private const double RptCheckboxRisePt = 0.55;    // square bottom above the row baseline

    private const double RptCheckboxLabelGapPt = 5.6;  // square → its label: the source's

                                                       // own space plus the inline gap —
                                                       // the same gap for every label
    private const double RptCheckboxGray = 0.4;       // the square's stroke shade

    private const double RptFrameInsetPt = 1.5;       // region left → fieldset frame

    private const double RptFieldsetPadPt = 8.35;     // region left → fieldset content

    private const double RptLegendInsetPt = 9.85;     // region left → legend text

    private const double RptFrameOverhangPt = 13.7;   // frame width beyond the fieldset's percentage box

    private const double RptLegendGapPt = 2.0;        // the frame's top edge breaks this far around the legend

    private const double RptFrameGray = 0.502;        // #808080, the fieldset frame shade

    private const double RptStrokePt = 0.75;          // 1 css px

    private const double RptH5FontPt = 9.96;          // UA h5: 0.83 em of the 12 pt base

    private const double RptH3FontPt = 14.04;         // UA h3: 1.17 em of the 12 pt base

    private const double RptH5BasePt = 10.87;         // band top → the h5 line's baseline

    private const double RptH5ToH3Pt = 17.82;         // h5 baseline → first h3 baseline

    private const double RptH3PitchPt = 18.75;        // h3 baseline → next h3 baseline

    private const double RptH3ToRegionPt = 28.01;     // last h3 baseline → first data-row baseline

    private const double RptBandLeftPt = 90.0;        // the band's own default left — the header

    // Segoe UI ASCII advances (fraction of em, glyphs 32..126), read once from the
    // face's own tables — the report dialect measures with real Segoe UI
    // metrics, so right-aligned and centred runs anchor at their true metric
    // positions ("Source:" measures 33.80 at 9.75). The
    // Standard-14 twin only supplies the drawn glyphs.
    private static readonly double[] SegoeAdvances =
    {
        0.2739, 0.2842, 0.3921, 0.5908, 0.5391, 0.8184, 0.8003, 0.2300, 0.3018, 0.3018,
        0.4170, 0.6841, 0.2168, 0.3999, 0.2168, 0.3896, 0.5391, 0.5391, 0.5391, 0.5391,
        0.5391, 0.5391, 0.5391, 0.5391, 0.5391, 0.5391, 0.2168, 0.2168, 0.6841, 0.6841,
        0.6841, 0.4482, 0.9551, 0.6450, 0.5732, 0.6191, 0.7012, 0.5059, 0.4883, 0.6860,
        0.7100, 0.2661, 0.3569, 0.5801, 0.4707, 0.8979, 0.7480, 0.7539, 0.5601, 0.7539,
        0.5981, 0.5312, 0.5239, 0.6870, 0.6211, 0.9341, 0.5898, 0.5527, 0.5703, 0.3018,
        0.3789, 0.3018, 0.6841, 0.4150, 0.2681, 0.5088, 0.5879, 0.4619, 0.5889, 0.5229,
        0.3130, 0.5889, 0.5659, 0.2422, 0.2422, 0.4971, 0.2422, 0.8613, 0.5659, 0.5859,
        0.5879, 0.5889, 0.3477, 0.4243, 0.3389, 0.5659, 0.4790, 0.7227, 0.4590, 0.4839,
        0.4521, 0.3018, 0.2393, 0.3018, 0.6841,
    };

    private static readonly double[] SegoeBoldAdvances =
    {
        0.2759, 0.3271, 0.4932, 0.5923, 0.5752, 0.8672, 0.8496, 0.2930, 0.3691, 0.3691,
        0.4551, 0.7070, 0.2710, 0.4043, 0.2710, 0.4434, 0.5752, 0.5752, 0.5752, 0.5752,
        0.5752, 0.5752, 0.5752, 0.5752, 0.5752, 0.5752, 0.2710, 0.2710, 0.7070, 0.7070,
        0.7070, 0.4380, 0.9541, 0.7031, 0.6411, 0.6240, 0.7373, 0.5322, 0.5200, 0.7109,
        0.7661, 0.3169, 0.4453, 0.6489, 0.5112, 0.9570, 0.7900, 0.7583, 0.6143, 0.7583,
        0.6528, 0.5605, 0.5859, 0.7231, 0.6670, 1.0049, 0.6553, 0.6069, 0.6069, 0.3691,
        0.4360, 0.3691, 0.7070, 0.4150, 0.3140, 0.5381, 0.6201, 0.4800, 0.6191, 0.5410,
        0.3833, 0.6191, 0.6021, 0.2842, 0.2842, 0.5591, 0.2842, 0.9160, 0.6050, 0.6113,
        0.6201, 0.6191, 0.3979, 0.4399, 0.3892, 0.6050, 0.5420, 0.7974, 0.5522, 0.5381,
        0.4790, 0.3691, 0.3262, 0.3691, 0.7070,
    };

    internal static byte[]? SegoeReportTtf(bool bold)
    {
        if (!_segoeProbed)
        {
            _segoeProbed = true;
            // By FILE NAME through the platform's font directories: the literal Windows
            // path resolves to nothing elsewhere, and the header text this face carries
            // then went missing on a box that has the very same font installed.
            _segoeTtf = ReadSystemFontFile("segoeui.ttf");
            _segoeBoldTtf = ReadSystemFontFile("segoeuib.ttf");
        }
        return bold ? _segoeBoldTtf : _segoeTtf;
    }

    /// <summary>Read a system font by FILE NAME, wherever this platform keeps its fonts.
    /// Null when the face is not installed, which the caller already treats as "use the
    /// ordinary face".</summary>
    private static byte[]? ReadSystemFontFile(string fileName)
    {
        try
        {
            return Aspose.Pdf.Text.SystemFontResolver.FindFontFile(fileName) is { } path
                ? System.IO.File.ReadAllBytes(path)
                : null;
        }
        catch { return null; }
    }

    /// <summary>Draw report text in the report's own face: the real Segoe UI
    /// embedded as a Type0 subset when the system provides it — exact shapes AND
    /// advances. Without it, Standard-14 glyphs anchor per character on the baked
    /// Segoe metrics, so the drawn shapes never drift more than one glyph's width
    /// from the true Segoe ink.</summary>
    private static void DrawReportWords(ContentStreamBuilder b, string res, string text,
        double x, double yPdf, double fs, bool bold, Page? page = null)
    {
        if (page is not null && SegoeReportTtf(bold) is { } segTtf)
        {
            // per WORD: Segoe kerns inside words, the Type0 embed does not —
            // anchoring each word at its metric position keeps the drift inside one
            // word's kerning, visually negligible
            var fd = ResolvePageFontDict(page);
            var wx2 = x;
            foreach (var word in text.Split(' '))
            {
                if (word.Length > 0)
                {
                    var (rn, hex) = Aspose.Pdf.Text.Type0FontEmbedder.Embed(
                        fd, segTtf, bold ? "SegoeUIBold" : "SegoeUI", word,
                        stripSpacesInBaseFont: true);
                    b.BeginText().SetFont(rn, fs)
                     .SetTextMatrix(1, 0, 0, 1, wx2, yPdf)
                     .ShowTextHexKerned(hex, KernAdjustments(word, bold)).EndText();
                }
                wx2 += MeasureReportText(word + " ", fs, bold);
            }
            return;
        }
        var cx = x;
        b.BeginText().SetFont(res, fs);
        foreach (var ch in text)
        {
            if (ch != ' ')
                b.SetTextMatrix(1, 0, 0, 1, cx, yPdf).ShowText(ch.ToString());
            cx += MeasureReportText(ch.ToString(), fs, bold);
        }
        b.EndText();
    }

    // Segoe UI kern pairs (em fractions, from the faces' own kern tables): Segoe
    // text kerns inside words, so both the measure and the drawn TJ runs
    // apply these. Format: two characters then the signed em value.
    private static readonly string SegoeKernData =
        "'r-0.0249;'s-0.0322;(j+0.1138;*A-0.0811;*J-0.0752;*c-0.0498;*d-0.0498;*e-0.0498;*g-0.0498;"
        + "*o-0.0498;*q-0.0498;A*-0.0630;A,+0.0332;AC-0.0132;AG-0.0132;AJ+0.0459;AO-0.0132;AT-0.0718;"
        + "AU-0.0132;AV-0.0571;AW-0.0361;AY-0.0762;AZ+0.0288;At-0.0132;Av-0.0210;Aw-0.0132;Ay-0.0181;"
        + "BT-0.0449;BY-0.0322;C?+0.0010;CC-0.0269;CG-0.0269;CO-0.0132;CQ-0.0269;D,-0.0630;D.-0.0630;"
        + "DA-0.0161;DT-0.0449;DX-0.0259;DZ-0.0239;EA+0.0049;EJ+0.0332;ET+0.0020;EW+0.0142;EX+0.0039;"
        + "F,-0.0752;F.-0.0752;FA-0.0649;FJ-0.0322;FS-0.0132;FT+0.0068;Fa-0.0371;Ff+0.0049;GT-0.0239;"
        + "GV-0.0132;Gy-0.0132;J,-0.0498;J.-0.0498;JA-0.0181;JJ-0.0322;Ja-0.0132;K,+0.0190;KC-0.0439;"
        + "KG-0.0439;KJ+0.0439;KO-0.0439;KQ-0.0439;KX+0.0181;KZ+0.0190;Kc-0.0132;Kd-0.0132;Ke-0.0132;"
        + "Kg-0.0132;Ko-0.0132;Kq-0.0132;Kt-0.0229;Kv-0.0361;Kw-0.0259;Ky-0.0449;L*-0.1011;L?-0.0498;"
        + "LA+0.0288;LC-0.0322;LG-0.0322;LJ+0.0488;LO-0.0342;LQ-0.0342;LT-0.0552;LU-0.0142;LV-0.0571;"
        + "LW-0.0239;LY-0.0630;LZ+0.0288;Lt-0.0132;Lv-0.0498;Lw-0.0322;Ly-0.0371;O,-0.0449;O.-0.0449;"
        + "OA-0.0132;OJ-0.0049;OT-0.0449;OX-0.0181;OY-0.0122;OZ-0.0239;P,-0.1592;P.-0.1592;PA-0.0771;"
        + "PG-0.0049;PJ-0.0630;PW+0.0190;PX-0.0298;Pa-0.0322;Pc-0.0371;Pd-0.0371;Pe-0.0371;Pg-0.0371;"
        + "Po-0.0371;Pq-0.0361;Q,-0.0449;Q.-0.0630;QA-0.0132;QT-0.0449;QX-0.0181;QY-0.0049;QZ-0.0239;"
        + "RC-0.0142;RG-0.0142;RJ+0.0278;RO-0.0098;RQ-0.0098;RT-0.0259;RY-0.0190;Rc-0.0259;Rd-0.0259;"
        + "Re-0.0278;Rg-0.0278;Ro-0.0288;Rq-0.0259;St-0.0322;Sv-0.0239;Sw-0.0132;Sy-0.0229;T,-0.0630;"
        + "T.-0.0879;T:-0.0112;TA-0.0752;TC-0.0449;TG-0.0449;TJ-0.0552;TO-0.0449;TQ-0.0449;TT+0.0190;"
        + "TV+0.0210;TW+0.0190;TX-0.0029;TY+0.0142;Ta-0.1060;Tc-0.1030;Td-0.1030;Te-0.1030;Tf-0.0469;"
        + "Tg-0.1030;Tm-0.0869;Tn-0.0869;To-0.1030;Tp-0.0869;Tq-0.1030;Tr-0.0869;Ts-0.0752;Tu-0.0869;"
        + "Tv-0.0498;Tw-0.0552;Tx-0.0879;Ty-0.0552;Tz-0.0630;UA-0.0200;V,-0.1001;V.-0.1118;VA-0.0571;"
        + "VC-0.0210;VG-0.0210;VJ-0.0342;VO-0.0059;VQ-0.0210;VS-0.0132;VT+0.0190;Va-0.0718;Vc-0.0630;"
        + "Vd-0.0630;Ve-0.0630;Vg-0.0630;Vm-0.0371;Vn-0.0371;Vo-0.0630;Vp-0.0371;Vq-0.0630;Vr-0.0371;"
        + "Vs-0.0322;Vu-0.0371;W,-0.0571;W.-0.0630;WA-0.0361;WT+0.0190;Wa-0.0371;Wc-0.0239;Wd-0.0239;"
        + "We-0.0239;Wg-0.0239;Wo-0.0239;Wq-0.0239;X,+0.0332;X.+0.0278;XC-0.0112;XG-0.0112;XJ+0.0469;"
        + "XO-0.0112;XQ-0.0112;XT+0.0161;Y,-0.0859;Y.-0.0952;YA-0.0771;YC-0.0220;YG-0.0220;YJ-0.0322;"
        + "YO-0.0220;YQ-0.0220;YS-0.0132;YT+0.0190;Ya-0.0972;Yc-0.0879;Yd-0.0879;Ye-0.0879;Yf-0.0132;"
        + "Yg-0.0879;Ym-0.0688;Yn-0.0688;Yo-0.0879;Yp-0.0688;Yq-0.0879;Yr-0.0688;Ys-0.0649;Yu-0.0688;"
        + "ZJ+0.0400;ZT+0.0190;Zy-0.0259;[j+0.1138;ba-0.0132;bf-0.0049;bx-0.0122;cJ+0.0342;cT-0.0498;"
        + "cY-0.0371;e'-0.0508;f)+0.0688;f,-0.0630;f--0.0498;f.-0.0630;f:+0.0400;f?+0.0322;f]+0.0688;"
        + "fb+0.0088;fh+0.0088;ft+0.0181;fv+0.0190;fw+0.0190;fx+0.0088;fy+0.0161;f}+0.0400;gj+0.0229;"
        + "jj+0.0171;k,+0.0400;k--0.0679;k.+0.0400;k:+0.0400;kc-0.0200;kd-0.0132;ke-0.0200;kg-0.0200;"
        + "ko-0.0200;kq-0.0132;kt-0.0078;n'-0.0508;o'-0.0708;oa-0.0132;of-0.0181;ox-0.0122;pa-0.0132;"
        + "pf-0.0181;px-0.0122;qj+0.0498;r,-0.0771;r--0.0630;r.-0.0830;r:+0.0400;rc-0.0132;rd-0.0132;"
        + "re-0.0132;rf+0.0190;rg-0.0132;rm-0.0020;rn-0.0020;ro-0.0132;rq-0.0132;rs+0.0068;rt+0.0288;"
        + "rv+0.0400;rw+0.0400;rx+0.0288;ry+0.0400;rz+0.0190;t--0.0552;t?-0.0259;tc-0.0132;td-0.0132;"
        + "te-0.0078;tg-0.0078;to-0.0078;tq-0.0078;tx+0.0142;u'-0.0322;v,-0.0571;v.-0.0630;va-0.0181;"
        + "vc-0.0059;vd-0.0078;ve-0.0059;vg-0.0059;vo-0.0059;vq-0.0078;w,-0.0439;w.-0.0498;wc-0.0029;"
        + "wd-0.0049;we-0.0049;wg-0.0029;wo-0.0029;wq-0.0049;xc-0.0078;xd-0.0078;xe-0.0078;xg-0.0078;"
        + "xo-0.0078;xq-0.0078;y'+0.0142;y,-0.0498;y.-0.0620;y?-0.0371;yc-0.0049;yd-0.0049;ye-0.0049;"
        + "yf+0.0020;yg-0.0049;yo-0.0049;yq-0.0049;yt+0.0029;{j+0.0991;";

    private static readonly string SegoeBoldKernData =
        "'r-0.0298;'s-0.0498;(j+0.0928;*A-0.0649;*J-0.0601;*c-0.0400;*d-0.0400;*e-0.0400;*g-0.0400;"
        + "*o-0.0400;*q-0.0400;A*-0.0601;A,+0.0288;AC-0.0151;AG-0.0098;AJ+0.0381;AO-0.0151;AT-0.0698;"
        + "AU-0.0171;AV-0.0552;AW-0.0322;AY-0.0752;AZ+0.0112;At-0.0200;Av-0.0220;Aw-0.0151;Ay-0.0200;"
        + "BT-0.0239;BY-0.0249;C?+0.0112;CC-0.0298;CG-0.0298;CO-0.0122;CQ-0.0220;D,-0.0498;D.-0.0498;"
        + "DA-0.0151;DT-0.0352;DX-0.0298;DZ-0.0200;EA+0.0142;EJ+0.0239;ET+0.0088;EV+0.0049;EW+0.0200;"
        + "EX+0.0200;F,-0.0698;F.-0.0698;FA-0.0542;FJ-0.0249;FS-0.0098;FT+0.0122;Fa-0.0298;Ff+0.0088;"
        + "GT-0.0200;GV-0.0098;Gy-0.0098;J,-0.0400;J.-0.0400;JA-0.0298;JJ-0.0249;Ja-0.0098;K,+0.0298;"
        + "K?+0.0112;KC-0.0288;KG-0.0288;KJ+0.0288;KO-0.0288;KQ-0.0288;KT+0.0049;KX+0.0200;KZ+0.0200;"
        + "Kc-0.0098;Kd-0.0098;Ke-0.0098;Kg-0.0098;Ko-0.0098;Kq-0.0098;Kt-0.0249;Kv-0.0352;Kw-0.0249;"
        + "Ky-0.0420;L*-0.1001;L?-0.0400;LA+0.0220;LC-0.0249;LG-0.0249;LJ+0.0278;LO-0.0249;LQ-0.0249;"
        + "LT-0.0659;LU-0.0200;LV-0.0571;LW-0.0352;LY-0.0708;LZ+0.0288;Lt-0.0098;Lv-0.0449;Lw-0.0298;"
        + "Ly-0.0352;O,-0.0498;O.-0.0391;OA-0.0151;OJ-0.0098;OT-0.0400;OX-0.0249;OY-0.0200;OZ-0.0200;"
        + "P,-0.1709;P.-0.1499;PA-0.0591;PG+0.0049;PJ-0.0659;PW+0.0171;PX-0.0220;Pa-0.0249;Pc-0.0298;"
        + "Pd-0.0298;Pe-0.0298;Pg-0.0298;Po-0.0298;Pq-0.0298;Q,-0.0391;Q.-0.0391;QA-0.0098;QT-0.0400;"
        + "QX-0.0200;QY-0.0151;QZ-0.0200;RC-0.0098;RG-0.0098;RJ+0.0239;RO-0.0098;RQ-0.0098;RT-0.0200;"
        + "RY-0.0098;Rc-0.0249;Rd-0.0249;Re-0.0249;Rg-0.0249;Ro-0.0249;Rq-0.0249;St-0.0249;Sv-0.0200;"
        + "Sw-0.0098;Sy-0.0249;T,-0.0708;T.-0.0908;T:-0.0088;TA-0.0698;TC-0.0352;TG-0.0352;TJ-0.0659;"
        + "TO-0.0371;TQ-0.0371;TT+0.0200;TV+0.0288;TW+0.0200;TX-0.0020;TY+0.0200;Ta-0.0850;Tc-0.0898;"
        + "Td-0.0898;Te-0.0898;Tf-0.0400;Tg-0.0898;Tm-0.0688;Tn-0.0688;To-0.0898;Tp-0.0640;Tq-0.0898;"
        + "Tr-0.0752;Ts-0.0752;Tu-0.0688;Tv-0.0400;Tw-0.0449;Tx-0.0698;Ty-0.0449;Tz-0.0391;UA-0.0220;"
        + "UJ-0.0181;V,-0.1001;V.-0.1001;V:-0.0200;V?+0.0078;VA-0.0518;VC-0.0200;VG-0.0200;VJ-0.0562;"
        + "VO-0.0020;VQ-0.0122;VS-0.0098;VT+0.0200;Va-0.0752;Vc-0.0649;Vd-0.0649;Ve-0.0649;Vg-0.0601;"
        + "Vm-0.0352;Vn-0.0298;Vo-0.0649;Vp-0.0352;Vq-0.0649;Vr-0.0352;Vs-0.0381;Vu-0.0269;W,-0.0601;"
        + "W.-0.0601;W:-0.0098;WA-0.0352;WT+0.0142;Wa-0.0400;Wc-0.0269;Wd-0.0269;We-0.0269;Wg-0.0269;"
        + "Wo-0.0269;Wq-0.0200;X,+0.0288;X.+0.0288;XC-0.0151;XG-0.0151;XJ+0.0332;XO-0.0151;XQ-0.0151;"
        + "XT+0.0200;Y,-0.1108;Y.-0.1108;YA-0.0752;YC-0.0249;YG-0.0249;YJ-0.0562;YO-0.0249;YQ-0.0249;"
        + "YS-0.0098;YT+0.0200;Ya-0.0898;Yc-0.0898;Yd-0.0898;Ye-0.0898;Yf-0.0151;Yg-0.0898;Ym-0.0649;"
        + "Yn-0.0649;Yo-0.0898;Yp-0.0669;Yq-0.0898;Yr-0.0649;Ys-0.0552;Yu-0.0649;ZJ+0.0239;ZT+0.0200;"
        + "Zy-0.0249;[j+0.0830;ba-0.0098;bf-0.0049;bx-0.0200;cJ+0.0342;cT-0.0400;cY-0.0298;e'-0.0698;"
        + "f)+0.0381;f*+0.0210;f,-0.0498;f--0.0400;f.-0.0498;f:+0.0400;f?+0.0298;f]+0.0381;fb+0.0151;"
        + "fh+0.0088;fk+0.0049;fl+0.0049;ft+0.0190;fv+0.0200;fw+0.0200;fx+0.0088;fy+0.0200;f}+0.0288;"
        + "gj+0.0088;jj+0.0142;k,+0.0400;k--0.0552;k.+0.0400;k:+0.0400;kc-0.0142;kd-0.0098;ke-0.0142;"
        + "kg-0.0142;ko-0.0142;kq-0.0098;kt-0.0059;kz+0.0078;n'-0.0601;o'-0.0801;oa-0.0098;of-0.0151;"
        + "oj-0.0020;ox-0.0200;pa-0.0098;pf-0.0151;px-0.0200;qj+0.0439;r,-0.0801;r--0.0498;r.-0.0801;"
        + "r:+0.0400;rc-0.0039;rd-0.0039;re-0.0039;rf+0.0249;rg-0.0039;rh+0.0029;ri+0.0039;rm+0.0029;"
        + "rn+0.0029;ro-0.0039;rq-0.0098;rs+0.0059;rt+0.0288;ru+0.0029;rv+0.0400;rw+0.0342;rx+0.0269;"
        + "ry+0.0400;rz+0.0200;t--0.0449;t?-0.0400;tc-0.0039;td-0.0039;te-0.0039;tg-0.0039;to-0.0039;"
        + "tq-0.0039;tx+0.0142;u'-0.0400;v,-0.0601;v.-0.0601;va-0.0151;vc-0.0068;vd-0.0068;ve-0.0098;"
        + "vg-0.0098;vo-0.0098;vq-0.0098;w,-0.0400;w.-0.0400;wc-0.0049;wd-0.0049;we-0.0049;wg-0.0049;"
        + "wo-0.0049;wq-0.0049;xc-0.0171;xd-0.0171;xe-0.0171;xg-0.0171;xo-0.0171;xq-0.0171;y'+0.0200;"
        + "y,-0.0552;y.-0.0552;y?+0.0010;yc-0.0098;yd-0.0098;ye-0.0098;yf+0.0078;yg-0.0098;yo-0.0098;"
        + "yq-0.0098;yt+0.0020;{j+0.0781;";

    private static Dictionary<int, double> SegoeKern(bool bold)
    {
        var cache = bold ? _segoeBoldKern : _segoeKern;
        if (cache is null)
        {
            cache = new Dictionary<int, double>();
            foreach (var e in (bold ? SegoeBoldKernData : SegoeKernData)
                     .Split(';', StringSplitOptions.RemoveEmptyEntries))
                cache[(e[0] << 8) | e[1]] = double.Parse(e[2..],
                    System.Globalization.CultureInfo.InvariantCulture);
            if (bold) _segoeBoldKern = cache; else _segoeKern = cache;
        }
        return cache;
    }

    /// <summary>Measure report-band text in the real Segoe UI face metrics.</summary>
    internal static double MeasureReportText(string t, double fs, bool bold)
    {
        if (t.Length == 0) return 0;
        var table = bold ? SegoeBoldAdvances : SegoeAdvances;
        var kern = SegoeKern(bold);
        var w = 0.0;
        for (var i = 0; i < t.Length; i++)
        {
            var c = t[i];
            w += c is >= (char)32 and < (char)127 ? table[c - 32]
                : c == ' ' ? table[0]
                : bold ? 0.6 : 0.55;
            if (i + 1 < t.Length && kern.TryGetValue((c << 8) | t[i + 1], out var kv))
                w += kv;
        }
        return w * fs;
    }

    /// <summary>TJ adjustments (thousandths of text space; positive moves the
    /// following glyphs left) for a word's internal kern pairs.</summary>
    private static double[] KernAdjustments(string word, bool bold)
    {
        var k = SegoeKern(bold);
        var adj = new double[Math.Max(0, word.Length - 1)];
        for (var i = 0; i + 1 < word.Length; i++)
            if (k.TryGetValue((word[i] << 8) | word[i + 1], out var v))
                adj[i] = -v * 1000.0;
        return adj;
    }

    /// <summary>Append kerned, word-anchored Segoe-embedded text ops for one line —
    /// the converter's report body draws through this. False when the face is
    /// unavailable (caller keeps its metric-anchored Standard-14 path).</summary>
    internal static bool TryAppendReportLineOps(System.Text.StringBuilder sb,
        Core.PdfDictionary fontDict, string line, double x, string yStr, double fs, bool bold)
    {
        if (SegoeReportTtf(bold) is not { } ttf) return false;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var wx = x;
        foreach (var word in line.Split(' '))
        {
            if (word.Length > 0)
            {
                var (rn, hex) = Aspose.Pdf.Text.Type0FontEmbedder.Embed(
                    fontDict, ttf, bold ? "SegoeUIBold" : "SegoeUI", word,
                    stripSpacesInBaseFont: true);
                sb.Append('/').Append(rn).Append(' ')
                  .Append(fs.ToString("F1", inv))
                  .Append(" Tf 1 0 0 1 ").Append(wx.ToString("F2", inv))
                  .Append(' ').Append(yStr).Append(" Tm [");
                var adj = KernAdjustments(word, bold);
                var seg = 0;
                void Flush(int endExcl)
                {
                    sb.Append('<');
                    for (var g = seg * 2; g < endExcl * 2 && g < hex.Length; g++)
                        sb.Append(hex[g].ToString("X2"));
                    sb.Append('>');
                    seg = endExcl;
                }
                var glyphs = hex.Length / 2;
                for (var i = 0; i + 1 < glyphs && i < adj.Length; i++)
                {
                    if (adj[i] == 0) continue;
                    Flush(i + 1);
                    sb.Append(adj[i].ToString("0.####", inv));
                }
                Flush(glyphs);
                sb.Append("] TJ ");
            }
            wx += MeasureReportText(word + " ", fs, bold);
        }
        return true;
    }

    private static double RenderReportRegion(Page? page, ContentStreamBuilder? b, string html,
        double x, double w, double yTopBase, bool inFieldset, string? boldRes, string? plainRes)
    {
        var rr = new ReportRegionState();
        rr.page = page;
        rr.b = b;
        rr.html = html;
        rr.x = x;
        rr.w = w;
        rr.yTopBase = yTopBase;
        rr.inFieldset = inFieldset;
        rr.boldRes = boldRes;
        rr.plainRes = plainRes;
        rr.fs = RptFontPt;
        rr.pitch = RptRowPitchPt;
        rr.draw = rr.page is not null && rr.b is not null;
        rr.rx = new System.Text.RegularExpressions.Regex(
            @"(?s)<(?<tag>div|fieldset)\b(?<attrs>[^>]*)>",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        rr.yBase = rr.yTopBase;
        rr.pos = 0;
        rr.pendingCols = new List<(string inner, double frac)>();

        // a fragment with no block children IS one leaf row (a column whose
        // content is a bare input+label pair, or bare text)
        if (!rr.rx.IsMatch(rr.html))
        {
            if (HtmlFragment.StripHtmlTags(rr.html).Trim().Length == 0
                && !rr.html.Contains("<input", StringComparison.OrdinalIgnoreCase))
                return 0;
            rr.html = "<div>" + rr.html + "</div>";
        }

        while (rr.pos < rr.html.Length)
        {
            if (!RenderReportElement(rr)) break;
        }
        FlushCols(rr);
        return rr.yBase - rr.yTopBase;
    }

    /// <summary>Renders the next element of the region's HTML - a fieldset, an inline column or a row - and advances past it; false at the end of the markup.</summary>
    private static bool RenderReportElement(ReportRegionState rr)
    {
        var m = rr.rx.Match(rr.html, rr.pos);
        if (!m.Success) return false;
        // balanced end of this element
        var depth = 1;
        var scan = m.Index + m.Length;
        var tagRx = new System.Text.RegularExpressions.Regex("<(/?)" + m.Groups["tag"].Value + @"\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var end = rr.html.Length;
        for (var tm = tagRx.Match(rr.html, scan); tm.Success; tm = tagRx.Match(rr.html, tm.Index + 1))
        {
            depth += tm.Groups[1].Value.Length > 0 ? -1 : 1;
            if (depth == 0) { end = tm.Index; break; }
        }
        var inner = rr.html[(m.Index + m.Length)..end];
        rr.pos = Math.Min(rr.html.Length, end + m.Groups["tag"].Value.Length + 3);

        var attrs = m.Groups["attrs"].Value;
        var styleM = System.Text.RegularExpressions.Regex.Match(attrs,
            @"style\s*=\s*(['""])(?<s>[^'""]*)\1",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var style = styleM.Success ? styleM.Groups["s"].Value : "";
        var fracM = System.Text.RegularExpressions.Regex.Match(style, @"width\s*:\s*([\d.]+)%");
        var isInline = System.Text.RegularExpressions.Regex.IsMatch(style,
            @"display\s*:\s*inline-block", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var isFieldset = m.Groups["tag"].Value.Equals("fieldset", StringComparison.OrdinalIgnoreCase);

        if (RenderReportFieldset(rr, inner, fracM, isFieldset)) return true;
        if (isInline && fracM.Success)
        {
            rr.pendingCols.Add((inner, double.Parse(fracM.Groups[1].Value,
                System.Globalization.CultureInfo.InvariantCulture) / 100.0));
            return true;
        }
        FlushCols(rr);
        // white-space:pre content (class="pre") keeps its own line breaks:
        // one row per source line
        if (System.Text.RegularExpressions.Regex.IsMatch(attrs,
                @"class\s*=\s*(['""])[^'""]*\bpre\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        {
            foreach (var preLine in HtmlFragment.StripHtmlTags(inner)
                         .Replace("\r", "").Split('\n'))
            {
                var pl = preLine.Trim();
                if (pl.Length == 0) continue;
                if (rr.draw)
                    DrawReportWords(rr.b!, rr.plainRes!, pl, rr.x, rr.page!.Height - rr.yBase, rr.fs, bold: false, rr.page);
                rr.yBase += rr.pitch;
            }
            return true;
        }
        // a leaf row: label + value, a checkbox + label, a background band,
        // plain text, or a blank spacer; a div of nested divs recurses
        if (System.Text.RegularExpressions.Regex.IsMatch(inner, @"(?s)^\s*<(div|fieldset)\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            && !System.Text.RegularExpressions.Regex.IsMatch(inner,
                @"(?s)^\s*<div\b[^>]*background-color",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        {
            rr.yBase += RenderReportRegion(rr.page, rr.b, inner, rr.x, rr.w, rr.yBase, rr.inFieldset, rr.boldRes, rr.plainRes);
            return true;
        }
        RenderReportRow(rr, inner, style);
        return true;
    }

    /// <summary>Renders one report row: its background, a label/value pair or a check box, else its plain text.</summary>
    private static void RenderReportRow(ReportRegionState rr, string inner, string style)
    {
        var bgM = System.Text.RegularExpressions.Regex.Match(inner + " " + style,
            @"background-color\s*:\s*([-\w#]+)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var lbl = System.Text.RegularExpressions.Regex.Match(inner,
            @"(?s)<label[^>]*>(.*?)</label>",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var val = System.Text.RegularExpressions.Regex.Match(inner,
            @"(?s)<span[^>]*>(.*?)</span>",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var hasCheckbox = System.Text.RegularExpressions.Regex.IsMatch(inner,
            @"<input\b[^>]*type\s*=\s*(['""])?checkbox",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var text = HtmlFragment.StripHtmlTags(System.Text.RegularExpressions.Regex.Replace(
            inner, @"(?s)<(label|span|input)\b[^>]*>|</(label|span)>", "")).Trim();

        if (rr.draw && bgM.Success
            && Converters.HtmlToPdfConverter.ParseCssColor(bgM.Groups[1].Value.Trim()) is { } bandBg)
            rr.b!.SetFillColor(bandBg.R / 255.0, bandBg.G / 255.0, bandBg.B / 255.0)
              .Rectangle(rr.x, rr.page!.Height - (rr.yBase + RptBandDescentPt), rr.w, rr.pitch).Fill()
              .SetFillColor(0, 0, 0);

        if (lbl.Success || hasCheckbox)
        {
            var labelText = lbl.Success
                ? HtmlFragment.StripHtmlTags(lbl.Groups[1].Value).Trim() : "";
            // a checkbox row is TALLER — the input's own line box — and its
            // content seats on the taller row's OWN baseline
            var rowPitch = hasCheckbox ? CheckRowPitch : rr.pitch;
            var rowBase = rr.yBase + (rowPitch - rr.pitch);
            if (rr.draw)
            {
                var cx2 = rr.x;
                if (hasCheckbox)
                {
                    // the checkbox square, then its label INLINE (input+label css)
                    rr.b!.SetStrokeGray(RptCheckboxGray).SetLineWidth(RptStrokePt)
                      .Rectangle(rr.x + RptCheckboxIndentPt,
                          rr.page!.Height - (rowBase + RptCheckboxRisePt),
                          RptCheckboxSizePt, RptCheckboxSizePt)
                      .Stroke().SetStrokeGray(0);
                    if (labelText.Length > 0)
                        DrawReportWords(rr.b, rr.boldRes!, labelText,
                            rr.x + RptCheckboxIndentPt + RptCheckboxSizePt + RptCheckboxLabelGapPt,
                            rr.page.Height - rowBase, rr.fs, bold: true, rr.page);
                }
                else
                {
                    var labelRight = rr.x + (rr.inFieldset ? RptFieldsetLabelFrac : RptLabelBoxFrac) * rr.w;
                    if (labelText.Length > 0)
                        DrawReportWords(rr.b!, rr.boldRes!, labelText,
                            labelRight - Measure(rr, labelText, true), rr.page!.Height - rr.yBase, rr.fs, bold: true, rr.page);
                    var valText = val.Success
                        ? HtmlFragment.StripHtmlTags(val.Groups[1].Value).Trim() : "";
                    // a fieldset row's value follows after ONE space; outside,
                    // the label's .5-em margin comes first — both measured
                    if (valText.Length > 0)
                        DrawReportWords(rr.b!, rr.plainRes!, valText,
                            labelRight + (rr.inFieldset ? RptSpaceEm
                                : RptLabelMarginEm + RptSpaceEm) * rr.fs,
                            rr.page!.Height - rr.yBase, rr.fs, bold: false, rr.page);
                    _ = cx2;
                }
            }
            rr.yBase += rowPitch;
            return;
        }
        // plain text, an &nbsp; spacer line, or a genuinely EMPTY div — the
        // empty one gets NO line box at all
        var plainTxt = System.Text.RegularExpressions.Regex.Replace(
            text.Replace("&nbsp;", " ").Replace(' ', ' '), @"\s+", " ").Trim();
        if (rr.draw && plainTxt.Length > 0)
            DrawReportWords(rr.b!, rr.plainRes!, plainTxt, rr.x, rr.page!.Height - rr.yBase, rr.fs, bold: false, rr.page);
        if (plainTxt.Length > 0
            || inner.Contains("&nbsp;", StringComparison.OrdinalIgnoreCase)
            || inner.Contains(' '))
            rr.yBase += rr.pitch;
    }

    /// <summary>A fieldset element: its legend, its boxed rows (measured dry first) and the frame; true when this element was one.</summary>
    private static bool RenderReportFieldset(ReportRegionState rr, string inner, System.Text.RegularExpressions.Match fracM, bool isFieldset)
    {
        if (isFieldset)
        {
            FlushCols(rr);
            var frac = fracM.Success ? double.Parse(fracM.Groups[1].Value,
                System.Globalization.CultureInfo.InvariantCulture) / 100.0 : 1.0;
            // the legend TAKES the arriving row baseline and the frame opens
            // FsLegendDrop above it, crossing the legend's caps
            var legend = System.Text.RegularExpressions.Regex.Match(inner,
                @"(?s)<legend[^>]*>(.*?)</legend>",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var legendBase = rr.yBase;
            var boxTop = legendBase - FsLegendDrop;
            var innerRows = System.Text.RegularExpressions.Regex.Replace(inner,
                @"(?s)<legend[^>]*>.*?</legend>", "",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var rowsTop = legendBase + FsLegendToRow;
            var rowsH = RenderReportRegion(null, null, innerRows, 0, frac * rr.w, 0, true, null, null);
            var boxBottom = rowsTop + rowsH - rr.pitch + FsPadBottom;
            if (rr.draw)
            {
                var legendText = legend.Success
                    ? HtmlFragment.StripHtmlTags(legend.Groups[1].Value).Trim() : "";
                if (legendText.Length > 0)
                    DrawReportWords(rr.b!, rr.boldRes!, legendText,
                        rr.x + RptLegendInsetPt, rr.page!.Height - legendBase, rr.fs, bold: true, rr.page);
                RenderReportRegion(rr.page, rr.b, innerRows, rr.x + RptFieldsetPadPt, frac * rr.w, rowsTop,
                    true, rr.boldRes, rr.plainRes);
                // the frame: sides and bottom whole; the TOP edge breaks around
                // the legend, which rides it
                var bx0 = rr.x + RptFrameInsetPt;
                var bx1 = bx0 + frac * rr.w + RptFrameOverhangPt;
                var byTop = rr.page!.Height - boxTop;
                var byBot = rr.page.Height - boxBottom;
                rr.b!.SetStrokeGray(RptFrameGray).SetLineWidth(RptStrokePt)
                  .MoveTo(bx0, byTop).LineTo(bx0, byBot).Stroke()
                  .MoveTo(bx1, byTop).LineTo(bx1, byBot).Stroke()
                  .MoveTo(bx0, byBot).LineTo(bx1, byBot).Stroke();
                if (legendText.Length > 0)
                {
                    var lw = MeasureReportText(legendText, rr.fs, bold: true);
                    rr.b.MoveTo(bx0, byTop).LineTo(rr.x + RptLegendInsetPt - RptLegendGapPt, byTop).Stroke()
                     .MoveTo(rr.x + RptLegendInsetPt + lw + RptLegendGapPt, byTop).LineTo(bx1, byTop).Stroke();
                }
                else
                    rr.b.MoveTo(bx0, byTop).LineTo(bx1, byTop).Stroke();
                rr.b.SetStrokeGray(0);
            }
            rr.yBase = boxBottom + FsGap + FsLegendDrop;
            return true;
        }
        return false;
    }
}
