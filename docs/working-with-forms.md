# Working with Forms

## Reading form fields

### Iterate fields

`doc.Form` yields `WidgetAnnotation` instances when iterated. To work with the
field model (name, value, type), iterate `doc.Form.Fields` instead.

```csharp
using Aspose.Pdf;
using Aspose.Pdf.Forms;

using var doc = new Document("form.pdf");

Console.WriteLine($"Has form: {doc.HasForm}");
Console.WriteLine($"Field count: {doc.Form.Count}");

foreach (var field in doc.Form.Fields)
{
    Console.WriteLine($"{field.FullName} ({field.Type}) = {field.Value}");
}
```

### Look up a field by name

`FindByName` returns the `Field`; the `this[name]` indexer returns the
`WidgetAnnotation` for the matching field (throws if not found).

```csharp
var field = doc.Form.FindByName("firstName");
if (field is not null)
{
    Console.WriteLine($"Value: {field.Value}");
    Console.WriteLine($"Type: {field.Type}");
    Console.WriteLine($"Read-only: {field.IsReadOnly}");
    Console.WriteLine($"Required: {field.IsRequired}");
}
```

### Export form values as a dictionary

```csharp
Dictionary<string, string?> data = doc.Form.ToObject();

foreach (var (name, value) in data)
    Console.WriteLine($"{name} = {value}");
```

## Field types

`Aspose.Pdf.Forms.FieldType` values map to concrete field classes:

| FieldType         | Class             | Description                          |
|-------------------|-------------------|--------------------------------------|
| `Text`            | `TextBoxField`    | Single- or multi-line text input     |
| `CheckBox`        | `CheckboxField`   | Checkbox (on / off)                  |
| `RadioButton`     | `RadioButtonField`| Radio button group                   |
| `ComboBox`        | `ComboBoxField`   | Drop-down                            |
| `ListBox`         | `ListBoxField`    | List box                             |
| `Button`          | `ButtonField`     | Push button                          |
| `Signature`       | `SignatureField`  | Digital-signature field              |

`ChoiceField` is the shared base of `ComboBoxField` and `ListBoxField`; a
concrete choice field reports `FieldType.ComboBox` or `FieldType.ListBox` (never
a bare `Choice`).

### Text fields

```csharp
using Aspose.Pdf;
using Aspose.Pdf.Forms;

foreach (var field in doc.Form.Fields)
{
    if (field is TextBoxField textField)
    {
        Console.WriteLine($"{textField.FullName}: maxLen={textField.MaxLen}, " +
                          $"multiline={textField.Multiline}");
    }
}
```

### Checkboxes

```csharp
foreach (var field in doc.Form.Fields)
{
    if (field is CheckboxField cb)
        Console.WriteLine($"{cb.FullName}: checked={cb.IsChecked}, onValue={cb.OnValue}");
}
```

### Radio buttons

`doc.Form.RadioGroups` returns one `RadioButtonGroup` per logical group.

```csharp
foreach (var group in doc.Form.RadioGroups)
{
    Console.WriteLine($"Radio group: {group.Name}");
    foreach (var option in group.Options)
        Console.WriteLine($"  {option.Value} (selected: {option.IsSelected})");
}
```

### Choice fields (dropdowns and lists)

`Option.Name` is the display value; `Option.Value` is the export value.

```csharp
foreach (var field in doc.Form.Fields)
{
    if (field is ChoiceField choice)
    {
        Console.WriteLine($"{choice.FullName} (combo={choice.IsCombo}):");
        foreach (var opt in choice.Options)
            Console.WriteLine($"  {opt.Name} (export: {opt.Value})");

        Console.WriteLine($"  Selected: {string.Join(", ", choice.SelectedValues)}");
    }
}
```

## Filling form fields

```csharp
using Aspose.Pdf;
using Aspose.Pdf.Forms;

using var doc = new Document("form.pdf");

// Text field
doc.Form.FindByName("name")!.Value = "John Doe";
doc.Form.FindByName("email")!.Value = "john@example.com";

// Checkbox
if (doc.Form.FindByName("agree") is CheckboxField cb)
    cb.IsChecked = true;

// Radio button — Selected is 1-based
if (doc.Form.FindByName("gender") is RadioButtonField radio)
    radio.Selected = 1;

// Combo box — Selected is 1-based
if (doc.Form.FindByName("country") is ComboBoxField combo)
    combo.Selected = 2;

doc.Save("filled-form.pdf");
```

