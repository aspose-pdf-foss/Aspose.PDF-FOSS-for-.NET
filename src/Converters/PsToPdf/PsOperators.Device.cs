using System;
using System.Collections.Generic;

namespace Aspose.Pdf.Converters.PsToPdf;

/// <summary>
/// Device, resource and input/output operators. Most of the raster-device controls
/// are accepted and ignored: halftones, screens, transfer functions and undercolour
/// removal change nothing in the PDF the reference converter writes, so honouring
/// them would move the output away from the etalons rather than towards them.
/// </summary>
internal sealed partial class PsInterpreter
{
    /// <summary>Install every operator table.</summary>
    private void RegisterOperators()
    {
        RegisterCoreOperators();
        RegisterDataOperators();
        RegisterGraphicsOperators();
        RegisterFontOperators();
    }

    private void RegisterDeviceOperators()
    {
        RegisterIgnoredDeviceOperators();
        RegisterProbedNames();
        RegisterIdentityOperators();
        RegisterResourceOperators();
        RegisterFileOperators();
    }

    /// <summary>Operators that consume their operands and do nothing else.</summary>
    private void RegisterIgnoredDeviceOperators()
    {
        DefDiscard("setscreen", 3);
        DefDiscard("setcolorscreen", 12);
        DefDiscard("settransfer", 1);
        DefDiscard("setcolortransfer", 4);
        DefDiscard("setblackgeneration", 1);
        DefDiscard("setundercolorremoval", 1);
        DefDiscard("sethalftone", 1);
        DefDiscard("setcolorrendering", 1);
        DefDiscard("setdevparams", 2);
        DefDiscard("setsystemparams", 1);
        DefDiscard("setuserparams", 1);
        Def("nulldevice", i => i.Graphics.NullDevice());
        DefDiscard("banddevice", 4);
        DefDiscard("framedevice", 4);
        DefDiscard("renderbands", 1);
        Def("setpagedevice", SetPageDeviceOperator);
        Def("currentpagedevice", i => i.Push(PsValue.Dict(new PsDictionary())));
        Def("currentscreen", i =>
        {
            i.Push(DefaultScreenFrequency);
            i.Push(0);
            i.Push(PsValue.Proc(new PsArray(0)));
        });
        Def("currentcolorscreen", i =>
        {
            for (var k = 0; k < ColorScreenResults; k++) i.Push(PsValue.Null);
        });
        Def("currenttransfer", i => i.Push(PsValue.Proc(new PsArray(0))));
        Def("currentcolortransfer", i =>
        {
            for (var k = 0; k < ColorComponentCount; k++) i.Push(PsValue.Proc(new PsArray(0)));
        });
        Def("currentblackgeneration", i => i.Push(PsValue.Proc(new PsArray(0))));
        Def("currentundercolorremoval", i => i.Push(PsValue.Proc(new PsArray(0))));
        // A program reads the halftone back and branches on its type before choosing
        // how to ask for a screen, so the dictionary has to carry the entries a
        // halftone of the plain kind does rather than be empty.
        Def("currenthalftone", i => i.Push(PsValue.Dict(PlainHalftone())));
        Def("currentcolorrendering", i => i.Push(PsValue.Dict(new PsDictionary())));
        Def("currentdevparams", i =>
        {
            i.Pop();
            i.Push(PsValue.Dict(new PsDictionary()));
        });
        Def("currentsystemparams", i => i.Push(PsValue.Dict(new PsDictionary())));
        Def("currentuserparams", i => i.Push(PsValue.Dict(new PsDictionary())));
        Def("deviceinfo", i => i.Push(PsValue.Dict(new PsDictionary())));
    }

    /// <summary>A halftone screen frequency to report when asked. Nothing reads it
    /// back for a decision that reaches the page.</summary>
    private const double DefaultScreenFrequency = 60;

    /// <summary>The halftone a device with no screen of its own reports: the plain
    /// kind, one screen for every colourant.</summary>
    private static PsDictionary PlainHalftone()
    {
        var dict = new PsDictionary(HalftoneEntryCount);
        dict.Put("HalftoneType", PsValue.Int(PlainHalftoneType));
        dict.Put("Frequency", PsValue.Real(DefaultScreenFrequency));
        dict.Put("Angle", PsValue.Int(0));
        dict.Put("SpotFunction", PsValue.Proc(new PsArray(0)));
        return dict;
    }

    /// <summary>The halftone kind that carries one frequency, angle and spot.</summary>
    private const int PlainHalftoneType = 1;

