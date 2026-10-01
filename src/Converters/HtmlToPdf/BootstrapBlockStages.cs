using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// The stages of the bootstrap block parse: the text flush and one token.
    private static void FlushText(BootstrapBlocksState bb)
    {
        var t = Regex.Replace(DecodeEntities(bb.text.ToString()), @"\s+", " ");
        bb.text.Clear();
        if (bb.textTarget == "h2" && t.Trim().Length > 0)
            bb.blocks.Add(new BsHeading { Text = t.Trim() });
        else if (bb.textTarget == "p" && bb.p is not null && t.Length > 0
                 && (t != " " || bb.p.Runs.Count > 0))
            bb.p.Runs.Add(new BsRun { Text = bb.p.Runs.Count == 0 ? t.TrimStart() : t });
        else if (bb.textTarget == "cell" && bb.row is not null && t.Trim().Length > 0)
            bb.row.Add((t.Trim(), bb.cellTh));
        else if (bb.textTarget == "btn" && bb.btn is not null && t.TrimEnd().Length > 0)
            bb.btn.Label += t.TrimEnd();
    }

    /// <summary>The stages of the bootstrap block parse: the text flush and one token.</summary>
    private static void ParseBootstrapToken(BootstrapBlocksState bb, Token tok)
    {
        if (tok.Kind == TokenKind.Text)
        {
            if (bb.textTarget is not null) bb.text.Append(tok.Value);
            return;
        }
        var tag = tok.Tag!.ToLowerInvariant();
        if (tok.IsClose)
        {
            switch (tag)
            {
                case "h2": FlushText(bb); bb.textTarget = null; break;
                case "p": FlushText(bb); if (bb.p is { Runs.Count: > 0 }) bb.blocks.Add(bb.p); bb.p = null; bb.textTarget = null; break;
                case "td" or "th": FlushText(bb); bb.textTarget = null; break;
                case "tr": if (bb.row is { Count: > 0 }) bb.table?.Rows.Add(bb.row); bb.row = null; break;
                case "table": if (bb.table is { Rows.Count: > 0 }) bb.blocks.Add(bb.table); bb.table = null; break;
                case "button": FlushText(bb); if (bb.p is not null && bb.btn is not null) bb.p.Runs.Add(new BsRun { Button = bb.btn }); bb.btn = null; bb.textTarget = bb.p is not null ? "p" : null; break;
                case "a" when bb.linkDepth > 0:
                    bb.linkDepth--;
                    // a link styled as a button closes like a <button>
                    if (bb.btn is not null && !bb.btn.IsButtonTag)
                    {
                        FlushText(bb);
                        bb.p?.Runs.Add(new BsRun { Button = bb.btn });
                        bb.btn = null;
                        bb.textTarget = bb.p is not null ? "p" : null;
                    }
                    break;
            }
            return;
        }
        switch (tag)
        {
            case "h2": FlushText(bb); bb.textTarget = "h2"; break;
            case "hr": FlushText(bb); bb.blocks.Add(new BsRule()); break;
            case "p": FlushText(bb); bb.p = new BsParagraph(); bb.textTarget = "p"; break;
            case "table": bb.table = new BsTable(); break;
            case "tr": bb.row = new List<(string, bool)>(); break;
            case "td" or "th": FlushText(bb); bb.cellTh = tag == "th"; bb.textTarget = "cell"; break;
            case "span":
                if (tok.Attributes is { } sa && sa.TryGetValue("class", out var scls)
                    && scls.Contains("glyphicon"))
                {
                    var cp = BsGlyphiconCp(scls);
                    if (cp > 0)
                    {
                        FlushText(bb);
                        if (bb.btn is not null) bb.btn.IconCp = cp;
                        else bb.p?.Runs.Add(new BsRun { IconCp = cp, InLink = bb.linkDepth > 0 });
                    }
                }
                break;
            case "button":
                if (bb.p is not null && tok.Attributes is { } ba
                    && ba.TryGetValue("class", out var bcls) && bcls.Contains("btn"))
                {
                    FlushText(bb);
                    var (fill, border, fg) = BtnColors(bb.bs, bb.css, bcls);
                    bb.btn = new BsButton
                    {
                        IsButtonTag = true, Large = bcls.Contains("btn-lg"),
                        Fill = fill, Border = border, Fg = fg,
                    };
                    bb.textTarget = "btn";
                }
                break;
            case "a":
                bb.linkDepth++;
                if (bb.p is not null && tok.Attributes is { } aa
                    && aa.TryGetValue("class", out var acls) && acls.Contains("btn"))
                {
                    FlushText(bb);
                    var (fill, border, fg) = BtnColors(bb.bs, bb.css, acls);
                    bb.btn = new BsButton
                    {
                        IsButtonTag = false, Large = acls.Contains("btn-lg"),
                        Fill = fill, Border = border, Fg = fg,
                    };
                    bb.textTarget = "btn";
                }
                break;
        }
    }
}
