using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Emits the collected items in order: headings, question/answer pairs in their boxes and intro paragraphs, flushing a page and opening the next when the card runs out.</summary>
    private static void EmitAccessFormItems(AccessFormState af)
    {
        foreach (var (kind, a, b) in af.items)
        {
            switch (kind)
            {
                case "intro":
                    af.lastMark = AfTitleBl + AfIntroBlOff;
                    Emit(af, "F1", AfBodyFs, AfIntroX, af.lastMark, a, af.gray);
                    af.lastWasBox = false;
                    break;
                case "hr":
                    af.lastMark += af.lastWasBox ? AfHrAfterBoxes : AfHrAfterIntro;
                    Rule(af, af.lastMark);
                    af.lastWasBox = false;
                    break;
                case "head":
                    af.lastMark += AfSectionBlOff;
                    Emit(af, "F1", AfSectionFs, AfSectionX, af.lastMark, a, af.black);
                    af.lastMark += AfFirstBoxOff;   // becomes the first box top
                    af.lastWasBox = false;
                    break;
                case "pre":
                    af.lastMark += AfVarBlOff;      // from the preceding box bottom
                    Emit(af, "F4", AfVarFs, AfVarX, af.lastMark, a, af.gray);
                    if (b.Length > 0)
                    {
                        af.lastMark += AfVarPitch;
                        Emit(af, "F4", AfVarFs, AfVarContX, af.lastMark, b, af.gray);
                    }
                    af.lastMark += AfBoxAfterVar;   // becomes the next box top
                    af.lastWasBox = false;
                    break;
                case "qa":
                    var top = af.lastWasBox ? af.lastMark + AfBoxPitch - AfBoxH : af.lastMark;
                    if (top + AfBoxH > AfGroundBottom)
                    {
                        FlushPage(af, AfGroundBottom, lastPage: false);
                        af.firstPage = false;
                        NewPage(af);
                        top = AfGroundTop;
                    }
                    QuestionBox(af, top);
                    Emit(af, "F2", AfBodyFs, AfTextX, top + AfQBlOff, a, af.black);
                    Emit(af, "F1", AfBodyFs, AfTextX, top + AfABlOff, b, af.gray);
                    af.lastMark = top + AfBoxH;     // the box bottom
                    af.lastWasBox = true;
                    break;
            }
        }
    }

    /// <summary>Collects the form's items from the card markup: headings, question/answer pairs and intro paragraphs, flattened.</summary>
    private static void CollectAccessFormItems(AccessFormState af)
    {
        foreach (Match m in Regex.Matches(af.content,
            @"<div class=['""](section|section-rule)['""]\s*>((?:(?!<div class=['""]section)[\s\S])*)",
            RegexOptions.IgnoreCase))
        {
            if (m.Groups[1].Value.Equals("section-rule", StringComparison.OrdinalIgnoreCase))
            {
                af.items.Add(("hr", "", ""));
                continue;
            }
            var body = m.Groups[2].Value;
            var hM = Regex.Match(body, @"class=['""]header['""]\s*>([\s\S]*?)</div>", RegexOptions.IgnoreCase);
            if (hM.Success) af.items.Add(("head", Flat(af, hM.Groups[1].Value), ""));
            foreach (Match part in Regex.Matches(body,
                @"<div class=['""]questionContainer['""]\s*>([\s\S]*?)</div>\s*</div>|<pre\b[^>]*>([\s\S]*?)</pre>",
                RegexOptions.IgnoreCase))
            {
                if (part.Groups[2].Success)
                {
                    var lines = Regex.Replace(DecodeEntities(
                        Regex.Replace(part.Groups[2].Value, @"<[^>]+>", "")), "\r", "").Split('\n');
                    var real = new List<string>();
                    foreach (var l in lines) if (l.Trim().Length > 0) real.Add(l.Trim());
                    af.items.Add(("pre", real.Count > 0 ? real[0] : "",
                        real.Count > 1 ? string.Join(" ", real.GetRange(1, real.Count - 1)) : ""));
                    continue;
                }
                var qM = Regex.Match(part.Groups[1].Value, @"class=['""]question['""]\s*>([\s\S]*?)</div>",
                    RegexOptions.IgnoreCase);
                var aM = Regex.Match(part.Groups[1].Value, @"class=['""]answer['""]\s*>([\s\S]*?)$",
                    RegexOptions.IgnoreCase);
                af.items.Add(("qa", qM.Success ? Flat(af, qM.Groups[1].Value) : "",
                    aM.Success ? Flat(af, aM.Groups[1].Value) : ""));
            }
            // an intro section holds a bare answer paragraph and no boxes
            if (!hM.Success && !body.Contains("questionContainer", StringComparison.Ordinal)
                && Regex.Match(body, @"<p>([\s\S]*?)</p>", RegexOptions.IgnoreCase) is { Success: true } pIntro)
                af.items.Add(("intro", Flat(af, pIntro.Groups[1].Value), ""));
        }
    }
}
