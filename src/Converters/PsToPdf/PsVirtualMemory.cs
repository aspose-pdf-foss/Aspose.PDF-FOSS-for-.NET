using System;
using System.Collections.Generic;

namespace Aspose.Pdf.Converters.PsToPdf;

/// <summary>
/// The journal <c>save</c> and <c>restore</c> work through. Under an open save an
/// array or a dictionary is copied the first time it is written to, and <c>restore</c>
/// puts every copy back. A STRING's bytes are not journalled: the reference conversion
/// hands the page the text a program wrote into a string between the pair, while the
/// matrix it wrote into an array beside it comes back the way the save found it.
/// </summary>
internal sealed class PsVirtualMemory
{
    private readonly List<Level> _levels = new();

    /// <summary>One open save: what was copied under it, and how to put it back.</summary>
    private sealed class Level
    {
        public readonly List<Action> Undo = new();
        public readonly HashSet<object> Copied = new();
    }

    /// <summary>Open a save. Every composite written to from now on is copied first.</summary>
    public void Save() => _levels.Add(new Level());

    /// <summary>Put back everything changed since the save at <paramref name="level"/>,
    /// newest change first, and close that save and every one opened inside it.</summary>
    public void RestoreTo(int level)
    {
        if (level < 1 || level > _levels.Count) return;
        for (var k = _levels.Count - 1; k >= level - 1; k--)
        {
            var undo = _levels[k].Undo;
            for (var u = undo.Count - 1; u >= 0; u--) undo[u]();
        }

        _levels.RemoveRange(level - 1, _levels.Count - level + 1);
    }

    /// <summary>Note that an array is about to be written to.</summary>
    public void Changing(PsArray? value)
    {
        if (!Claim(value)) return;
        var slots = value!.ToList();
        Record(() =>
        {
            for (var k = 0; k < slots.Count; k++) value[k] = slots[k];
        });
    }

    /// <summary>Note that a dictionary is about to be written to.</summary>
    public void Changing(PsDictionary? value)
    {
        if (!Claim(value)) return;
        var entries = new List<KeyValuePair<PsValue, PsValue>>(value!.Entries);
        Record(() => value.Reset(entries));
    }

    /// <summary>Note that whatever a value holds is about to be written to.</summary>
    public void Changing(PsValue value)
    {
        Changing(value.AsArray);
        Changing(value.AsDictionary);
    }

    /// <summary>Whether this object still needs copying under the innermost save. An
    /// object already copied there is left alone: the copy already holds what the save
    /// found.</summary>
    private bool Claim(object? value) =>
        value != null && _levels.Count > 0 && _levels[_levels.Count - 1].Copied.Add(value);

    private void Record(Action undo) => _levels[_levels.Count - 1].Undo.Add(undo);
}
