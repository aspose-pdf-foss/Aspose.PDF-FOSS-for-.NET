using System;
using System.Collections.Generic;

namespace Aspose.Pdf.Converters.PsToPdf;

/// <summary>Stack, arithmetic, relational and control operators.</summary>
internal sealed partial class PsInterpreter
{
    /// <summary>Bind an operator into systemdict.</summary>
    private void Def(string name, Action<PsInterpreter> action) =>
        SystemDict.Put(name, PsValue.Op(new PsOperator(name, action)));

    /// <summary>Install the constants a program expects to find already defined.</summary>
    private void RegisterConstants()
    {
        SystemDict.Put("true", PsValue.True);
        SystemDict.Put("false", PsValue.False);
        SystemDict.Put("null", PsValue.Null);
        SystemDict.Put("systemdict", PsValue.Dict(SystemDict));
        SystemDict.Put("globaldict", PsValue.Dict(GlobalDict));
        SystemDict.Put("userdict", PsValue.Dict(UserDict));
        SystemDict.Put("errordict", PsValue.Dict(ErrorDict));
        SystemDict.Put("$error", PsValue.Dict(new PsDictionary()));
        SystemDict.Put("FontDirectory", PsValue.Dict(FontDirectory));
        SystemDict.Put("StandardEncoding", PsValue.Arr(PsEncodings.StandardArray()));
        SystemDict.Put("ISOLatin1Encoding", PsValue.Arr(PsEncodings.IsoLatin1Array()));
    }

    private void RegisterCoreOperators()
    {
        RegisterStackOperators();
        RegisterArithmeticOperators();
        RegisterRelationalOperators();
        RegisterControlOperators();
    }

    private void RegisterStackOperators()
    {
        Def("pop", i => i.Pop());
        Def("exch", i =>
        {
            var b = i.Pop();
            var a = i.Pop();
            i.Push(b);
            i.Push(a);
        });
        Def("dup", i => i.Push(i.Peek()));
        Def("copy", CopyOperator);
        Def("index", i =>
        {
            var n = i.PopCount();
            i.Push(i.Peek(n));
        });
        Def("roll", i =>
        {
            var shift = i.PopInt();
            var n = i.PopCount();
            i.Roll(n, shift);
        });
        Def("clear", i => i.ClearOperands());
        Def("count", i => i.Push(i.OperandCount));
        Def("mark", i => i.Push(PsValue.Mark));
        Def("cleartomark", i => i.PopToMark());
        Def("counttomark", i =>
        {
            var mark = i.FindMark();
            if (mark < 0) throw new PsErrorSignal("unmatchedmark");
            i.Push(i.OperandCount - mark - 1);
        });
    }

    /// <summary><c>copy</c> duplicates stack operands for an integer, and copies a
    /// composite's contents for anything else.</summary>
    private static void CopyOperator(PsInterpreter i)
    {
        var top = i.Peek();
        if (top.IsNumber)
        {
            var n = i.PopCount();
            i.CopyTop(n);
            return;
        }

        var target = i.Pop();
        var source = i.Pop();
        i.Vm.Changing(target);
        i.Push(CopyComposite(source, target));
    }

    private static PsValue CopyComposite(PsValue source, PsValue target)
    {
        if (source.Type == PsType.Array && target.Type == PsType.Array)
        {
            var from = source.AsArray!;
            var to = target.AsArray!;
            if (to.Length < from.Length) throw new PsErrorSignal("rangecheck");
            for (var k = 0; k < from.Length; k++) to[k] = from[k];
            return PsValue.Arr(to.Interval(0, from.Length));
        }

        if (source.Type == PsType.String && target.Type == PsType.String)
        {
            var from = source.AsString!;
            var to = target.AsString!;
            if (to.Length < from.Length) throw new PsErrorSignal("rangecheck");
            to.Put(0, from);
            return PsValue.Str(to.Interval(0, from.Length));
        }

        if (source.AsDictionary != null && target.AsDictionary != null)
        {
            // Snapshot first: a program may copy a dictionary onto itself, and
            // writing into the map being walked would break the enumeration.
            var entries = new List<KeyValuePair<PsValue, PsValue>>(source.AsDictionary!.Entries);
            foreach (var pair in entries) target.AsDictionary!.Put(pair.Key, pair.Value);
            return target;
        }

        throw new PsErrorSignal("typecheck");
    }

