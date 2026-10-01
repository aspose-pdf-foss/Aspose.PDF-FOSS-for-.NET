using System;
using System.Collections.Generic;

namespace Aspose.Pdf.Converters.PsToPdf;

/// <summary>Operand- and dictionary-stack access. Every operator reaches its
/// arguments through these, so the type and range checks the language requires live
/// in one place instead of in each operator.</summary>
internal sealed partial class PsInterpreter
{
    /// <summary>How many operands are on the stack.</summary>
    public int OperandCount => _operands.Count;

    /// <summary>How many dictionaries are on the dictionary stack.</summary>
    public int DictStackCount => _dictStack.Count;

    /// <summary>Push an operand.</summary>
    public void Push(PsValue value)
    {
        if (_operands.Count >= OperandLimit) throw new PsErrorSignal("stackoverflow");
        _operands.Add(value);
    }

    /// <summary>Push a number.</summary>
    public void Push(double value) => Push(PsValue.Num(value));

    /// <summary>Push a boolean.</summary>
    public void Push(bool value) => Push(PsValue.Bool(value));

    /// <summary>Pop an operand.</summary>
    public PsValue Pop()
    {
        if (_operands.Count == 0) throw new PsErrorSignal("stackunderflow");
        var value = _operands[_operands.Count - 1];
        _operands.RemoveAt(_operands.Count - 1);
        return value;
    }

    /// <summary>The operand <paramref name="depth"/> places below the top, without
    /// removing it. Depth 0 is the top.</summary>
    public PsValue Peek(int depth = 0)
    {
        var i = _operands.Count - 1 - depth;
        if (i < 0) throw new PsErrorSignal("stackunderflow");
        return _operands[i];
    }

    /// <summary>Replace the operand <paramref name="depth"/> places below the top.</summary>
    public void Poke(int depth, PsValue value)
    {
        var i = _operands.Count - 1 - depth;
        if (i < 0) throw new PsErrorSignal("stackunderflow");
        _operands[i] = value;
    }

    /// <summary>Discard every operand.</summary>
    public void ClearOperands() => _operands.Clear();

    /// <summary>Pop a number, rejecting anything else.</summary>
    public double PopNumber()
    {
        var v = Pop();
        if (!v.IsNumber) throw new PsErrorSignal("typecheck");
        return v.Number;
    }

    /// <summary>Pop an integer, rejecting anything else.</summary>
    public int PopInt()
    {
        var v = Pop();
        if (!v.IsNumber) throw new PsErrorSignal("typecheck");
        return v.ToInt();
    }

    /// <summary>Pop a non-negative integer, rejecting a negative one.</summary>
    public int PopCount()
    {
        var n = PopInt();
        if (n < 0) throw new PsErrorSignal("rangecheck");
        return n;
    }

    /// <summary>Pop a boolean, rejecting anything else.</summary>
    public bool PopBool()
    {
        var v = Pop();
        if (v.Type != PsType.Boolean) throw new PsErrorSignal("typecheck");
        return v.ToBool();
    }

    /// <summary>Pop a string, rejecting anything else.</summary>
    public PsString PopString()
    {
        var v = Pop();
        if (v.Type != PsType.String) throw new PsErrorSignal("typecheck");
        return v.AsString!;
    }

    /// <summary>Pop an array, rejecting anything else.</summary>
    public PsArray PopArray()
    {
        var v = Pop();
        if (v.Type != PsType.Array) throw new PsErrorSignal("typecheck");
        return v.AsArray!;
    }

    /// <summary>Pop a dictionary or a font, rejecting anything else.</summary>
    public PsDictionary PopDict()
    {
        var v = Pop();
        if (v.Type != PsType.Dictionary && v.Type != PsType.Font) throw new PsErrorSignal("typecheck");
        return v.AsDictionary!;
    }

    /// <summary>Pop a procedure body, accepting any array.</summary>
    public PsArray PopProc() => PopArray();

    /// <summary>Pop a name, accepting the string form some operators allow.</summary>
    public string PopName()
    {
        var v = Pop();
        if (v.Type == PsType.Name) return v.Text!;
        if (v.Type == PsType.String) return v.AsString!.ToString();
        throw new PsErrorSignal("typecheck");
    }

