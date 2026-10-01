using System;
using System.Collections.Generic;
using System.Globalization;

namespace Aspose.Pdf.Converters.PsToPdf;

/// <summary>Array, dictionary, string, type-conversion and virtual-memory operators.</summary>
internal sealed partial class PsInterpreter
{
    private void RegisterDataOperators()
    {
        RegisterCompositeOperators();
        RegisterDictOperators();
        RegisterStringOperators();
        RegisterTypeOperators();
        RegisterVmOperators();
    }

    private void RegisterCompositeOperators()
    {
        Def("array", i => i.Push(PsValue.Arr(new PsArray(i.PopCount()))));
        Def("packedarray", i =>
        {
            var items = new List<PsValue>();
            var n = i.PopCount();
            for (var k = 0; k < n; k++) items.Insert(0, i.Pop());
            i.Push(PsValue.Arr(new PsArray(items)));
        });
        Def("[", i => i.Push(PsValue.Mark));
        Def("]", i => i.Push(PsValue.Arr(new PsArray(i.PopToMark()))));
        Def("length", LengthOperator);
        Def("get", GetOperator);
        Def("put", PutOperator);
        Def("getinterval", GetIntervalOperator);
        Def("putinterval", PutIntervalOperator);
        Def("aload", AloadOperator);
        Def("astore", AstoreOperator);
        Def("forall", ForAllOperator);
    }

    private static void LengthOperator(PsInterpreter i)
    {
        var v = i.Pop();
        i.Push(v.Type switch
        {
            PsType.Array => v.AsArray!.Length,
            PsType.String => v.AsString!.Length,
            PsType.Dictionary or PsType.Font => v.AsDictionary!.Count,
            PsType.Name => v.Text!.Length,
            _ => throw new PsErrorSignal("typecheck"),
        });
    }

    private static void GetOperator(PsInterpreter i)
    {
        var key = i.Pop();
        var target = i.Pop();
        switch (target.Type)
        {
            case PsType.Array:
                i.Push(target.AsArray![CheckIndex(key, target.AsArray!.Length)]);
                return;
            case PsType.String:
                i.Push(target.AsString![CheckIndex(key, target.AsString!.Length)]);
                return;
            case PsType.Dictionary:
            case PsType.Font:
                if (!target.AsDictionary!.TryGet(key, out var value)) throw new PsErrorSignal("undefined");
                i.Push(value);
                return;
            default:
                throw new PsErrorSignal("typecheck");
        }
    }

    private static void PutOperator(PsInterpreter i)
    {
        var value = i.Pop();
        var key = i.Pop();
        var target = i.Pop();
        i.Vm.Changing(target);
        switch (target.Type)
        {
            case PsType.Array:
                target.AsArray![CheckIndex(key, target.AsArray!.Length)] = value;
                return;
            case PsType.String:
                if (!value.IsNumber) throw new PsErrorSignal("typecheck");
                target.AsString![CheckIndex(key, target.AsString!.Length)] = (byte)value.ToInt();
                return;
            case PsType.Dictionary:
            case PsType.Font:
                if (target.AsDictionary!.ReadOnly) throw new PsErrorSignal("invalidaccess");
                target.AsDictionary!.Put(key, value);
                return;
            default:
                throw new PsErrorSignal("typecheck");
        }
    }

    private static int CheckIndex(PsValue key, int length)
    {
        if (!key.IsNumber) throw new PsErrorSignal("typecheck");
        var index = key.ToInt();
        if (index < 0 || index >= length) throw new PsErrorSignal("rangecheck");
        return index;
    }

    private static void GetIntervalOperator(PsInterpreter i)
    {
        var count = i.PopCount();
        var index = i.PopCount();
        var target = i.Pop();
        if (target.Type == PsType.Array)
        {
            CheckInterval(index, count, target.AsArray!.Length);
            i.Push(PsValue.Arr(target.AsArray!.Interval(index, count)));
            return;
        }

        if (target.Type != PsType.String) throw new PsErrorSignal("typecheck");
        CheckInterval(index, count, target.AsString!.Length);
        i.Push(PsValue.Str(target.AsString!.Interval(index, count)));
    }