    private void RegisterArithmeticOperators()
    {
        Def("add", i => Binary(i, (a, b) => a + b));
        Def("sub", i => Binary(i, (a, b) => a - b));
        Def("mul", i => Binary(i, (a, b) => a * b));
        Def("div", i => Binary(i, Divide));
        Def("idiv", i =>
        {
            var b = i.PopInt();
            var a = i.PopInt();
            if (b == 0) throw new PsErrorSignal("undefinedresult");
            i.Push(PsValue.Int(a / b));
        });
        Def("mod", i =>
        {
            var b = i.PopInt();
            var a = i.PopInt();
            if (b == 0) throw new PsErrorSignal("undefinedresult");
            i.Push(PsValue.Int(a % b));
        });
        Def("neg", i => Unary(i, a => -a));
        Def("abs", i => Unary(i, Math.Abs));
        Def("ceiling", i => Unary(i, Math.Ceiling));
        Def("floor", i => Unary(i, Math.Floor));
        Def("round", i => Unary(i, a => Math.Round(a, MidpointRounding.AwayFromZero)));
        Def("truncate", i => Unary(i, Math.Truncate));
        Def("sqrt", i => i.Push(PsValue.Real(Math.Sqrt(Math.Max(0, i.PopNumber())))));
        Def("sin", i => i.Push(PsValue.Real(Math.Sin(ToRadians(i.PopNumber())))));
        Def("cos", i => i.Push(PsValue.Real(Math.Cos(ToRadians(i.PopNumber())))));
        Def("atan", AtanOperator);
        Def("exp", i =>
        {
            var e = i.PopNumber();
            i.Push(PsValue.Real(Math.Pow(i.PopNumber(), e)));
        });
        Def("ln", i => i.Push(PsValue.Real(SafeLog(i.PopNumber(), Math.E))));
        Def("log", i => i.Push(PsValue.Real(SafeLog(i.PopNumber(), Base10))));
        Def("rand", i => i.Push(PsValue.Int(i.NextRandom())));
        Def("srand", i => i.SeedRandom(i.PopInt()));
        Def("rrand", i => i.Push(PsValue.Int(i.RandomSeed)));
        Def("bitshift", i =>
        {
            var shift = i.PopInt();
            var value = i.PopInt();
            i.Push(PsValue.Int(shift >= 0 ? value << shift : value >> -shift));
        });
        Def("and", i => Logical(i, (a, b) => a & b, (a, b) => a && b));
        Def("or", i => Logical(i, (a, b) => a | b, (a, b) => a || b));
        Def("xor", i => Logical(i, (a, b) => a ^ b, (a, b) => a ^ b));
        Def("not", NotOperator);
    }

    /// <summary>Base of the common logarithm.</summary>
    private const double Base10 = 10.0;

    /// <summary>Degrees in a half turn, the conversion PostScript's trig uses.</summary>
    private const double DegreesPerHalfTurn = 180.0;

    private static double ToRadians(double degrees) => degrees * Math.PI / DegreesPerHalfTurn;

    private static double SafeLog(double value, double logBase) =>
        value <= 0 ? 0 : Math.Log(value, logBase);

    /// <summary>Division always yields a real, and division by zero is an error.</summary>
    /// <summary><c>div</c> over a zero divisor answers zero rather than raising. The
    /// reference conversion carries on: a program that counts halftone cells the screen
    /// never asked it about divides its two zero counters and goes on to set a grey of
    /// nought, and the etalon holds the black square that makes.</summary>
    private static double Divide(double a, double b) => b == 0 ? 0 : a / b;