    /// <summary>How many entries that halftone carries.</summary>
    private const int HalftoneEntryCount = 4;

    /// <summary>How many results <c>currentcolorscreen</c> leaves.</summary>
    private const int ColorScreenResults = 12;

    /// <summary>Components in the subtractive space the transfer operators cover.</summary>
    private const int ColorComponentCount = 4;

    /// <summary>Bind an operator that discards a fixed number of operands.</summary>
    private void DefDiscard(string name, int operands) => Def(name, i =>
    {
        for (var k = 0; k < operands; k++) i.Pop();
    });

    /// <summary><c>setpagedevice</c> is honoured only for the page size, and only
    /// before anything has been painted.</summary>
    private static void SetPageDeviceOperator(PsInterpreter i)
    {
        var dict = i.PopDict();
        if (!dict.TryGet("PageSize", out var size) || size.AsArray is null) return;
        var array = size.AsArray;
        if (array.Length < 2 || !array[0].IsNumber || !array[1].IsNumber) return;
        i.Graphics.ResizePage(array[0].Number, array[1].Number);
    }

    /// <summary>Operators that report what interpreter this is.</summary>
    private void RegisterIdentityOperators()
    {
        // These are VALUES in the system dictionary, not operators: a program reads
        // them with `systemdict /languagelevel get` and compares the number it finds,
        // which an operator object would not answer.
        SystemDict.Put("version", PsValue.Str(PsString.FromText(LanguageVersion)));
        SystemDict.Put("languagelevel", PsValue.Int(LanguageLevel));
        SystemDict.Put("product", PsValue.Str(PsString.FromText(ProductName)));
        SystemDict.Put("revision", PsValue.Int(ProductRevision));
        SystemDict.Put("serialnumber", PsValue.Int(0));
        Def("usertime", i => i.Push(i.ElapsedMilliseconds));
        Def("realtime", i => i.Push(i.ElapsedMilliseconds));
        Def("checkpassword", i =>
        {
            i.Pop();
            i.Push(true);
        });
        Def("handleerror", i => { });
        // Whether procedures are stored packed is a memory matter, not a painting one;
        // a program asks so it can put the setting back the way it found it.
        Def("setpacking", i => i.Packing = i.PopBool());
        Def("currentpacking", i => i.Push(i.Packing));
        Def("echo", i => i.PopBool());
    }

    /// <summary>The language version a level-2 interpreter reports.</summary>
    private const string LanguageVersion = "2017";

    /// <summary>The language level implemented.</summary>
    private const int LanguageLevel = 2;

    /// <summary>The product string, kept generic because a program may print it.</summary>
    private const string ProductName = "PostScript interpreter";

    /// <summary>The revision this interpreter reports.</summary>
    private const int ProductRevision = 1;

    /// <summary>Milliseconds since the conversion began, which <c>usertime</c>
    /// reports and animation-style programs use to seed a value.</summary>
    public int ElapsedMilliseconds => Environment.TickCount - _startTicks;

    private readonly int _startTicks = Environment.TickCount;

    /// <summary>Names a level-2 interpreter is expected to carry, which programs test
    /// for with <c>known</c> or <c>where</c> before choosing a branch. Their presence
    /// is what the program is asking about; each one consumes its operands and does
    /// nothing, because none of them reaches the page.</summary>
    private void RegisterProbedNames()
    {
        // In systemdict, where the language puts them.
        DefDiscard("filenameforall", 3);
        DefDiscard("deletefile", 1);
        DefDiscard("renamefile", 2);
        DefDiscard("setfileposition", 2);
        Def("fileposition", i =>
        {
            i.Pop();
            i.Push(0);
        });

        // `diskonline` is deliberately absent from BOTH dictionaries: the two samples
        // that ask where it lives — one of systemdict, one of statusdict — each expect
        // to be told it is in neither.
        DefStatus("diskstatus", i =>
        {
            i.Push(0);
            i.Push(0);
        });
        DefStatus("dostartpage", i => i.Push(false));
        DefStatus("setdostartpage", i => i.PopBool());
        DefStatus("setpassword", i =>
        {
            i.Pop();
            i.Pop();
            i.Push(true);
        });
        DefStatus("printerror", i => i.Pop());
        DefStatus("jobname", i => i.Push(PsValue.Str(new PsString(0))));
        SystemDict.Put("statusdict", PsValue.Dict(StatusDict));
    }