    private static void PutIntervalOperator(PsInterpreter i)
    {
        var source = i.Pop();
        var index = i.PopCount();
        var target = i.Pop();
        i.Vm.Changing(target);
        if (target.Type == PsType.Array && source.Type == PsType.Array)
        {
            CheckInterval(index, source.AsArray!.Length, target.AsArray!.Length);
            for (var k = 0; k < source.AsArray!.Length; k++) target.AsArray![index + k] = source.AsArray![k];
            return;
        }

        if (target.Type != PsType.String || source.Type != PsType.String)
            throw new PsErrorSignal("typecheck");
        CheckInterval(index, source.AsString!.Length, target.AsString!.Length);
        target.AsString!.Put(index, source.AsString);
    }

    private static void CheckInterval(int index, int count, int length)
    {
        if (index + count > length) throw new PsErrorSignal("rangecheck");
    }

    private static void AloadOperator(PsInterpreter i)
    {
        var value = i.Pop();
        var array = value.AsArray;
        if (array is null) throw new PsErrorSignal("typecheck");
        for (var k = 0; k < array.Length; k++) i.Push(array[k]);
        i.Push(value);
    }

    private static void AstoreOperator(PsInterpreter i)
    {
        var value = i.Pop();
        var array = value.AsArray;
        if (array is null) throw new PsErrorSignal("typecheck");
        i.Vm.Changing(array);
        for (var k = array.Length - 1; k >= 0; k--) array[k] = i.Pop();
        i.Push(value);
    }

    private static void ForAllOperator(PsInterpreter i)
    {
        var proc = i.PopProc();
        var target = i.Pop();
        try
        {
            ForAllBody(i, proc, target);
        }
        catch (PsExitSignal)
        {
        }
    }

    private static void ForAllBody(PsInterpreter i, PsArray proc, PsValue target)
    {
        switch (target.Type)
        {
            case PsType.Array:
                for (var k = 0; k < target.AsArray!.Length; k++)
                {
                    i.Push(target.AsArray![k]);
                    i.ExecuteProcedure(proc);
                }

                return;
            case PsType.String:
                for (var k = 0; k < target.AsString!.Length; k++)
                {
                    i.Push(target.AsString![k]);
                    i.ExecuteProcedure(proc);
                }

                return;
            case PsType.Dictionary:
            case PsType.Font:
                foreach (var pair in new List<KeyValuePair<PsValue, PsValue>>(target.AsDictionary!.Entries))
                {
                    i.Push(pair.Key);
                    i.Push(pair.Value);
                    i.ExecuteProcedure(proc);
                }

                return;
            default:
                throw new PsErrorSignal("typecheck");
        }
    }

    private void RegisterDictOperators()
    {
        Def("dict", i => i.Push(PsValue.Dict(new PsDictionary(i.PopCount()))));
        Def("<<", i => i.Push(PsValue.Mark));
        Def(">>", DictFromMarkOperator);
        Def("begin", i => i.PushDict(i.PopDict()));
        Def("end", i => i.PopDictStack());
        Def("def", i =>
        {
            var value = i.Pop();
            var key = i.Pop();
            i.Vm.Changing(i.CurrentDict);
            i.CurrentDict.Put(key, value);
        });
        Def("store", StoreOperator);
        Def("load", i =>
        {
            var key = i.Pop();
            if (!i.TryResolve(key, out var value)) throw new PsErrorSignal("undefined");
            i.Push(value);
        });
        Def("known", i =>
        {
            var key = i.Pop();
            i.Push(i.PopDict().TryGet(key, out _));
        });
        Def("where", WhereOperator);
        Def("undef", i =>
        {
            var key = i.Pop();
            var dict = i.PopDict();
            i.Vm.Changing(dict);
            dict.Remove(key);
        });
        Def("maxlength", i => i.Push(i.PopDict().Capacity));
        Def("currentdict", i => i.Push(PsValue.Dict(i.CurrentDict)));
        Def("countdictstack", i => i.Push(i.DictStackCount));
        Def("dictstack", DictStackOperator);
        Def("cleardictstack", i => i.ClearDictStack());
    }

