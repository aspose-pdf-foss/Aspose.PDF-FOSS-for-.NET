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
        foreach (var groups in SafeRegex.Matches(af.content,
            @"<div class=['""](section|section-rule)['""]\s*>((?:(?!<div class=['""]section)[\s\S])*)",
            RegexOptions.IgnoreCase).Select(m => m.Groups))
        {
            if (groups[1].Value.Equals("section-rule", StringComparison.OrdinalIgnoreCase))
            {
                af.items.Add(("hr", "", ""));
                continue;
            }
            var body = groups[2].Value;
            var hM = SafeRegex.Match(body, @"class=['""]header['""]\s*>([\s\S]*?)</div>", RegexOptions.IgnoreCase);
            if (hM.Success) af.items.Add(("head", FlatKeepingNbsp(hM.Groups[1].Value), ""));
            foreach (var partGroups in SafeRegex.Matches(body,
                @"<div class=['""]questionContainer['""]\s*>([\s\S]*?)</div>\s*</div>|<pre\b[^>]*>([\s\S]*?)</pre>",
                RegexOptions.IgnoreCase).Select(part => part.Groups))
            {
                if (partGroups[2].Success)
                {
                    var lines = SafeRegex.Replace(DecodeEntities(
                        SafeRegex.Replace(partGroups[2].Value, @"<[^>]+>", "")), "\r", "").Split('\n');
                    var real = new List<string>();
                    foreach (var l in lines) if (l.Trim().Length > 0) real.Add(l.Trim());
                    af.items.Add(("pre", real.Count > 0 ? real[0] : "",
                        real.Count > 1 ? string.Join(" ", real.GetRange(1, real.Count - 1)) : ""));
                    continue;
                }
                var qM = SafeRegex.Match(partGroups[1].Value, @"class=['""]question['""]\s*>([\s\S]*?)</div>",
                    RegexOptions.IgnoreCase);
                var aM = SafeRegex.Match(partGroups[1].Value, @"class=['""]answer['""]\s*>([\s\S]*?)$",
                    RegexOptions.IgnoreCase);
                af.items.Add(("qa", qM.Success ? FlatKeepingNbsp(qM.Groups[1].Value) : "",
                    aM.Success ? FlatKeepingNbsp(aM.Groups[1].Value) : ""));
            }
            // an intro section holds a bare answer paragraph and no boxes
            if (!hM.Success && !body.Contains("questionContainer", StringComparison.Ordinal)
                && SafeRegex.Match(body, @"<p>([\s\S]*?)</p>", RegexOptions.IgnoreCase) is { Success: true } pIntro)
                af.items.Add(("intro", FlatKeepingNbsp(pIntro.Groups[1].Value), ""));
        }
    }
}
