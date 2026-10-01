using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// Font face sheet: 
    // Resolve a paragraph's rule chain: any selector whose last simple part is
    // p.<class> and whose ancestor parts each match the wrapper's classes.
    private static string PropOf(FontFaceSheetState ff, string pClass, string divClasses, string prop)
    {
        string? value = null;
        foreach (var (sel, body) in ff.rules)
        {
            var parts = sel.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;
            var last = parts[^1];
            var match = false;
            foreach (var cls in pClass.Split(' ', System.StringSplitOptions.RemoveEmptyEntries))
                if (last == "p." + cls || last == "." + cls) match = true;
            if (!match) continue;
            var ancestorsOk = true;
            for (var i = 0; i < parts.Length - 1; i++)
            {
                var anc = parts[i].TrimStart('.');
                var ok = false;
                foreach (var cls in divClasses.Split(' ', System.StringSplitOptions.RemoveEmptyEntries))
                    if (anc == cls) ok = true;
                if (!ok) { ancestorsOk = false; break; }
            }
            if (!ancestorsOk) continue;
            var pv = Regex.Match(body, prop + @":\s*(?<v>[^;}]+)");
            if (pv.Success) value = pv.Groups["v"].Value.Trim();
        }
        return value ?? "";
    }

    private static double Pt(FontFaceSheetState ff, string v) => Regex.Match(v, @"(?<n>-?[\d.]+)pt") is { Success: true } pm2
        ? double.Parse(pm2.Groups["n"].Value, System.Globalization.CultureInfo.InvariantCulture) : 0;
}