    private static void DictFromMarkOperator(PsInterpreter i)
    {
        var items = i.PopToMark();
        var dict = new PsDictionary(items.Count / 2);
        for (var k = 0; k + 1 < items.Count; k += 2) dict.Put(items[k], items[k + 1]);
        i.Push(PsValue.Dict(dict));
    }

    /// <summary><c>store</c> writes where the name is already defined, and falls back
    /// to the current dictionary when it is defined nowhere.</summary>
    private static void StoreOperator(PsInterpreter i)
    {
        var value = i.Pop();
        var key = i.Pop();
        var dict = i.FindDictionary(key) ?? i.CurrentDict;
        i.Vm.Changing(dict);
        dict.Put(key, value);
    }

    private static void WhereOperator(PsInterpreter i)
    {
        var key = i.Pop();
        var dict = i.FindDictionary(key);
        if (dict is null)
        {
            i.Push(false);
            return;
        }

        i.Push(PsValue.Dict(dict));
        i.Push(true);
    }

    private static void DictStackOperator(PsInterpreter i)
    {
        var array = i.PopArray();
        var n = Math.Min(array.Length, i.DictStackCount);
        for (var k = 0; k < n; k++) array[k] = PsValue.Dict(i.DictAt(i.DictStackCount - 1 - k));
        i.Push(PsValue.Arr(array.Interval(0, n)));
    }

    private void RegisterStringOperators()
    {
        Def("string", i => i.Push(PsValue.Str(new PsString(i.PopCount()))));
        Def("search", SearchOperator);
        Def("anchorsearch", AnchorSearchOperator);
        Def("token", TokenOperator);
    }

    /// <summary><c>search</c> splits a string at the first occurrence of a seek
    /// string, leaving post/match/pre/true, or the original string and false.</summary>
    private static void SearchOperator(PsInterpreter i)
    {
        var seek = i.PopString();
        var subject = i.PopString();
        var at = IndexOf(subject, seek, anchored: false);
        if (at < 0)
        {
            i.Push(PsValue.Str(subject));
            i.Push(false);
            return;
        }

        // The three pieces are left with what FOLLOWS the match at the bottom and what
        // came BEFORE it on top, which is the order a program reads them back in: the
        // word it just took, then the separator, then the rest of the text.
        i.Push(PsValue.Str(subject.Interval(at + seek.Length, subject.Length - at - seek.Length)));
        i.Push(PsValue.Str(subject.Interval(at, seek.Length)));
        i.Push(PsValue.Str(subject.Interval(0, at)));
        i.Push(true);
    }

    private static void AnchorSearchOperator(PsInterpreter i)
    {
        var seek = i.PopString();
        var subject = i.PopString();
        if (IndexOf(subject, seek, anchored: true) != 0)
        {
            i.Push(PsValue.Str(subject));
            i.Push(false);
            return;
        }

        i.Push(PsValue.Str(subject.Interval(seek.Length, subject.Length - seek.Length)));
        i.Push(PsValue.Str(subject.Interval(0, seek.Length)));
        i.Push(true);
    }

    private static int IndexOf(PsString subject, PsString seek, bool anchored)
    {
        if (seek.Length == 0) return 0;
        var last = anchored ? 0 : subject.Length - seek.Length;
        for (var start = 0; start <= last; start++)
        {
            var match = true;
            for (var k = 0; k < seek.Length && match; k++) match = subject[start + k] == seek[k];
            if (match) return start;
        }

        return -1;
    }

