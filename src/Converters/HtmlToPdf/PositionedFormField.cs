using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Positioned form: one field box of a section rendered.</summary>
    private static bool RenderFormField(PositionedFormState pf, FormSectionState sn, Match fb)
    {
        var ff = new FormFieldState();
        ff.pf = pf;
        ff.sn = sn;
        ff.fb = fb;
        ParseFieldBox(ff);

        SizeAndBoxField(ff);

        RenderFieldQuestion(ff);

        ff.respM = Regex.Match(ff.fBody,
            @"class=""pan-question-response[^""]*""[^>]*>([\s\S]*)$", RegexOptions.IgnoreCase);
        if (!ff.respM.Success) return true;
        ff.resp = ff.respM.Groups[1].Value;
        ff.respBelow = ff.qW >= 220;              // 300px+ questions stack (measured)
        ff.rx = ff.respBelow ? ff.contentX0 : ff.qRight;
        ff.ry = ff.respBelow ? ff.contentY0 + ff.pf.lineBox : ff.contentY0;

        RenderFieldUnderlines(ff);

        RenderFieldOptionRows(ff);
        return true;
    }
}