### Bulk fill with the FormEditor facade

`FormEditor.FillFields` accepts a name -> value dictionary and returns the
filled PDF bytes:

```csharp
using Aspose.Pdf.Facades;

var editor = new FormEditor();

byte[] input = File.ReadAllBytes("form.pdf");
byte[] output = editor.FillFields(input, new Dictionary<string, string>
{
    ["firstName"] = "Jane",
    ["lastName"]  = "Smith",
    ["city"]      = "New York",
});

File.WriteAllBytes("filled.pdf", output);
```

## Creating form fields

### With `FormFieldBuilder`

```csharp
using Aspose.Pdf;
using Aspose.Pdf.Forms;

using var doc = new Document();
var page = doc.Pages.Add();
var builder = new FormFieldBuilder(doc);

builder.AddTextField(page, "name",
    new Rectangle(100, 700, 300, 720), "Default value");

builder.AddCheckBox(page, "agree",
    new Rectangle(100, 660, 120, 680), isChecked: true);

builder.AddComboBox(page, "color",
    new Rectangle(100, 620, 300, 640),
    new[] { "Red", "Green", "Blue" }, selectedValue: "Green");

builder.AddListBox(page, "items",
    new Rectangle(100, 500, 300, 600),
    new[] { "Item A", "Item B", "Item C" });

builder.AddRadioButton(page, "size",
    new[] { new Rectangle(100, 450, 120, 470), new Rectangle(150, 450, 170, 470) },
    new[] { "Small", "Large" }, selectedIndex: 0);

builder.AddSignatureField(page, "sig", new Rectangle(100, 380, 300, 430));

doc.Save("new-form.pdf");
```

### With `FormEditor` (facade)

```csharp
using Aspose.Pdf.Facades;

var editor = new FormEditor("input.pdf", "form.pdf");

editor.AddTextField("notes",   pageNumber: 1, llx: 100, lly: 300, urx: 400, ury: 350);
editor.AddCheckBox("newsletter", pageNumber: 1, llx: 100, lly: 260, urx: 120, ury: 280);

editor.Save();
```

## Flattening forms

Replace interactive widgets with static page content:

```csharp
using Aspose.Pdf.Facades;

var editor = new FormEditor();
byte[] flattened = editor.FlattenForm(File.ReadAllBytes("form.pdf"));
File.WriteAllBytes("flat.pdf", flattened);
```

## Removing fields

```csharp
using Aspose.Pdf.Facades;

var editor = new FormEditor("form.pdf", "no-old.pdf");
editor.RemoveField("oldField");
editor.Save();
```

## Renaming fields

```csharp
using Aspose.Pdf.Facades;

var editor = new FormEditor();
var (pdf, found) = editor.RenameField(inputBytes, "old_name", "new_name");
if (found)
    File.WriteAllBytes("renamed.pdf", pdf);
```

## XFA forms

Detect XFA-backed forms and extract the XFA datasets:

```csharp
using var doc = new Document("xfa.pdf");

if (doc.Form.IsXfa)
{
    Console.WriteLine($"Form type: {doc.Form.Type}");   // Static or Dynamic
    string? xml = doc.Form.GetXfaDatasetsXml();
    Console.WriteLine(xml);
}
```

Assign an XFA packet to a document — for example when copying XFA data from
another form. The packet is written into the AcroForm `/XFA`, so the document
round-trips as an XFA form (`IsXfa`/`HasXfa` report `true` after save):

```csharp
using System.Xml;

XmlDocument xfa = source.Form.XFA.XDP;   // the XDP packet from another form
target.Form.AssignXfa(xfa);
target.Save("with-xfa.pdf");
```

Dynamic XFA layouts can be **flattened** to real PDF pages whose AcroForm fields
stay searchable and fillable, and XFA datasets sync two-way with the AcroForm
fields and export / import via FDF, XFDF, and XML. Fine-grained authoring of
individual XFA dataset fields is still limited.
