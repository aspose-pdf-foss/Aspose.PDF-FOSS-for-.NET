using System;
using System.Collections.Generic;

namespace Aspose.Pdf.Converters.PsToPdf;

/// <summary>
/// The PostScript execution engine: operand, dictionary and execution stacks, the
/// name-resolution rule, and the loop that runs one object at a time. Operators are
/// registered from the tables in the <c>PsOperators*</c> files; graphics operators
/// reach the page through <see cref="Graphics"/>.
/// </summary>
internal sealed partial class PsInterpreter
{
    /// <summary>PostScript's own limit on the operand stack, and the point at which a
    /// runaway program is stopped rather than allowed to exhaust memory.</summary>
    private const int OperandLimit = 100000;

    /// <summary>How many objects a single conversion may execute before the
    /// interpreter gives up. A source that loops forever must still yield the page it
    /// painted, so this is a budget rather than an error.</summary>
    private const int StepBudget = 40000000;

    private readonly List<PsValue> _operands = new();
    private readonly List<PsDictionary> _dictStack = new();
    private readonly List<PsFrame> _execStack = new();
    private int _steps;

    /// <summary>Create an interpreter over a fresh set of dictionaries.</summary>
    public PsInterpreter(PsGraphics graphics)
    {
        Graphics = graphics;
        SystemDict = new PsDictionary();
        GlobalDict = new PsDictionary();
        UserDict = new PsDictionary();
        ErrorDict = new PsDictionary();
        FontDirectory = new PsDictionary();
        _dictStack.Add(SystemDict);
        _dictStack.Add(GlobalDict);
        _dictStack.Add(UserDict);
        RegisterOperators();
        RegisterConstants();
    }

    /// <summary>The page the graphics operators paint on.</summary>
    public PsGraphics Graphics { get; }

    /// <summary>The dictionary holding every built-in operator.</summary>
    public PsDictionary SystemDict { get; }

    /// <summary>The global dictionary, between system and user on the stack.</summary>
    public PsDictionary GlobalDict { get; }

    /// <summary>The dictionary a program's own definitions land in.</summary>
    public PsDictionary UserDict { get; }

    /// <summary>The error handlers, which this interpreter records rather than runs.</summary>
    public PsDictionary ErrorDict { get; }

    /// <summary>Fonts <c>definefont</c> has registered.</summary>
    public PsDictionary FontDirectory { get; }

    /// <summary>The name of the first error raised, or null when the program ran clean.</summary>
    public string? FirstError { get; private set; }

    /// <summary>How many errors the program raised.</summary>
    public int ErrorCount { get; private set; }

    /// <summary>Whether the step budget ran out.</summary>
    public bool BudgetExhausted { get; private set; }

    /// <summary>Whether procedures are stored packed, which a program reads back and
    /// restores around its own prologue.</summary>
    public bool Packing { get; set; }

    /// <summary>The source currently being executed, which <c>currentfile</c> hands
    /// out so a program can read inline data.</summary>
    public PsFile? CurrentFile { get; set; }

    /// <summary>Where the fonts named by the source are looked for.</summary>
    public PsFontLibrary? Fonts { get; set; }

    /// <summary>Run a whole PostScript source to completion.</summary>
    public void Run(byte[] source)
    {
        var scanner = new PsScanner(source);
        CurrentFile = new PsFile(source);
        Execute(scanner);
    }

    /// <summary>Run every object a scanner yields, keeping the file position in step
    /// so that a program reading <c>currentfile</c> consumes the same bytes.</summary>
    public void Execute(PsScanner scanner)
    {
        scanner.ImmediateResolver ??= ResolveImmediate;
        while (true)
        {
            scanner.Position = Math.Max(scanner.Position, CurrentFile?.Position ?? 0);
            if (!scanner.TryRead(out var value)) return;
            if (CurrentFile != null) CurrentFile.Position = scanner.Position;
            if (!Step(value)) return;
        }
    }

    /// <summary>Execute one object, reporting false when the program asked to stop.</summary>
    private bool Step(PsValue value)
    {
        try
        {
            ExecuteObject(value);
        }
        catch (PsQuitSignal)
        {
            return false;
        }
        catch (PsStopSignal)
        {
            // A `stop` with no `stopped` to catch it terminates the program.
            return false;
        }
        catch (PsErrorSignal e)
        {
            // An error nothing caught ends the program, as the language's own default
            // handler does: a source with a mistake in it yields the marks it made
            // BEFORE the mistake and nothing after. One corpus sample makes the
            // mistake deliberately and expects a blank page for it.
            RecordError(e.ErrorName);
            return false;
        }

        return !BudgetExhausted;
    }