    /// <summary><c>token</c> scans the next object out of a string, leaving the rest
    /// of the string, the object and true, or just false. Out of a FILE it leaves the
    /// object and true, or false, and the file is left just past what was read — the
    /// idiom a program uses to read its own coordinates out of its source.</summary>
    private static void TokenOperator(PsInterpreter i)
    {
        var subject = i.Pop();
        if (subject.Type == PsType.File)
        {
            TokenFromFile(i, subject.AsFile!);
            return;
        }

        if (subject.Type != PsType.String) throw new PsErrorSignal("typecheck");
        var text = subject.AsString!;
        var scanner = new PsScanner(text.ToArray());
        if (!scanner.TryRead(out var token))
        {
            i.Push(false);
            return;
        }

        i.Push(PsValue.Str(text.Interval(scanner.Position, text.Length - scanner.Position)));
        i.Push(token);
        i.Push(true);
    }

    /// <summary>Read one object out of a file, leaving the file just past it.</summary>
    private static void TokenFromFile(PsInterpreter i, PsFile file)
    {
        var start = file.Position;
        var rest = file.Read(file.Available);
        var scanner = new PsScanner(rest);
        if (!scanner.TryRead(out var token))
        {
            i.Push(false);
            return;
        }

        file.Position = start + scanner.Position;
        i.Push(token);
        i.Push(true);
    }

    private void RegisterTypeOperators()
    {
        Def("type", i => i.Push(PsValue.ExecName(i.Pop().TypeName).WithExecutable(false)));
        Def("cvlit", i => i.Push(i.Pop().WithExecutable(false)));
        Def("cvx", i => i.Push(i.Pop().WithExecutable(true)));
        Def("xcheck", i => i.Push(i.Pop().IsExecutable));
        Def("cvi", i => i.Push(PsValue.Int(Math.Truncate(NumericOf(i.Pop())))));
        Def("cvr", i => i.Push(PsValue.Real(NumericOf(i.Pop()))));
        Def("cvn", i => i.Push(PsValue.LiteralName(i.PopString().ToString())));
        Def("cvs", CvsOperator);
        Def("cvrs", CvrsOperator);
        Def("executeonly", i => { });
        Def("noaccess", i => { });
        Def("readonly", ReadOnlyOperator);
        Def("rcheck", i =>
        {
            i.Pop();
            i.Push(true);
        });
        Def("wcheck", i => i.Push(!(i.Pop().AsDictionary?.ReadOnly ?? false)));
    }

    private static double NumericOf(PsValue v)
    {
        if (v.IsNumber) return v.Number;
        if (v.Type == PsType.String && PsScanner.TryParseNumber(v.AsString!.ToString(), out var parsed))
            return parsed.Number;
        throw new PsErrorSignal("typecheck");
    }

    private static void ReadOnlyOperator(PsInterpreter i)
    {
        var v = i.Peek();
        if (v.AsDictionary != null) v.AsDictionary!.ReadOnly = true;
    }

    private static void CvsOperator(PsInterpreter i)
    {
        var target = i.PopString();
        var value = i.Pop();
        var text = TextOf(value);
        if (text.Length > target.Length) throw new PsErrorSignal("rangecheck");
        target.Put(0, PsString.FromText(text));
        i.Push(PsValue.Str(target.Interval(0, text.Length)));
    }

    /// <summary><c>cvrs</c> renders a number in a given radix.</summary>
    private static void CvrsOperator(PsInterpreter i)
    {
        var target = i.PopString();
        var radix = i.PopInt();
        var value = (long)NumericOf(i.Pop());
        var text = ToRadixText(value, radix);
        if (text.Length > target.Length) throw new PsErrorSignal("rangecheck");
        target.Put(0, PsString.FromText(text));
        i.Push(PsValue.Str(target.Interval(0, text.Length)));
    }

