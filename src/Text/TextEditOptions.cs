namespace Aspose.Pdf.Text;

/// <summary>Base class for text-processing options.</summary>
public abstract class TextOptions
{
}

/// <summary>Options that control how text is edited (replaced, font-substituted, language-transformed).</summary>
public sealed class TextEditOptions : TextOptions
{
    /// <summary>Action to perform if font does not contain required character.</summary>
    public enum NoCharacterAction
    {
        ThrowException,
        UseStandardFont,
        ReplaceAnyway,
        ReplaceFonts,
        UseCustomReplacementFont,
    }

    /// <summary>Font-replacement strategy when the original font cannot encode replacement text.</summary>
    public enum FontReplace
    {
        Default,
        RemoveUnusedFonts,
    }

    /// <summary>Language transformation behavior (RTL, ligatures, etc).</summary>
    public enum LanguageTransformation
    {
        Default,
        ExactlyAsISee,
        None,
    }

    /// <summary>How clipping paths are processed when text edits affect clipped regions.</summary>
    public enum ClippingPathsProcessingMode
    {
        KeepIntact,
        Expand,
        Remove,
    }

    public TextEditOptions(bool allowLanguageTransformation)
    {
        AllowLanguageTransformation = allowLanguageTransformation;
    }

    public TextEditOptions(FontReplace fontReplaceBehavior)
    {
        FontReplaceBehavior = fontReplaceBehavior;
    }

    public TextEditOptions(LanguageTransformation languageTransformationBehavior)
    {
        LanguageTransformationBehavior = languageTransformationBehavior;
    }

    public TextEditOptions(NoCharacterAction noCharacterBehavior)
    {
        NoCharacterBehavior = noCharacterBehavior;
    }

    public bool AllowLanguageTransformation { get; set; }
    public ClippingPathsProcessingMode ClippingPathsProcessing { get; set; }
    public FontReplace FontReplaceBehavior { get; set; }
    public LanguageTransformation LanguageTransformationBehavior { get; set; }

    private NoCharacterAction _noCharacterBehavior;

    public NoCharacterAction NoCharacterBehavior
    {
        get => _noCharacterBehavior;
        set { _noCharacterBehavior = value; NoCharacterBehaviorExplicit = true; }
    }

    /// <summary>True when <see cref="NoCharacterBehavior"/> was explicitly set by
    /// the caller (vs. the enum's zero-default). Keeps
    /// <see cref="NoCharacterAction.ThrowException"/> (the zero value) opt-in so it
    /// only fires when the caller deliberately selected it.</summary>
    internal bool NoCharacterBehaviorExplicit { get; private set; }

    public Font? ReplacementFont { get; set; }
    public bool ToAttemptGetUnderlineFromSource { get; set; }
}
