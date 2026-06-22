# Working with Tables

## Extracting tables

`TableAbsorber` detects table-shaped layouts on a page. Pages are 1-based.

```csharp
using Aspose.Pdf;
using Aspose.Pdf.Text;

using var doc = new Document("report.pdf");

var absorber = new TableAbsorber();
absorber.Visit(doc.Pages[1]);

Console.WriteLine($"Found {absorber.Tables.Count} tables");

foreach (var table in absorber.Tables)
{
    Console.WriteLine($"Table at {table.Rect}, {table.Rows.Count} rows");

    foreach (var row in table.Rows)
    {
        foreach (var cell in row.Cells)
            Console.Write($"| {cell.Text,-20} ");

        Console.WriteLine("|");
    }

    Console.WriteLine();
}
```

### Access the text fragments inside a cell

Each cell exposes its `TextFragments` (a `TextFragmentCollection`, 1-based):

```csharp
foreach (var table in absorber.Tables)
foreach (var row   in table.Rows)
foreach (var cell  in row.Cells)
{
    for (int i = 1; i <= cell.TextFragments.Count; i++)
    {
        var frag = cell.TextFragments[i];
        Console.WriteLine($"  '{frag.Text}' — font={frag.TextState.FontName}, " +
                          $"size={frag.TextState.FontSize}");
    }
}
```

### Export to CSV

```csharp
using var writer = new StreamWriter("table.csv");

foreach (var table in absorber.Tables)
foreach (var row   in table.Rows)
{
    var cells = row.Cells.Select(c => $"\"{c.Text.Replace("\"", "\"\"")}\"");
    writer.WriteLine(string.Join(",", cells));
}
```

## Removing detected tables

```csharp
var absorber = new TableAbsorber();
absorber.Visit(doc.Pages[1]);

if (absorber.Tables.Count > 0)
{
    absorber.Remove(absorber.Tables[0]);
    doc.Save("no-table.pdf");
}
```

## Creating tables

### A basic table

```csharp
using Aspose.Pdf;

using var doc = new Document();
var page = doc.Pages.Add();

var table = new Table
{
    ColumnWidths = "100 200 100",   // three columns
};

// Header row
var header = table.Rows.Add();
header.Cells.Add("ID");
header.Cells.Add("Name");
header.Cells.Add("Price");

// Data rows
var row1 = table.Rows.Add();
row1.Cells.Add("1");
row1.Cells.Add("Widget");
row1.Cells.Add("$9.99");

var row2 = table.Rows.Add();
row2.Cells.Add("2");
row2.Cells.Add("Gadget");
row2.Cells.Add("$24.99");

page.AddTable(table);
doc.Save("table.pdf");
```

### Styled tables

```csharp
using Aspose.Pdf;
using Aspose.Pdf.Text;

var table = new Table
{
    ColumnWidths        = "150 200 100",
    Border              = new BorderInfo(BorderSide.All, 0.5f, Color.Black),
    DefaultCellBorder   = new BorderInfo(BorderSide.All, 0.25f, Color.Gray),
    DefaultCellPadding  = new MarginInfo(5, 3, 5, 3),
    DefaultCellTextState = new TextState
    {
        FontSize = 10f,
        FontName = "Helvetica",
    },
};

// Header row with background colour and bold text
var header = table.Rows.Add();
header.BackgroundColor    = Color.LightBlue;
header.DefaultCellTextState = new TextState
{
    FontSize = 11f,
    IsBold   = true,
};
header.Cells.Add("Product");
header.Cells.Add("Description");
header.Cells.Add("Price");

// Alternating row colours
for (int i = 0; i < 10; i++)
{
    var row = table.Rows.Add();
    if (i % 2 == 1)
        row.BackgroundColor = Color.LightGray;

    row.Cells.Add($"Item {i + 1}");
    row.Cells.Add($"Description for item {i + 1}");
    row.Cells.Add($"${(i + 1) * 5.99:F2}");
}

page.AddTable(table);
```

### Cell spanning

```csharp
var table = new Table { ColumnWidths = "100 100 100" };

var row = table.Rows.Add();
var span = row.Cells.Add("This spans 2 columns");
span.ColSpan = 2;
row.Cells.Add("Normal");

var row2 = table.Rows.Add();
var tall = row2.Cells.Add("Spans 2 rows");
tall.RowSpan = 2;
row2.Cells.Add("A");
row2.Cells.Add("B");

var row3 = table.Rows.Add();
row3.Cells.Add("C");
row3.Cells.Add("D");
```

### Rich content in cells

`Cells.Add(TextFragment)` lets a cell carry styled text:

```csharp
using Aspose.Pdf;
using Aspose.Pdf.Text;

var row = table.Rows.Add();

var fragment = new TextFragment("Bold text");
fragment.TextState.IsBold          = true;
fragment.TextState.ForegroundColor = Color.FromRgb(255, 0, 0);
row.Cells.Add(fragment);

row.Cells.Add("Plain text");
```

### Positioning

```csharp
var table = new Table
{
    Left   = 100f,
    Top    = 500f,
    Margin = new MarginInfo(10, 10, 10, 10),
};
```

### Repeating header rows

```csharp
table.RepeatingRowsCount = 1;   // first row repeats on each continuation page
```

`RepeatingRowsCount` (and `Broken`, below) are only honoured by the flow
layout that runs when a table is added to a page's paragraph stream and the
document is saved. `page.AddTable(table)` renders the table on a single page
in one pass and does not paginate, so to get header repetition / page breaks
add the table to `Page.Paragraphs` instead:

```csharp
page.Paragraphs.Add(table);
doc.Save("table.pdf");
```

### Multi-page tables

When a table is added to `Page.Paragraphs` (not `page.AddTable`), the flow
layout splits it across pages on save:

```csharp
using Aspose.Pdf;

var table = new Table
{
    Broken = TableBroken.Vertical,
};

for (int i = 0; i < 100; i++)
{
    var row = table.Rows.Add();
    row.Cells.Add($"Row {i + 1}");
    row.Cells.Add("Data");
}

page.Paragraphs.Add(table);
doc.Save("multi-page-table.pdf");
```
