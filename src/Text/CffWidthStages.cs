

namespace Aspose.Pdf.Text;

internal sealed partial class CffParser
{
// The stages of the CFF width extraction: the private dict read and one glyph width.

    /// <summary>Reads the nominal and default widths from the private dictionary when it is present.</summary>
    private void ReadCffPrivateDict(CffWidthState cw)
    {
        if (cw.privateDictSize > 0 && cw.privateDictOffset > 0 &&
            cw.privateDictOffset + cw.privateDictSize <= _data.Length)
        {
            var privateDictData = new byte[cw.privateDictSize];
            Array.Copy(_data, cw.privateDictOffset, privateDictData, 0, cw.privateDictSize);
            var privateDict = ParseDict(privateDictData);

            _defaultWidthX = GetDictInt(privateDict, 20, 0);
            _nominalWidthX = GetDictInt(privateDict, 21, 0);
        }
    }

    /// <summary>x</summary>
    private void ReadCffGlyphWidth(CffWidthState cw, int i)
    {
        var csData = ReadIndexEntry(cw.charStringsIndex, i);
        if (csData.Length == 0)
        {
            cw.widths[i] = _defaultWidthX;
            return;
        }

        var width = ExtractCharstringWidth(csData);
        cw.widths[i] = width;
    }
}