    /// <summary>Pop six numbers as a transformation matrix, most-recent last.</summary>
    public PsMatrix PopMatrixOperands()
    {
        var f = PopNumber();
        var e = PopNumber();
        var d = PopNumber();
        var c = PopNumber();
        var b = PopNumber();
        var a = PopNumber();
        return new PsMatrix(a, b, c, d, e, f);
    }

    /// <summary>Read a matrix out of a six-element array.</summary>
    public static PsMatrix MatrixFromArray(PsArray? array)
    {
        if (array is null || array.Length < PsMatrix.ElementCount) throw new PsErrorSignal("rangecheck");
        var m = new double[PsMatrix.ElementCount];
        for (var i = 0; i < PsMatrix.ElementCount; i++)
        {
            if (!array[i].IsNumber) throw new PsErrorSignal("typecheck");
            m[i] = array[i].Number;
        }

        return new PsMatrix(m[0], m[1], m[2], m[3], m[4], m[5]);
    }

    /// <summary>Write a matrix into a six-element array and return it.</summary>
    public static PsArray MatrixToArray(PsArray? target, PsMatrix matrix)
    {
        var array = target ?? new PsArray(PsMatrix.ElementCount);
        if (array.Length < PsMatrix.ElementCount) throw new PsErrorSignal("rangecheck");
        var values = matrix.ToElements();
        // A matrix holds REALS, whole numbers included, which is what a program reading
        // one back and printing it writes out.
        for (var i = 0; i < PsMatrix.ElementCount; i++) array[i] = PsValue.Real(values[i]);
        return array;
    }

    /// <summary>The topmost dictionary, the one <c>def</c> writes into.</summary>
    public PsDictionary CurrentDict => _dictStack[_dictStack.Count - 1];

    /// <summary>Push a dictionary onto the dictionary stack.</summary>
    public void PushDict(PsDictionary dict)
    {
        if (dict is null) throw new PsErrorSignal("typecheck");
        _dictStack.Add(dict);
    }

    /// <summary>Pop the dictionary stack, refusing to remove the permanent three.</summary>
    public PsDictionary PopDictStack()
    {
        if (_dictStack.Count <= PermanentDictCount) throw new PsErrorSignal("dictstackunderflow");
        var dict = _dictStack[_dictStack.Count - 1];
        _dictStack.RemoveAt(_dictStack.Count - 1);
        return dict;
    }

    /// <summary>The dictionary <paramref name="depth"/> places below the top.</summary>
    public PsDictionary DictAt(int depth) => _dictStack[_dictStack.Count - 1 - depth];

    /// <summary>Drop every dictionary above the permanent three.</summary>
    public void ClearDictStack() => _dictStack.RemoveRange(PermanentDictCount,
        _dictStack.Count - PermanentDictCount);

    /// <summary>systemdict, globaldict and userdict are always on the stack.</summary>
    private const int PermanentDictCount = 3;

    /// <summary>The index of the topmost mark, or -1 when there is none.</summary>
    public int FindMark()
    {
        for (var i = _operands.Count - 1; i >= 0; i--)
            if (_operands[i].Type == PsType.Mark)
                return i;
        return -1;
    }

    /// <summary>Pop everything above the topmost mark, and the mark itself.</summary>
    public List<PsValue> PopToMark()
    {
        var mark = FindMark();
        if (mark < 0) throw new PsErrorSignal("unmatchedmark");
        var items = new List<PsValue>(_operands.Count - mark - 1);
        for (var i = mark + 1; i < _operands.Count; i++) items.Add(_operands[i]);
        _operands.RemoveRange(mark, _operands.Count - mark);
        return items;
    }

    /// <summary>Rotate the top <paramref name="n"/> operands by
    /// <paramref name="shift"/> places, the way <c>roll</c> does.</summary>
    public void Roll(int n, int shift)
    {
        if (n < 0) throw new PsErrorSignal("rangecheck");
        if (n == 0) return;
        if (n > _operands.Count) throw new PsErrorSignal("stackunderflow");
        var start = _operands.Count - n;
        var slice = _operands.GetRange(start, n);
        var offset = ((shift % n) + n) % n;
        for (var i = 0; i < n; i++) _operands[start + (i + offset) % n] = slice[i];
    }

    /// <summary>Duplicate the top <paramref name="n"/> operands.</summary>
    public void CopyTop(int n)
    {
        if (n > _operands.Count) throw new PsErrorSignal("stackunderflow");
        var start = _operands.Count - n;
        for (var i = 0; i < n; i++) Push(_operands[start + i]);
    }
}