    private static string ToRadixText(long value, int radix)
    {
        if (radix < 2 || radix > 36) throw new PsErrorSignal("rangecheck");
        if (value == 0) return "0";
        const string Digits = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        var negative = value < 0;
        var magnitude = Math.Abs(value);
        var text = string.Empty;
        while (magnitude > 0)
        {
            text = Digits[(int)(magnitude % radix)] + text;
            magnitude /= radix;
        }

        return negative ? "-" + text : text;
    }

    /// <summary>The text <c>cvs</c> produces: a number's digits, a name's or string's
    /// characters, and the type name for anything else.</summary>
    private static string TextOf(PsValue v) => v.Type switch
    {
        PsType.Integer => ((long)v.Number).ToString(CultureInfo.InvariantCulture),
        PsType.Real => FormatReal(v.Number),
        PsType.Boolean => v.ToBool() ? "true" : "false",
        PsType.Name => v.Text!,
        PsType.String => v.AsString!.ToString(),
        PsType.Null => "null",
        _ => "--nostringval--",
    };

    /// <summary>How many significant digits a real is written with.</summary>
    private const int RealDigits = 6;

    /// <summary>PostScript writes a real with up to six significant digits and no
    /// exponent for the magnitudes a page can hold.</summary>
    private static string FormatReal(double value)
    {
        var text = value.ToString("G" + RealDigits.ToString(CultureInfo.InvariantCulture),
            CultureInfo.InvariantCulture);
        // Out of the range the shorthand covers the language writes an exponent, which
        // this form already gives; inside it a real always carries a point, whole or
        // not, so a program printing a coordinate writes "594.0" and never "594".
        if (text.IndexOf('E') >= 0) return text;
        return text.IndexOf('.') < 0 ? text + ".0" : text;
    }

    private void RegisterVmOperators()
    {
        Def("save", i =>
        {
            i.SaveDepths.Add(i.Graphics.StackDepth);
            i.Vm.Save();
            i.Push(PsValue.SaveToken(i.SaveDepths.Count));
        });
        Def("restore", RestoreOperator);
        Def("gsave", i => i.Graphics.GSave());
        Def("grestore", i => i.Graphics.GRestore());
        Def("grestoreall", i => i.Graphics.GRestoreAll());
        Def("setglobal", i => i.PopBool());
        Def("currentglobal", i => i.Push(false));
        Def("gcheck", i =>
        {
            i.Pop();
            i.Push(false);
        });
        Def("vmstatus", i =>
        {
            i.Push(0);
            i.Push(0);
            i.Push(0);
        });
        Def("vmreclaim", i => i.PopInt());
        Def("startjob", i =>
        {
            i.Pop();
            i.PopBool();
            i.Push(true);
        });
        Def("defineuserobject", i =>
        {
            i.Pop();
            i.PopInt();
        });
        Def("undefineuserobject", i => i.PopInt());
        Def("execuserobject", i => i.PopInt());
    }

    /// <summary>The graphics-stack depth each open <c>save</c> recorded, so that the
    /// matching <c>restore</c> can put the graphics state back where it was.</summary>
    public List<int> SaveDepths { get; } = new();

    /// <summary>The journal that lets a <c>restore</c> put back what a program wrote
    /// into a string, an array or a dictionary after the matching <c>save</c>.</summary>
    public PsVirtualMemory Vm { get; } = new();

    private static void RestoreOperator(PsInterpreter i)
    {
        var token = i.Pop();
        if (token.Type != PsType.Save) throw new PsErrorSignal("typecheck");
        var level = token.ToInt();
        if (level <= 0 || level > i.SaveDepths.Count) throw new PsErrorSignal("invalidrestore");
        i.Graphics.UnwindTo(i.SaveDepths[level - 1]);
        i.Vm.RestoreTo(level);
        i.SaveDepths.RemoveRange(level - 1, i.SaveDepths.Count - level + 1);
    }
}
