namespace Aspose.Pdf.Facades;

/// <summary>
/// Configures how <see cref="PdfContentEditor.ReplaceText(string, string)"/>
/// matches and substitutes text. Mirrors the Aspose.Pdf API:
/// <c>editor.ReplaceTextStrategy.IsRegularExpressionUsed</c> /
/// <c>editor.ReplaceTextStrategy.ReplaceScope</c>.
///
/// When obtained from a <see cref="PdfContentEditor"/> (<see cref="Owner"/> set),
/// the three settings are live views over the editor's
/// <see cref="PdfContentEditor.TextSearchOptions"/> /
/// <see cref="PdfContentEditor.TextEditOptions"/> /
/// <see cref="PdfContentEditor.TextReplaceOptions"/>, so the legacy strategy API
/// and the option objects stay in sync. A standalone instance keeps its own state.
/// </summary>
public sealed class ReplaceTextStrategy
{
    /// <summary>The editor this strategy is bound to; null for a standalone instance.</summary>
    internal PdfContentEditor? Owner;

    /// <summary>Bind this strategy to <paramref name="editor"/>, pushing any
    /// configuration set while standalone onto the editor's option objects so it
    /// survives once the strategy becomes a live view over them. Without this, a
    /// pre-configured strategy assigned to <see cref="PdfContentEditor.ReplaceTextStrategy"/>
    /// would silently fall back to the editor's defaults.</summary>
    internal void BindTo(PdfContentEditor editor)
    {
        editor.TextReplaceOptions.ReplaceScope = _scope == Scope.ReplaceAll
            ? Aspose.Pdf.Text.TextReplaceOptions.Scope.REPLACE_ALL
            : Aspose.Pdf.Text.TextReplaceOptions.Scope.REPLACE_FIRST;
        editor.TextSearchOptions.IsRegularExpressionUsed = _isRegex;
        editor.TextEditOptions.NoCharacterBehavior = ToEditOption(_noCharacter);
        Owner = editor;
    }

    // Standalone backing (used only when Owner is null).
    private Scope _scope = Scope.ReplaceFirst;
    private bool _isRegex;
    private NoCharacterAction _noCharacter = NoCharacterAction.UseStandardFont;

    /// <summary>How many matches to replace per call.</summary>
    public enum Scope
    {
        /// <summary>Replace only the first match. (default)</summary>
        ReplaceFirst,
        /// <summary>Replace every match.</summary>
        ReplaceAll,
    }

    /// <summary>How many matches to replace; default <see cref="Scope.ReplaceFirst"/>.</summary>
    public Scope ReplaceScope
    {
        get => Owner is null
            ? _scope
            : (Owner.TextReplaceOptions.ReplaceScope == Aspose.Pdf.Text.TextReplaceOptions.Scope.REPLACE_ALL
                ? Scope.ReplaceAll : Scope.ReplaceFirst);
        set
        {
            if (Owner is null) _scope = value;
            else Owner.TextReplaceOptions.ReplaceScope = value == Scope.ReplaceAll
                ? Aspose.Pdf.Text.TextReplaceOptions.Scope.REPLACE_ALL
                : Aspose.Pdf.Text.TextReplaceOptions.Scope.REPLACE_FIRST;
        }
    }

    /// <summary>When true, the search string is interpreted as a .NET regular expression.</summary>
    public bool IsRegularExpressionUsed
    {
        get => Owner is null ? _isRegex : Owner.TextSearchOptions.IsRegularExpressionUsed;
        set { if (Owner is null) _isRegex = value; else Owner.TextSearchOptions.IsRegularExpressionUsed = value; }
    }

    /// <summary>What to do when the replacement font lacks a glyph for a character.</summary>
    public enum NoCharacterAction
    {
        UseStandardFont = 0,
        ReplaceFromOtherFonts = 1,
        ReplaceAnyway = 2,
        ThrowException = 3,
    }

    /// <summary>Behaviour when the replacement font cannot render a character.</summary>
    public NoCharacterAction NoCharacterBehavior
    {
        get => Owner is null ? _noCharacter : FromEditOption(Owner.TextEditOptions.NoCharacterBehavior);
        set { if (Owner is null) _noCharacter = value; else Owner.TextEditOptions.NoCharacterBehavior = ToEditOption(value); }
    }

    // The legacy and DOM NoCharacterAction enums share value names but not numeric
    // values, so map by name.
    private static NoCharacterAction FromEditOption(Aspose.Pdf.Text.TextEditOptions.NoCharacterAction a) => a switch
    {
        Aspose.Pdf.Text.TextEditOptions.NoCharacterAction.ThrowException => NoCharacterAction.ThrowException,
        Aspose.Pdf.Text.TextEditOptions.NoCharacterAction.ReplaceAnyway => NoCharacterAction.ReplaceAnyway,
        _ => NoCharacterAction.UseStandardFont,
    };

    private static Aspose.Pdf.Text.TextEditOptions.NoCharacterAction ToEditOption(NoCharacterAction a) => a switch
    {
        NoCharacterAction.ThrowException => Aspose.Pdf.Text.TextEditOptions.NoCharacterAction.ThrowException,
        NoCharacterAction.ReplaceAnyway => Aspose.Pdf.Text.TextEditOptions.NoCharacterAction.ReplaceAnyway,
        _ => Aspose.Pdf.Text.TextEditOptions.NoCharacterAction.UseStandardFont,
    };
}
