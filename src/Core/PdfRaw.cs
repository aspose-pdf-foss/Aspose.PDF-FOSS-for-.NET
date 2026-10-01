using System.Text;

namespace Aspose.Pdf.Core;

/// <summary>
/// Bytes that go into the file exactly as they stand.
///
/// Every other kind here is a VALUE that knows how to write itself: a number
/// writes digits, a name writes a slash and its characters, a string decides
/// between brackets and hex. This is the opposite — bytes that mean whatever
/// they say, written through untouched and never interpreted.
///
/// Two things need that, and neither has another shape in this model:
///
/// - A content stream's operators. `q`, `Tj`, `re` are not names, numbers or
///   strings, and writing one as any of those writes something else.
/// - Room reserved in advance. A writer that must lay down a length or an
///   offset it does not know yet writes a run of spaces now and overwrites it
///   once the number is known; until then the placeholder has to survive being
///   written, and no value-shaped object would survive it unchanged.
///
/// ⚠⚠ Nothing checks what is in here. A caller that puts malformed bytes in a
/// document gets a malformed document — that is the point of the kind, and the
/// reason to reach for any other kind first when one fits.
/// </summary>
internal sealed class PdfRaw : PdfObject
{
    /// <summary>The bytes, as they will appear.</summary>
    public byte[] Bytes { get; }

    public PdfRaw(byte[] bytes) => Bytes = bytes ?? Array.Empty<byte>();

    /// <summary>
    /// A run of spaces, to be written over later.
    ///
    /// Spaces rather than zeros so a file caught half-written is still readable
    /// — a reader meeting the placeholder sees a gap where a number should be,
    /// not a string of NULs in the middle of an object.
    /// </summary>
    public static PdfRaw Reserved(int count)
    {
        var room = new byte[System.Math.Max(0, count)];
        for (var i = 0; i < room.Length; i++) room[i] = (byte)' ';
        return new PdfRaw(room);
    }

    /// <summary>
    /// The bytes as text, which is what they are in every case this exists for.
    ///
    /// ⚠ Latin-1, so every byte maps to the character of the same number and
    /// back again. Anything else would rewrite a byte that is not a character
    /// in whatever encoding was guessed.
    /// </summary>
    public override string ToString() => Compat.Latin1.GetString(Bytes);
}