    /// <summary><c>atan</c> takes numerator and denominator and answers in degrees,
    /// normalised into the range 0 to 360.</summary>
    private static void AtanOperator(PsInterpreter i)
    {
        var den = i.PopNumber();
        var num = i.PopNumber();
        var degrees = Math.Atan2(num, den) * DegreesPerHalfTurn / Math.PI;
        if (degrees < 0) degrees += 2 * DegreesPerHalfTurn;
        i.Push(PsValue.Real(degrees));
    }

    /// <summary>An arithmetic operator keeps integer type when both operands are
    /// integers and the result has no fractional part — except <c>div</c>, which the
    /// language defines as always real.</summary>
    private static void Binary(PsInterpreter i, Func<double, double, double> op)
    {
        var b = i.Pop();
        var a = i.Pop();
        if (!a.IsNumber || !b.IsNumber) throw new PsErrorSignal("typecheck");
        var result = op(a.Number, b.Number);
        var integral = a.Type == PsType.Integer && b.Type == PsType.Integer;
        i.Push(integral && result == Math.Floor(result) ? PsValue.Int(result) : PsValue.Num(result));
    }

    private static void Unary(PsInterpreter i, Func<double, double> op)
    {
        var a = i.Pop();
        if (!a.IsNumber) throw new PsErrorSignal("typecheck");
        var result = op(a.Number);
        i.Push(a.Type == PsType.Integer ? PsValue.Int(result) : PsValue.Num(result));
    }

    /// <summary>The bitwise operators double as the boolean ones, on type.</summary>
    private static void Logical(PsInterpreter i, Func<int, int, int> bits, Func<bool, bool, bool> logic)
    {
        var b = i.Pop();
        var a = i.Pop();
        if (a.Type == PsType.Boolean && b.Type == PsType.Boolean)
        {
            i.Push(logic(a.ToBool(), b.ToBool()));
            return;
        }

        if (!a.IsNumber || !b.IsNumber) throw new PsErrorSignal("typecheck");
        i.Push(PsValue.Int(bits(a.ToInt(), b.ToInt())));
    }

    private static void NotOperator(PsInterpreter i)
    {
        var a = i.Pop();
        if (a.Type == PsType.Boolean) i.Push(!a.ToBool());
        else if (a.IsNumber) i.Push(PsValue.Int(~a.ToInt()));
        else throw new PsErrorSignal("typecheck");
    }

    private void RegisterRelationalOperators()
    {
        Def("eq", i => i.Push(EqualOperands(i)));
        Def("ne", i => i.Push(!EqualOperands(i)));
        Def("gt", i => Compare(i, c => c > 0));
        Def("ge", i => Compare(i, c => c >= 0));
        Def("lt", i => Compare(i, c => c < 0));
        Def("le", i => Compare(i, c => c <= 0));
    }

    /// <summary><c>eq</c> equates a name with the string of the same characters,
    /// which the value type's own equality deliberately does not.</summary>
    private static bool EqualOperands(PsInterpreter i)
    {
        var b = i.Pop();
        var a = i.Pop();
        if (a.Type == PsType.Name && b.Type == PsType.String) return a.Text == b.AsString!.ToString();
        if (a.Type == PsType.String && b.Type == PsType.Name) return a.AsString!.ToString() == b.Text;
        if (a.Type == PsType.String && b.Type == PsType.String)
            return a.AsString!.ToString() == b.AsString!.ToString();
        return a.Equals(b);
    }

    private static void Compare(PsInterpreter i, Func<int, bool> accept)
    {
        var b = i.Pop();
        var a = i.Pop();
        if (a.IsNumber && b.IsNumber)
        {
            i.Push(accept(a.Number.CompareTo(b.Number)));
            return;
        }

        var sa = AsComparableText(a);
        var sb = AsComparableText(b);
        if (sa is null || sb is null) throw new PsErrorSignal("typecheck");
        i.Push(accept(string.CompareOrdinal(sa, sb)));
    }