    /// <summary>Bind an operator into the status dictionary alone.</summary>
    private void DefStatus(string name, Action<PsInterpreter> action) =>
        StatusDict.Put(name, PsValue.Op(new PsOperator(name, action)));

    /// <summary>How many forms are being painted, so a form that paints itself
    /// cannot recurse without end.</summary>
    public int FormDepth { get; set; }

    /// <summary>The dictionary a program reaches for device status through. Its
    /// entries mirror the probed names, so <c>statusdict /x known</c> answers.</summary>
    public PsDictionary StatusDict { get; } = new();

    private void RegisterResourceOperators()
    {
        Def("defineresource", DefineResourceOperator);
        Def("undefineresource", i =>
        {
            var category = i.PopName();
            var key = i.Pop();
            i.ResourceCategory(category).Remove(key);
        });
        Def("findresource", FindResourceOperator);
        Def("resourcestatus", ResourceStatusOperator);
        Def("resourceforall", ResourceForAllOperator);
        Def("execform", PsForms.Execute);
    }

    /// <summary>Named resource categories a program has defined into.</summary>
    private readonly Dictionary<string, PsDictionary> _resources =
        new(StringComparer.Ordinal);

    /// <summary>The dictionary backing one resource category.</summary>
    public PsDictionary ResourceCategory(string category)
    {
        if (category == "Font") return FontDirectory;
        if (!_resources.TryGetValue(category, out var dict))
        {
            dict = new PsDictionary();
            _resources[category] = dict;
        }

        return dict;
    }

    private static void DefineResourceOperator(PsInterpreter i)
    {
        var category = i.PopName();
        var instance = i.Pop();
        var key = i.Pop();
        i.ResourceCategory(category).Put(key, instance);
        i.Push(instance);
    }

    private static void FindResourceOperator(PsInterpreter i)
    {
        var category = i.PopName();
        var key = i.Pop();
        if (i.ResourceCategory(category).TryGet(key, out var value))
        {
            i.Push(value);
            return;
        }

        if (category == "Font")
        {
            i.Push(i.FindFontByName(KeyText(key)));
            return;
        }

        throw new PsErrorSignal("undefinedresource");
    }

    private static void ResourceStatusOperator(PsInterpreter i)
    {
        var category = i.PopName();
        var key = i.Pop();
        var known = i.ResourceCategory(category).TryGet(key, out _) ||
                    (category == "Font" && i.Fonts != null && i.Fonts.Knows(KeyText(key)));
        if (!known)
        {
            i.Push(false);
            return;
        }

        i.Push(0);
        i.Push(0);
        i.Push(true);
    }

    private static void ResourceForAllOperator(PsInterpreter i)
    {
        var category = i.PopName();
        i.Pop();
        var proc = i.PopProc();
        i.Pop();
        var entries = new List<PsValue>();
        foreach (var pair in i.ResourceCategory(category).Entries) entries.Add(pair.Key);
        try
        {
            foreach (var key in entries)
            {
                i.Push(key);
                i.ExecuteProcedure(proc);
            }
        }
        catch (PsExitSignal)
        {
        }
    }

    private static string KeyText(PsValue key) =>
        key.Type == PsType.String ? key.AsString!.ToString() : key.Text ?? string.Empty;

    private void RegisterFileOperators()
    {
        Def("currentfile", i => i.Push(PsValue.File(i.CurrentFile ?? new PsFile(new byte[0]))));
        Def("read", ReadOperator);
        Def("readline", ReadLineOperator);
        Def("readstring", i => ReadIntoString(i, hex: false));
        Def("readhexstring", i => ReadIntoString(i, hex: true));
        Def("bytesavailable", i => i.Push(FileOf(i.Pop()).Available));
        Def("closefile", i => FileOf(i.Pop()).Closed = true);
        Def("resetfile", i => { });
        Def("flush", i => { });
        Def("flushfile", i => i.Pop());
        Def("status", i =>
        {
            var v = i.Pop();
            i.Push(v.Type == PsType.File && !v.AsFile!.Closed);
        });
        Def("file", i =>
        {
            i.Pop();
            i.Pop();
            i.Push(PsValue.File(new PsFile(new byte[0])));
        });
        Def("filter", FilterOperator);
        Def("run", i => i.Pop());
        DefDiscard("print", 1);
        DefDiscard("write", 2);
        DefDiscard("writestring", 2);
        DefDiscard("writehexstring", 2);
        Def("=", i => i.Pop());
        Def("==", i => i.Pop());
        Def("stack", i => { });
        Def("pstack", i => { });
        Def("prompt", i => { });
        Def("bind", BindOperator);
        Def("usertime", i => i.Push(i.ElapsedMilliseconds));
    }

