using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Annotations.JavascriptExtensions;

public static partial class FieldDateTimeFormatter
{
    /// <summary>The stages of the field date format: one format token at a time.</summary>
    private static void FormatDateToken(DateFormatState df, Aspose.Pdf.Annotations.JavascriptExtensions.FieldDateTimeFormatter.FormatToken tok)
    {
        if (tok is LiteralToken lt)
        {
            df.sb.Append(lt.Text);
            return;
        }

        var ct2 = (ComponentToken)tok;
        int? val = df.compIdx < df.finalVals.Length ? df.finalVals[df.compIdx] : null;
        df.compIdx++;

        if (val == null)
        {
            df.sb.Append('0');
            return;
        }

        if (ct2.Kind == TokenKind.Month && (ct2.Abbrev || ct2.FullName))
        {
            int idx = val.Value - 1;
            if (idx >= 0 && idx < 12)
                df.sb.Append(ct2.FullName ? MonthLong[idx] : MonthShort[idx]);
        }
        else if (ct2.Kind == TokenKind.Year && ct2.FourDigit)
        {
            df.sb.Append(val.Value.ToString().PadLeft(4, '0'));
        }
        else if (ct2.PadWidth > 0)
        {
            int display = ct2.Kind == TokenKind.Year ? val.Value % 100 : val.Value;
            df.sb.Append(display.ToString().PadLeft(ct2.PadWidth, '0'));
        }
        else
        {
            df.sb.Append(val.Value.ToString());
        }
    }
}