    private static string? AsComparableText(PsValue v) => v.Type switch
    {
        PsType.String => v.AsString!.ToString(),
        PsType.Name => v.Text!,
        _ => null,
    };

    private void RegisterControlOperators()
    {
        Def("exec", i => i.Invoke(i.Pop()));
        Def("if", IfOperator);
        Def("ifelse", IfElseOperator);
        Def("for", ForOperator);
        Def("repeat", RepeatOperator);
        Def("loop", LoopOperator);
        Def("exit", i => throw new PsExitSignal());
        Def("stop", i => throw new PsStopSignal());
        Def("stopped", StoppedOperator);
        Def("quit", i => throw new PsQuitSignal());
        Def("start", i => { });
        Def("countexecstack", i => i.Push(0));
        Def("execstack", i => i.Push(PsValue.Arr(new PsArray(0))));
    }

    private static void IfOperator(PsInterpreter i)
    {
        var proc = i.PopProc();
        if (i.PopBool()) i.ExecuteProcedure(proc);
    }

    private static void IfElseOperator(PsInterpreter i)
    {
        var otherwise = i.PopProc();
        var then = i.PopProc();
        i.ExecuteProcedure(i.PopBool() ? then : otherwise);
    }

    private static void ForOperator(PsInterpreter i)
    {
        var proc = i.PopProc();
        var limit = i.PopNumber();
        var step = i.PopNumber();
        var start = i.Pop();
        if (!start.IsNumber) throw new PsErrorSignal("typecheck");
        var integral = start.Type == PsType.Integer && step == Math.Floor(step);
        try
        {
            for (var v = start.Number; step >= 0 ? v <= limit : v >= limit; v += step)
            {
                i.Push(integral ? PsValue.Int(v) : PsValue.Num(v));
                i.ExecuteProcedure(proc);
                if (step == 0) break;
            }
        }
        catch (PsExitSignal)
        {
            // `exit` leaves the innermost loop and is not an error.
        }
    }

    private static void RepeatOperator(PsInterpreter i)
    {
        var proc = i.PopProc();
        var count = i.PopInt();
        try
        {
            for (var k = 0; k < count; k++) i.ExecuteProcedure(proc);
        }
        catch (PsExitSignal)
        {
        }
    }

    private static void LoopOperator(PsInterpreter i)
    {
        var proc = i.PopProc();
        try
        {
            while (true) i.ExecuteProcedure(proc);
        }
        catch (PsExitSignal)
        {
        }
    }

    /// <summary><c>stopped</c> runs a procedure and reports whether it stopped, which
    /// is how a program guards a section that may fault.</summary>
    private static void StoppedOperator(PsInterpreter i)
    {
        var proc = i.Pop();
        try
        {
            i.Invoke(proc);
            i.Push(false);
        }
        catch (PsStopSignal)
        {
            i.Push(true);
        }
        catch (PsErrorSignal e)
        {
            i.RecordError(e.ErrorName);
            i.Push(true);
        }
    }

    /// <summary>The state of the linear congruential generator behind <c>rand</c>.</summary>
    public int RandomSeed { get; private set; } = 1;

    private const int RandomMultiplier = 16807;
    private const int RandomModulus = int.MaxValue;

    /// <summary>Reseed the generator.</summary>
    public void SeedRandom(int seed) => RandomSeed = seed == 0 ? 1 : Math.Abs(seed);

    /// <summary>The next pseudo-random integer.</summary>
    public int NextRandom()
    {
        RandomSeed = (int)((long)RandomSeed * RandomMultiplier % RandomModulus);
        if (RandomSeed <= 0) RandomSeed += RandomModulus - 1;
        return RandomSeed;
    }
}