    private static PsFile FileOf(PsValue value)
    {
        if (value.Type != PsType.File) throw new PsErrorSignal("typecheck");
        return value.AsFile!;
    }

    private static void ReadOperator(PsInterpreter i)
    {
        var file = FileOf(i.Pop());
        var b = file.ReadByte();
        if (b < 0)
        {
            i.Push(false);
            return;
        }

        i.Push(b);
        i.Push(true);
    }

    private static void ReadLineOperator(PsInterpreter i)
    {
        var target = i.PopString();
        var file = FileOf(i.Pop());
        var count = 0;
        while (count < target.Length)
        {
            var b = file.ReadByte();
            if (b < 0) break;
            if (b == '\n' || b == '\r') break;
            target[count++] = (byte)b;
        }

        i.Push(PsValue.Str(target.Interval(0, count)));
        i.Push(count > 0 || file.Available > 0);
    }

    /// <summary>Fill a string from a file, either raw or by decoding hex digits.
    /// This is how programs feed image data to <c>image</c> and <c>colorimage</c>.</summary>
    private static void ReadIntoString(PsInterpreter i, bool hex)
    {
        var target = i.PopString();
        var file = FileOf(i.Pop());
        var filled = hex ? FillFromHex(file, target) : FillFromBytes(file, target);
        i.Push(PsValue.Str(target.Interval(0, filled)));
        i.Push(filled == target.Length);
    }

    private static int FillFromBytes(PsFile file, PsString target)
    {
        var bytes = file.Read(target.Length);
        for (var k = 0; k < bytes.Length; k++) target[k] = bytes[k];
        return bytes.Length;
    }

    private static int FillFromHex(PsFile file, PsString target)
    {
        var filled = 0;
        var high = -1;
        while (filled < target.Length)
        {
            var b = file.ReadByte();
            if (b < 0) break;
            var digit = HexDigit(b);
            if (digit < 0) continue;
            if (high < 0)
            {
                high = digit;
                continue;
            }

            target[filled++] = (byte)((high << 4) | digit);
            high = -1;
        }

        return filled;
    }

    private static int HexDigit(int c)
    {
        if (c >= '0' && c <= '9') return c - '0';
        if (c >= 'a' && c <= 'f') return c - 'a' + 10;
        if (c >= 'A' && c <= 'F') return c - 'A' + 10;
        return -1;
    }

    /// <summary><c>filter</c> wraps a source in a decoder.</summary>
    private static void FilterOperator(PsInterpreter i)
    {
        var name = i.PopName();
        var source = i.Pop();
        if (source.Type != PsType.File)
        {
            var text = source.AsString?.ToArray() ?? new byte[0];
            i.Push(PsValue.File(new PsFile(PsFilters.Decode(name, text))));
            return;
        }

        // Wrapping the program's own source: the data does not begin where the filter
        // is made — an image dictionary names its source among its other entries and
        // the samples follow the operator that draws it — so the decode waits until
        // something reads. The encoded run then ends where the filter's terminator
        // says it does, and the source is left just past it, because everything after
        // it is more PostScript.
        i.Push(PsValue.File(new PsFile(source.AsFile!, name)));
    }

    /// <summary><c>bind</c> replaces operator names in a procedure with the operators
    /// themselves, which programs rely on for speed and, occasionally, to freeze a
    /// definition against later redefinition.</summary>
    private static void BindOperator(PsInterpreter i)
    {
        var value = i.Peek();
        if (value.Type == PsType.Array && value.IsExecutable) i.BindProcedure(value.AsArray, 0);
    }

    /// <summary>How deep <c>bind</c> descends into nested procedures.</summary>
    private const int MaxBindDepth = 32;

    /// <summary>Replace every executable name in a procedure that resolves to an
    /// operator with that operator, descending into nested procedures.</summary>
    public void BindProcedure(PsArray? body, int depth)
    {
        if (body is null || depth > MaxBindDepth) return;
        for (var k = 0; k < body.Length; k++)
        {
            var item = body[k];
            if (item.Type == PsType.Array && item.IsExecutable)
            {
                BindProcedure(item.AsArray, depth + 1);
                continue;
            }

            if (item.Type != PsType.Name || !item.IsExecutable) continue;
            if (TryResolve(item, out var bound) && bound.Type == PsType.Operator) body[k] = bound;
        }
    }
}
