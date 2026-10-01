using System.Text;

namespace Aspose.Pdf.Text;

internal sealed partial class GlyphOutlineParser : IGlyphOutlineSource
{
    /// <summary>The stages of the simple glyph parse: the flags, the x and y coordinates and the contours; each returns false when the table runs out.</summary>
    private bool BuildGlyphContours(SimpleGlyphState sg, int c)
    {
        if (sg.endPts[c] < sg.ptIdx - 1 || sg.endPts[c] >= sg.numPoints) return false;
        var count = sg.endPts[c] - sg.ptIdx + 1;
        var pts = new ContourPoint[count];
        for (var i = 0; i < count; i++)
        {
            var idx = sg.ptIdx + i;
            var onCurve = (sg.flags[idx] & 0x01) != 0;
            pts[i] = new ContourPoint(sg.xCoords[idx], sg.yCoords[idx], onCurve);
        }
        sg.contours[c] = pts;
        sg.ptIdx = sg.endPts[c] + 1;
        return true;
    }

    /// <summary>The stages of the simple glyph parse: the flags, the x and y coordinates and the contours; each returns false when the table runs out.</summary>
    private bool ReadGlyphYCoords(SimpleGlyphState sg, int i)
    {
        var f = sg.flags[i];
        if ((f & 0x04) != 0) // y is 1 byte
        {
            if (sg.offset >= _data.Length) return false;
            var dy = _data[sg.offset++];
            sg.y += (f & 0x20) != 0 ? dy : -dy;
        }
        else if ((f & 0x20) == 0) // y is 2 bytes (signed)
        {
            if (sg.offset + 2 > _data.Length) return false;
            sg.y += ReadInt16(sg.offset);
            sg.offset += 2;
        }
        sg.yCoords[i] = sg.y;
        return true;
    }

    /// <summary>The stages of the simple glyph parse: the flags, the x and y coordinates and the contours; each returns false when the table runs out.</summary>
    private bool ReadGlyphXCoords(SimpleGlyphState sg, int i)
    {
        var f = sg.flags[i];
        if ((f & 0x02) != 0) // x is 1 byte
        {
            if (sg.offset >= _data.Length) return false;
            var dx = _data[sg.offset++];
            sg.x += (f & 0x10) != 0 ? dx : -dx;
        }
        else if ((f & 0x10) == 0) // x is 2 bytes (signed)
        {
            if (sg.offset + 2 > _data.Length) return false;
            sg.x += ReadInt16(sg.offset);
            sg.offset += 2;
        }
        // else: x is same as previous (delta = 0)
        sg.xCoords[i] = sg.x;
        return true;
    }

    /// <summary>The stages of the simple glyph parse: the flags, the x and y coordinates and the contours; each returns false when the table runs out.</summary>
    /// <returns>The index of the LAST point this flag byte filled - a repeat flag fills several - or null when the table runs out.</returns>
    private int? ReadGlyphFlags(SimpleGlyphState sg, int i)
    {
        if (sg.offset >= _data.Length) return null;
        sg.flags[i] = _data[sg.offset++];
        if ((sg.flags[i] & 0x08) != 0) // repeat flag
        {
            if (sg.offset >= _data.Length) return null;
            var repeatCount = _data[sg.offset++];
            for (var j = 0; j < repeatCount && i + 1 < sg.numPoints; j++)
            {
                i++;
                sg.flags[i] = sg.flags[i - 1];
            }
        }
        return i;
    }
}
