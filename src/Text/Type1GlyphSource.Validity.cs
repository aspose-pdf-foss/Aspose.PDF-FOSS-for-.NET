namespace Aspose.Pdf.Text;

/// <summary>Type 1 program validity: the charstring command set and the scan that tells a
/// program carrying an undefined command from a well-formed one.</summary>
internal sealed partial class Type1GlyphSource
{
    // The Type 1 charstring command set (Adobe Type 1 Font Format §6.4): the single-byte
    // commands and the escaped (12 x) commands. Anything else is not a command.
    private static readonly HashSet<byte> DefinedCommands = new() { 1, 3, 4, 5, 6, 7, 8, 9, 10, 11, 13, 14, 21, 22, 30, 31 };
    private static readonly HashSet<byte> DefinedEscapedCommands = new() { 0, 1, 2, 6, 7, 12, 16, 17, 33 };

    /// <summary>Whether any charstring or subroutine of the program carries a byte that
    /// is not a Type 1 command. Such a program is no program at all to a strict reader:
    /// the reference drops the face during a PDF/A conversion and re-maps its text to a
    /// substitute, as for a font with no program (measured 2026-09-07 on a document whose
    /// HelveticaNeue-Roman and -Black spell their space glyph `hsbw 15 endchar`, the
    /// undefined command 15 - both rewritten onto Arial - while the Bold, Italic and
    /// BoldItalic siblings, clean, keep their runs; a synthetic one-face page reproduces it).</summary>
    internal bool HasUndefinedCommand()
    {
        foreach (var cs in _charStringsByGid)
            if (cs is not null && HasUndefinedCommand(cs)) return true;
        foreach (var subr in _subrs)
            if (subr is not null && HasUndefinedCommand(subr)) return true;
        return false;
    }

    private static bool HasUndefinedCommand(byte[] decrypted)
    {
        var i = 0;
        while (i < decrypted.Length)
        {
            var b = decrypted[i];
            if (b >= 32)
            {
                i += b <= 246 ? 1 : b <= 254 ? 2 : 5;
                continue;
            }
            if (b == 12)
            {
                if (i + 1 >= decrypted.Length || !DefinedEscapedCommands.Contains(decrypted[i + 1])) return true;
                i += 2;
                continue;
            }
            if (!DefinedCommands.Contains(b)) return true;
            i++;
        }
        return false;
    }
}
