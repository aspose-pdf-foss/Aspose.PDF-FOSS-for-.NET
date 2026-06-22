using System.Collections;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Annotations;

/// <summary>
/// Collection of actions associated with an annotation.
/// In PDF, multiple actions are represented via the /Next chain on the /A entry.
/// </summary>
public sealed class ActionCollection : IEnumerable<PdfAction>
{
    private readonly List<PdfAction> _actions = new();
    private readonly PdfDictionary _annotDict;
    private readonly PdfReader _reader;

    internal ActionCollection(PdfDictionary annotDict, PdfReader reader)
    {
        _annotDict = annotDict;
        _reader = reader;

        var aObj = reader.Resolve(annotDict.Get("A"));
        if (aObj is PdfDictionary actionDict)
        {
            CollectActions(actionDict, reader);
        }
        else if (aObj is PdfArray actionArray)
        {
            // Some PDFs store multiple actions as an array
            foreach (var item in actionArray)
            {
                var d = reader.ResolveDict(item);
                if (d is not null)
                    _actions.Add(PdfAction.Create(d, reader));
            }
        }
    }

    /// <summary>Construct an empty collection. Used by <see cref="PdfAction.Next"/> to
    /// surface a detached /Next chain without binding to a host annotation.</summary>
    internal ActionCollection()
    {
        _annotDict = new PdfDictionary();
        _reader = PdfReader.Empty;
    }

    private void CollectActions(PdfDictionary actionDict, PdfReader reader)
    {
        _actions.Add(PdfAction.Create(actionDict, reader));

        // Follow the /Next chain
        var nextObj = reader.Resolve(actionDict.Get("Next"));
        if (nextObj is PdfDictionary nextDict)
        {
            CollectActions(nextDict, reader);
        }
        else if (nextObj is PdfArray nextArr)
        {
            foreach (var item in nextArr)
            {
                var d = reader.ResolveDict(item);
                if (d is not null)
                    CollectActions(d, reader);
            }
        }
    }

    /// <summary>Number of actions in the collection.</summary>
    public int Count => _actions.Count;

    public bool IsReadOnly => false;
    public bool IsSynchronized => false;
    public object SyncRoot { get; } = new();

    /// <summary>Get an action by index (0-based).</summary>
    public PdfAction this[int index] => _actions[index];

    /// <summary>
    /// Delete an action at the specified 1-based index (matching .NET Aspose.PDF API).
    /// </summary>
    public void Delete(int index)
    {
        // The public API uses 1-based indexing for Delete
        _actions.RemoveAt(index - 1);
        RebuildActionChain();
    }

    /// <summary>Drop every action (clears /A entirely).</summary>
    public void Delete() => Clear();

    /// <summary>
    /// Add an action to the collection.
    /// </summary>
    public void Add(PdfAction action)
    {
        _actions.Add(action);
        RebuildActionChain();
    }

    public bool Contains(PdfAction item) => _actions.Contains(item);

    public void CopyTo(PdfAction[] array, int index) => _actions.CopyTo(array, index);

    public bool Remove(PdfAction item)
    {
        if (item is null) return false;
        var removed = _actions.Remove(item);
        if (removed) RebuildActionChain();
        return removed;
    }

    public void Clear()
    {
        _actions.Clear();
        RebuildActionChain();
    }

    private void RebuildActionChain()
    {
        if (_actions.Count == 0)
        {
            _annotDict.Remove("A");
            return;
        }

        // Set the first action as /A
        _annotDict.Set("A", _actions[0].Dict);

        // Chain remaining actions via /Next
        for (int i = 0; i < _actions.Count; i++)
        {
            if (i < _actions.Count - 1)
            {
                // Build a Next chain for all subsequent actions
                if (i == _actions.Count - 2)
                {
                    // Only one remaining: set /Next directly
                    _actions[i].Dict.Set("Next", _actions[i + 1].Dict);
                }
                else
                {
                    // Multiple remaining: set /Next as array
                    var nextArr = new PdfArray();
                    for (int j = i + 1; j < _actions.Count; j++)
                        nextArr.Add(_actions[j].Dict);
                    _actions[i].Dict.Set("Next", nextArr);
                }
            }
            else
            {
                // Last action: remove any /Next
                _actions[i].Dict.Remove("Next");
            }
        }
    }

    public IEnumerator<PdfAction> GetEnumerator() => _actions.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
