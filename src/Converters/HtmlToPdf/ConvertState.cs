using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-conversion working state of <see cref="ConvertFromHtml"/>: the document,
/// the flow cursor, the page box and margins, the dialect flags and the render-time
/// ledgers. One instance per conversion; never shared.</summary>
private sealed class ConvertState
{
    // Internal-link support: where each named anchor (id / <a name>) rendered,
    // and the inline <a href> ranges pending link-annotation emission. Resolved
    // in a second pass after layout so #fragment links to later pages work.
    public Dictionary<string, (Page page, double y)> anchorTargets = null!;
    // Styled-article flow (gated): the modern docs-site fingerprint — a
    // `body { margin:0 }` page whose base font is REM-sized with a unitless
    // line-height and a resolvable sans face (a Hugo/Bootstrap bundle). The
    // metric flow lays it out with the face's real advances and the sheet's
    // own line factor; the legacy calibrated flow rendered these articles at
    // the 12pt/1.3 defaults and every block landed far from its true place.
    public bool articleFlow;
    public double articleLineFactor;
    public Stack<(double SavedML, double SavedCW, double TopY, double MinEndY, Page StartPage)> bandStack = null!;
    // Build the block list, segmenting out real data tables (no form inputs) so they render
    // as column grids instead of a flattened single column. Text between tables flows through
    // the normal block path; a table with form inputs stays on the flat path (BuildTableFromHtml
    // would swallow its <input>s). Table-free HTML yields a single text segment = unchanged.
    public List<Block> blocks = null!;
    public string? bodyCssFace;
    // The certificate header: two IMG tags floated opposite ways by their own inline
    // style. Only this shape keeps its sheet against a wide table and its page margins
    // symmetric. Keyed on the inline img declaration, not on the presence of a float
    // anywhere - a stylesheet full of floated layout divs is a different dialect and
    // still widens as before.
    // The line-height the BODY's own class rule declares. The certificate page styles
    // its body with `.certificate { line-height: 1.42857143 }` and every paragraph it
    // does not style paces on that; the class rule was not reaching the blocks.
    public double bodyLineFactor;
    // …and the pinned report's page face (its body rule names one): the flow
    // wraps on the face's REAL advances — the 0.52-em estimate over-measures
    // narrow-lettered runs and wraps a line that should stay whole.
    public string? bodyPinnedFace;
    public Stack<(double XLeft, double TopY, double Width, double BorderPt, Page Page, double Gray, double PadSide, double SavedML, double SavedCW)> boxStack = null!;
    // Print-grid stacked column scopes: narrow the ambient content width for the
    // enclosed blocks (no y reset — columns stack, unlike float-band columns).
    public Stack<(double SavedML, double SavedCW)> colScopeStack = null!;
    public Dictionary<string, Dictionary<string, string>> css = null!;
    // Dash-overflow wrap box (quirks CSS-run and title-column docs): an
    // unbreakable dash-delimited segment wider than the content box widens the
    // WRAP LIMIT for the whole document — long tokens then break only after
    // dashes, overflowing the margin exactly as the expected render does
    // (measured: the widest segment IS the limit, so the defining segment
    // always fits its line whole). Zero when every segment fits — the legacy
    // wrap then stands untouched. Measured in the body face when the sheet
    // declares one, else the UA serif these documents are laid out with.
    public string dashWrapFace = null!;
    // Push-button colours for the escaped-attr dialect: the page's button{} TAG
    // rule (the only kind of rule this dialect can match) supplies the fill —
    // its gradient's first stop — and the caption colour; UA grey otherwise.
    public Color? dialectButtonFill;
    public string dialectButtonTextRg = null!;
    public Document doc = null!;
    // One /Font resource dict shared by every page of this conversion (see EnsureFonts).
    public Core.PdfDictionary docFontDict = null!;
    public bool doctypeLeadCharged;
    // CSS font-family → embedded TrueType. Each resolvable family is embedded once
    // (shared indirect font dict) and registered into each page's resources on first
    // use under a unique "FE<n>" resource name. Blocks whose family doesn't resolve
    // fall back to the Standard-14 FontRes (Helvetica/Courier).
    public Dictionary<string, (string resName, Core.PdfIndirectRef fontRef)> embeddedFonts = null!;
    // The FIELDSET WORKSHEET: a %-width padded BODY around <fieldset>/<legend>
    // sections of class-labelled grids — the UA flow renders it with the body
    // box offsets and fieldset frames (probed: content x 129 = 90 + the 2px
    // margin + 50px padding, frames 0.75 gray at the body's 70% content box).
    public bool fieldsetDoc;
    // Content streams of floated (align="left") tables, prepended to their pages after
    // the flow pass so float text leads the content order (floats paint first).
    public List<(Page page, byte[] ops)> floatFirstOps = null!;
    public HtmlFlowCursor flow = null!;
    public Dictionary<string, (int objNum, string embedName)> fontFileCache = null!;
    // Fieldset frames: FsBox markers bracket each box; the gray frame draws
    // at the close over [top, cursor]. A legend leading the box pins the
    // frame's top under its own baseline. Table segments parse separately,
    // so they take the LIVE indent below.
    public Stack<(Page page, double topY)> fsStack = null!;
    // Open height floors: the flow cursor and page where a sized container
    // opened (see Block.HeightFloorStart).
    public Stack<(double Y, Page P)> heightFloorStack = null!;
    // The html5-doctype slice of the bare UA class: a MODERN document, so it
    // renders with the CURRENT-era behaviour — real root-em heading
    // margins and the first-block MAX-collapse — while the doctype-less
    // vintage corpus keeps the dropped-margin top its templates pinned
    // (the era wall the first-block arm's comment records).
    public bool html5BareUa;
    public double marginBottom;
    public double marginLeft;
    public double marginRight;
    public double marginTop;
    // Respect user-set margins verbatim (including explicit zeros); fall back to
    // the HTML-renderer defaults only when MarginInfo was never touched.
    // Unstyled content sits
    // ~96 pt from the left and the first baseline ~89 pt from the top of an
    // A4 page; the previous 72 pt left/top shifted every conversion up-and-left
    // by ~24 pt / ~17 pt. Right/bottom keep 72 pt.
    // Margins are explicit when values were SET — or when the caller ASSIGNED a
    // MarginInfo object at all: `options.PageInfo.Margin = new MarginInfo()` is
    // the public API idiom for "zero margins", distinct from the untouched
    // default that gets the renderer's fallback margins.
    public bool marginsExplicit;
    public bool mozEmailDoc;
    public System.Collections.Generic.HashSet<Block>? msoKeepWithImage;
    public double pageHeight;
    public double pageWidth;
    public List<(Page page, Aspose.Pdf.Rectangle rect, string url, string? text)> pendingLinks = null!;
    // Print-authored cover documents: a body{margin:0} page that separates its
    // cover from the body with an explicit page-break-after — the cover
    // classes' own type scale, physical-unit margins, and line factors ARE the
    // layout (see coverStyles in ApplyCssRules).
    public bool printCoverDoc;
    public double printGridLineFactor;
    // Bootstrap form-horizontal label/value rows become 2-column tables (the
    // float-label pattern the flow renderer would stack). Remember the marker
    // BEFORE the transform removes it — it also unlocks px-width float columns.
    // (Computed before the shorthand expansion: the dialect also opts in to the
    // familyless-`font:`-shorthand reset semantics.)
    public HtmlDocProfile profile = null!;
    public double quirksWrapW;
    // Radio inputs collected during layout, grouped (after the loop) into one
    // RadioButtonField per HTML `name` so each option surfaces as a
    // RadioButtonOptionField on Form.Fields.
    public List<(string group, bool chk, Page page, Rectangle rect)> radioOptions = null!;
    public StringBuilder sb = null!;
    // Inline (style="…") families and the UA flow: a family on grid cells
    // always disqualifies (the allowance covers only flow typography); the
    // UA base face itself styles nothing new; and a STYLESHEET-FREE document
    // whose flow spells ONE family everywhere is a FACE SWAP, not typography
    // — the whole UA structure is kept and only the face changes
    // (probed: the certificate sheet's ladder is IDENTICAL under MS Mincho,
    // Arial, Times, Courier, Meiryo, and no family at all — 12pt body, 24pt
    // h1, the same y positions). Documents mixing SEVERAL inline families —
    // or carrying stylesheet rules at all (the round-trip/vintage corpus) —
    // keep the calibrated flow their templates were rendered by.
    public bool singleFamilyFaceSwap;
    // The priority-matrix section of the sectioned dead-stylesheet report
    // renders whole from its measured ladder (see SpMatrix.cs) — collect its
    // blocks up front so the loop can consume them in one piece.
    public HashSet<Block>? spBlocks;
    public Block? spFirst;
    public bool uaFlow;
    // A MediaWiki page export (mw-parser-output content, mw-list-item menus,
    // load.php stylesheet links): it renders as a UA-serif
    // document — its skin styles are utility rules that do not restyle the
    // flow — and applies the site sheet's Main-page hides (the upload and
    // cite tool items; measured A/B with the test's own
    // URL-base + print options). The inline mw sheets style nothing the UA
    // flow draws, so they come off before classification.
    public bool wikiExportDoc;
    public PageInfo? pageInfo;
    public MarginInfo? pageMargin;
    // A margin authored on the DEFAULT PageInfo an HtmlLoadOptions constructs
    // resolves PER SIDE — setting only Top keeps the renderer defaults for the
    // other three sides. A caller-REPLACED PageInfo (or MarginInfo) is authored
    // as a whole: its untouched sides are deliberate zeros.
    public bool perSide;
    // IsPriorityCssPageRule: the caller asks for the document's own `@page` rules
    // to outrank the PageInfo descriptor, so a print sheet that declares
    // `size: A3` and `margin: 20mm` is laid out on exactly that sheet. Every side
    // the rule leaves out keeps the descriptor's value. `@page :first` only
    // reaches the top margin here — the flow opens at it and every continuation
    // page resumes at the general rule's margin, which is the whole visible
    // difference between the first sheet and the rest.
    public double cssPageFirstTopLift;
    public CssPageRule? cssPageRule;
    // IsRenderToSinglePage: the whole flow is laid out CONTINUOUSLY
    // (no page breaks, uninterrupted paragraph rhythm) and sizes the one sheet
    // to whole CONTENT BANDS of the authored page: height = N × (authored
    // height − vertical margins). Layout runs against the PDF coordinate
    // ceiling so no break fires; the tail fix-up below shifts the finished
    // content onto the final sheet and sets its real MediaBox.
    public bool singlePage;
    public double singlePageRealH;
    public double scalePendingS;
    public double scaleReqPageW;
    public double scaleReqPageH;
    // <body bgcolor=…> / body { background(-color) } tints the page canvas. White is
    // the canvas default, so an explicit white paints nothing. Later declarations win:
    // the presentational attribute first, then an inline style, then the stylesheet.
    public Color? bodyBackground;
    public Match bodyOpen = null!;
    // …and the body tag's OWN inline style declares it just as well: `<body
    // style="margin: 0px">` is the same statement as `body { margin: 0 }`, and
    // reading only the stylesheet left such a page on the default top margin — its
    // whole document then sat 17 pt below the box its background paints.
    public string? bodyMargin;
    // A NON-zero `body { margin }` insets the content box on the LEFT. Only the
    // left: the widened sheet ends exactly one page margin past the last ink, so
    // the body's right margin never gets to push anything (the same asymmetry the
    // ink-widen rule below encodes). Resolved against the body's own declared
    // font size, the em a browser would use.
    public double bodyMarginLeftPt;
    // …its page face (the body rule's first installed family) carries the
    // inter-table <br/> line boxes.
    public string? elementGridFace;
    // Title-column stylesheets (the Outlook/Teams export shape): a class rule
    // declaring display:inline-block WITH a width marks label columns — the
    // label text is its own run and the value seats at the column's right
    // edge. Such documents also wrap their plain-text sections on the
    // dash-overflow model (see quirksWrapW below).
    public bool inlineBlockColRules;
    public double bodyMarT;
    // UA-default metric flow (gated): a STYLESHEET-LESS MSHTML export is laid out
    // from pure user-agent defaults — serif (Times New Roman) at the
    // 16px base, 1.125em line boxes with win-metric half-leading baselines, real
    // paragraph/heading gaps, 90/72pt page margins with the 8px body margin (6pt)
    // inside them (left on every page, top on page 1 only), and the page WIDTH
    // widened to fit a block image at its natural pixel size (see grp/T
    // notes); every other document
    // keeps its existing flow byte-for-byte.
    public bool uaMshtml;
    // Full-document UA-default flow: a complete <html> document that declares NO
    // font-family anywhere (inline style, <font>, or stylesheet) inherits the source
    // renderer's UA stylesheet — Times serif, 2em/1.5em… headings, browser block gaps
    // and line boxes — the same model MSHTML exports use, extended to any font-family-
    // free full document (CSS colours and other rules still apply through the flow).
    // Its text draws with the Standard-14 serif faces, so nothing is embedded; a bare
    // fragment (no <html>/<body>) or a table document keeps the legacy calibrated flow.
    // Only pure UA-default documents qualify: the stylesheet may tint text
    // (color/background) but must not drive LAYOUT — any margin/width/position/
    // content/font rule means the page relies on authored geometry the legacy flow
    // is calibrated to, so forcing it through the UA metric flow would move it.
    // A rule whose selector matches nothing in the document cannot drive layout —
    // generated pages ship dormant style blocks (an unused .jumbotron kit) that
    // must not disqualify the UA-default flow. Presence is judged by the
    // selector's LAST simple selector: its class, id, or element type.
    // (moved up: the css-layout scan below needs it)
    // Only the SIDE margins are authored (top/bottom keep the renderer
    // defaults) — a caller who sets all four sides authored a full custom
    // sheet, not an edge-to-edge one, and keeps the plain UA margin model.
    public bool edgeToEdgePre;
    // Body content that is entirely tables (whitespace aside): every styled
    // div/b/span then sits inside a table cell, so their class rules feed the
    // metric table renderer rather than the flow.
    public bool bodyAllTables;
    // A selector is TABLE-SCOPED when its rules can only reach table content —
    // the metric table renderer owns those, so they neither disqualify the
    // UA flow nor make the document "authored-family" (cssRealFamily below).
    public bool cssLayoutFree;
    // A document with no markup at all (a plain-text file fed through
    // HtmlLoadOptions) has nothing to disqualify it: it renders in pure UA
    // defaults exactly like a font-family-free <html><body> document.
    public bool tagFreeDoc;
    // A caller who zeroes BOTH side margins on the default PageInfo authored an
    // edge-to-edge sheet: such a document keeps the UA flow WITH its tables (the
    // metric table renderer draws them as real grids) — the table exclusion below
    // protects only the legacy calibrated flow, which these pages never had.
    public bool edgeToEdgeDoc;
    // Table interiors are excluded from the font scans: the metric table
    // renderer applies <font face/size> tags and inline font-family cell
    // styling itself, so they disqualify the UA flow only on FLOW text.
    public string htmlSansTables = null!;
    // A stylesheet family disqualifies the flow only when a USED rule names a
    // face that actually RESOLVES — a quoted junk family ("ARIAL,HELVETICA,
    // SANS-SERIFF" is one literal name) falls back to the UA default exactly
    // like no declaration at all.
    public bool cssRealFamily;
    // A document whose ONLY resolvable family comes from the BODY rule, at the
    // UA 16px base size, keeps UA structure wholesale: the rule swaps the face
    // under the same metrics (probed on the step-row sheet — a `body
    // { font-family: Arial; font-size: 12pt }` renders the UA line grid at
    // 13.5 with the UA paragraph margins, in Arial). Such a document rides
    // the UA flow with the body face as its metric/run face.
    public string? uaBodyFace;
    // The absolute-span LEDGER: a table-less stylesheet whose ONLY
    // layout-authoring properties lay label/value columns — display:block
    // rows, margin-left labels, position:absolute+left value columns,
    // class widths — authors geometry the UA flow implements directly,
    // so such a document renders in UA defaults with those mechanisms
    // rather than the legacy calibrated flow.
    public bool absSpanLedger;
    public double fsBodyPct;
    public double fsBodyChromePt;
    // A QUIRKS document whose stylesheets exist to load UNRESOLVABLE custom
    // faces (@font-face) renders in pure UA defaults: the expected render
    // ignores their layout wholesale (probed on the Zero-Trust report — TNR
    // 12 on the UA grid at the explicit margins, every class padding and
    // margin inert).
    public bool customFontFaceDoc;
    public bool uaNoFontDoc;
    // A <header>/<footer> becomes a *running* region (repeated on every page) only when it is
    // pinned with position:fixed — the print idiom `@media print { header { position:fixed } }`.
    // A semantic <header>/<footer> that is ordinary flow content (display:block, or
    // position:absolute/static) is laid out once in document order, so it must stay in the flow;
    // pulling it out and repeating it per page would duplicate that content on every page.
    public string? runHeader;
    public string? runFooter;
    public Match hMatch = null!;
    public Match fMatch = null!;
    // A <div id="footer"> whose only rendered content is an <img> — the classic
    // letterhead-logo footer. That image is pulled out of the
    // flow and placed ONCE at page 1's bottom margin, left content edge, at
    // its CSS pixel size (a trailing 630×60px logo lands at
    // (marginLeft, marginBottom) + 472.5×45pt on page 1 only; no other page
    // carries it). A footer div with visible text stays in the flow.
    public string? page1FooterImgSrc;
    public double page1FooterImgW;
    public double page1FooterImgH;
    public Match dfMatch = null!;
    public List<BeforeMarker> beforeMarkers = null!;
    // Only VISIBLE form controls keep a table on the flat path — a page whose
    // inputs are all display:none (hidden state carriers in generated reports)
    // renders its tables as real grids.
    public bool htmlHasFormInput;
    // A form control is a reason to keep ITS OWN table flat, not the whole document:
    // the grid path would swallow that table's controls, but a control three tables
    // away costs a data grid its columns for nothing. Documents that hold both — an
    // application form with a plain report grid at the end — segment per table.
    public bool perTableFormGate;
    // A chrome-less SINGLE-COLUMN table is a layout wrapper, not a grid: the
    // expected render flows the cell content inset by the default cell chrome
    // (UaCellChromePt) instead of drawing a grid — the SharePoint wiki shape.
    // Strict fingerprint: no border/spacing/padding attrs, no bgcolor or css
    // box decoration anywhere, no <th>, no nesting, every row exactly one td.
    // A stylesheet can give table cells the same chrome the markup can, and a
    // cell the sheet borders or fills is a GRID cell — the unwrap below reads
    // the segment's own text only, so the sheet is consulted here once.
    public bool sheetChromesCells;
    // Auto-size the page width to the widest data table's natural (content-fit) width when it
    // would otherwise overflow the content area — matching the layout engine, which widens
    // the page for a wide table rather than compressing/clipping it. Only widen (never shrink),
    // and only when a table genuinely overflows, so normal-width conversions are unchanged.
    public double availContentW;
    public double widestTable;
    // …or a <pre>-grown grid (the phantom surplus column): its sheet grows to
    // the longest pre line and the content seats at the UA top margin.
    public bool preGrownGridDoc;
    // The widest table's natural is the UA-serif percent-grid floor sum -
    // the sheet then follows the ink-widen model.
    public bool widestIsPctMin;
    // A table that DECLARES an absolute width: read off the MARKUP, not the
    // block list — the metric flow lays its tables out through the table
    // renderer, so they never become table blocks and the natural-width
    // probe above never sees them. Percent widths never widen.
    public double declaredTableW;
    public double collapseTableW;
    // A `table { width: Npx }` ELEMENT rule sizes every grid on the page the
    // same way a width attribute sizes one table. Tracked apart from the
    // attribute widths because its grown sheet has its own extent model.
    public double elementTableW;
    // Pull <title> for doc metadata before we lose it in stripping.
    public Match titleMatch = null!;
    // Float-band / border-box layout state. A band narrows the content box per
    // column and rewinds the cursor to the band top between columns; a box draws
    // its border rectangle when it closes on the page it opened on. Bands and
    // boxes that fit the current page render structurally; one that page-breaks
    // mid-way degrades to sequential flow (no rewind across pages).
    public double flowMarginLeft;
    public double flowContentWidth;
    /// <summary>The inline SVG payloads extracted from the markup, in document order.</summary>
    public List<byte[]> inlineSvgs = null!;
    /// <summary>The source markup, rewritten in place as the conversion pre-processes it.</summary>
    public string html = null!;
}
}