    /// <summary>Execute one object: push it when it is literal, and otherwise run it.</summary>
    public void ExecuteObject(PsValue value)
    {
        if (++_steps > StepBudget)
        {
            BudgetExhausted = true;
            throw new PsQuitSignal();
        }

        if (!value.IsExecutable)
        {
            Push(value);
            return;
        }

        switch (value.Type)
        {
            case PsType.Name:
                ExecuteName(value);
                return;
            case PsType.Operator:
                value.AsOperator!.Action(this);
                return;
            case PsType.Array:
                // A procedure literal met in the input is PUSHED, not run. It runs
                // only when a name resolves to it, or when `exec` is applied to it.
                Push(value);
                return;
            case PsType.String:
                Execute(new PsScanner(value.AsString!.ToArray()));
                return;
            default:
                Push(value);
                return;
        }
    }

    /// <summary>Run an object the way <c>exec</c> does: a procedure body is executed,
    /// and anything else is executed as though the scanner had just produced it.</summary>
    public void Invoke(PsValue value)
    {
        if (value.Type == PsType.Array)
        {
            ExecuteProcedure(value.AsArray);
            return;
        }

        ExecuteObject(value.WithExecutable(true));
    }

    /// <summary>Look a name up and execute what it is bound to.</summary>
    private void ExecuteName(PsValue name)
    {
        if (!TryResolve(name, out var bound)) throw new PsErrorSignal("undefined");
        if (bound.Type == PsType.Array && bound.IsExecutable) ExecuteProcedure(bound.AsArray);
        else ExecuteObject(bound);
    }

    /// <summary>Run every element of a procedure body.</summary>
    public void ExecuteProcedure(PsArray? body)
    {
        if (body is null) return;
        if (_execStack.Count > 0 && _execStack.Count > OperandLimit) throw new PsErrorSignal("execstackoverflow");
        _execStack.Add(new PsFrame(body));
        try
        {
            for (var i = 0; i < body.Length; i++) ExecuteObject(body[i]);
        }
        finally
        {
            _execStack.RemoveAt(_execStack.Count - 1);
        }
    }

    /// <summary>Resolve a name for the <c>//name</c> form. An undefined name is left
    /// as a literal rather than faulting, because the scanner may reach a definition
    /// that the program has not executed yet.</summary>
    private PsValue? ResolveImmediate(string name) =>
        TryResolve(PsValue.LiteralName(name), out var value) ? value : null;

    /// <summary>Search the dictionary stack from the top for a name.</summary>
    public bool TryResolve(PsValue name, out PsValue value)
    {
        for (var i = _dictStack.Count - 1; i >= 0; i--)
            if (_dictStack[i].TryGet(name, out value))
                return true;
        value = PsValue.Null;
        return false;
    }

    /// <summary>Search the dictionary stack for the dictionary holding a name.</summary>
    public PsDictionary? FindDictionary(PsValue name)
    {
        for (var i = _dictStack.Count - 1; i >= 0; i--)
            if (_dictStack[i].TryGet(name, out _))
                return _dictStack[i];
        return null;
    }

    /// <summary>Remember an error without stopping the conversion: a source that
    /// faults still contributes the marks it made before the fault.</summary>
    public void RecordError(string name)
    {
        ErrorCount++;
        FirstError ??= name;
    }

    private sealed class PsFrame
    {
        public PsFrame(PsArray body)
        {
            Body = body;
        }

        public PsArray Body { get; }
    }
}

/// <summary>Raised by an operator that cannot proceed; carries the PostScript error
/// name so that <c>stopped</c> and the error dictionary see the right one.</summary>
internal sealed class PsErrorSignal : Exception
{
    /// <summary>Raise a PostScript error.</summary>
    public PsErrorSignal(string errorName) : base(errorName)
    {
        ErrorName = errorName;
    }

    /// <summary>The PostScript error name, such as <c>typecheck</c>.</summary>
    public string ErrorName { get; }
}

/// <summary>Raised by <c>stop</c>, and caught by the nearest <c>stopped</c>.</summary>
internal sealed class PsStopSignal : Exception
{
}

/// <summary>Raised by <c>quit</c>, and by the step budget running out.</summary>
internal sealed class PsQuitSignal : Exception
{
}

/// <summary>Raised by <c>exit</c>, and caught by the innermost enclosing loop.</summary>
internal sealed class PsExitSignal : Exception
{
}
