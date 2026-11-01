namespace Aspose.Pdf.IO.Filters;

internal static partial class JpegDecoder
{
    private sealed partial class JpegReader
    {
        /// <summary>A four-component scan is CMYK (or YCCK, which converts to CMYK first): each pixel leaves as RGB, so the component count drops to three.</summary>
        private void ConvertCmykBuffersToPixels(byte[][] buffers, int[] bufWidths)
        {
            // 4-component JPEG = CMYK or YCCK (Adobe). Convert to RGB here and
            // report 3 components so every caller takes the uniform RGB path.
            // YCCK (transform 2): the YCbCr triple decodes to the INVERTED C/M/Y
            // (chR = 255-Cink …) while K is stored DIRECTLY, so display = (1-C)(1-K)
            // becomes chR·(255-K)/255. Raw Adobe CMYK (transform 0) stores all four
            // channels inverted, so the same product uses K directly: chR·K/255.
            bool ycck = _adobeTransform == 2;
            var outComponents = KeepCmyk ? 4 : 3;
            Pixels = new byte[Width * Height * outComponents];
            byte[] buf0 = buffers[0], buf1 = buffers[1], buf2 = buffers[2], buf3 = buffers[3];
            int h0 = _components[0].H, h1 = _components[1].H, h2 = _components[2].H, h3 = _components[3].H;
            var full = h0 == _maxH && h1 == _maxH && h2 == _maxH && h3 == _maxH;
            var idx = 0;
            for (var y = 0; y < Height; y++)
            {
                // Each component's row once per row, as the YCbCr path takes it: a subsampled
                // component (not expanded beforehand) repeats a sample over H/maxH columns.
                var row0 = y * _components[0].V / _maxV * bufWidths[0];
                var row1 = y * _components[1].V / _maxV * bufWidths[1];
                var row2 = y * _components[2].V / _maxV * bufWidths[2];
                var row3 = y * _components[3].V / _maxV * bufWidths[3];
                for (var x = 0; x < Width; x++)
                {
                    int s0, s1, s2, k;
                    if (full)
                    {
                        s0 = buf0[row0 + x];
                        s1 = buf1[row1 + x];
                        s2 = buf2[row2 + x];
                        k = buf3[row3 + x];
                    }
                    else
                    {
                        s0 = buf0[row0 + x * h0 / _maxH];
                        s1 = buf1[row1 + x * h1 / _maxH];
                        s2 = buf2[row2 + x * h2 / _maxH];
                        k = buf3[row3 + x * h3 / _maxH];
                    }

                    if (InvertCmyk && !ycck)
                    {
                        // /Decode [1 0 …]: the file stores Adobe-inverted CMYK
                        // (255 = full ink); flip back to direct ink amounts before
                        // the conversion below.
                        s0 = 255 - s0; s1 = 255 - s1; s2 = 255 - s2; k = 255 - k;
                    }

                    int chR, chG, chB;
                    if (ycck)
                    {
                        // Same fixed-point YCbCr math as the 3-component path.
                        chR = Clamp(s0 + ((91881 * (s2 - 128) + 32768) >> 16));
                        chG = Clamp(s0 + ((-22554 * (s1 - 128) - 46802 * (s2 - 128) + 32768) >> 16));
                        chB = Clamp(s0 + ((116130 * (s1 - 128) + 32768) >> 16));
                    }
                    else
                    {
                        chR = s0; chG = s1; chB = s2;
                    }

                    // YCCK carries the inverted C/M/Y; raw CMYK (Adobe transform 0 or no APP14
                    // marker) carries the ink amounts themselves - the earlier "Adobe ⇒ inverted
                    // (C·K)" special case produced black headers on CMYK-JPEG logos. The inks
                    // then convert as CMYK fills and raw CMYK images do, rather than by the plain
                    // (1-C)(1-K), which drew violets bright blue and greys with a cast.
                    if (ycck) { chR = 255 - chR; chG = 255 - chG; chB = 255 - chB; }
                    if (KeepCmyk)
                    {
                        Pixels[idx++] = (byte)chR;
                        Pixels[idx++] = (byte)chG;
                        Pixels[idx++] = (byte)chB;
                        Pixels[idx++] = (byte)k;
                        continue;
                    }
                    var (r, g, b) = Devices.CmykToRgbLut.ConvertBytes((byte)chR, (byte)chG, (byte)chB, (byte)k);
                    Pixels[idx++] = r;
                    Pixels[idx++] = g;
                    Pixels[idx++] = b;
                }
            }
            Components = outComponents; // RGB unless the inks were asked for
        }

        /// <summary>A three-component scan is YCbCr: the fixed-point inverse transform writes each pixel as RGB.</summary>
        private void ConvertYcbcrBuffersToPixels(byte[][] buffers, int[] bufWidths)
        {
            // A 3-channel JPEG is usually YCbCr, but Adobe images may store
            // direct RGB. An APP14 marker with transform 0 means RGB (1 means
            // YCbCr); with no marker, infer from the component IDs — 'R','G','B'
            // (82,71,66) is direct RGB, otherwise assume YCbCr. Applying the
            // YCbCr matrix to already-RGB samples flips colours (green->magenta).
            bool ycbcr = _adobeTransform >= 0
                ? _adobeTransform != 0
                : !(_components[0].Id == 82 && _components[1].Id == 71 && _components[2].Id == 66);
            Pixels = new byte[Width * Height * 3];
            byte[] buf0 = buffers[0], buf1 = buffers[1], buf2 = buffers[2];
            int h0 = _components[0].H, h1 = _components[1].H, h2 = _components[2].H;
            var full = h0 == _maxH && h1 == _maxH && h2 == _maxH;   // the usual case once expanded
            var idx = 0;
            for (var y = 0; y < Height; y++)
            {
                // Each component's row and its horizontal step, once per row: a subsampled
                // component (not expanded beforehand) repeats a sample over H/maxH columns.
                var row0 = y * _components[0].V / _maxV * bufWidths[0];
                var row1 = y * _components[1].V / _maxV * bufWidths[1];
                var row2 = y * _components[2].V / _maxV * bufWidths[2];
                for (var x = 0; x < Width; x++)
                {
                    int yVal, cb, cr;
                    if (full)
                    {
                        yVal = buf0[row0 + x];
                        cb = buf1[row1 + x];
                        cr = buf2[row2 + x];
                    }
                    else
                    {
                        yVal = buf0[row0 + x * h0 / _maxH];
                        cb = buf1[row1 + x * h1 / _maxH];
                        cr = buf2[row2 + x * h2 / _maxH];
                    }

                    if (!ycbcr)
                    {
                        // Samples are already R, G, B.
                        Pixels[idx++] = (byte)Clamp(yVal);
                        Pixels[idx++] = (byte)Clamp(cb);
                        Pixels[idx++] = (byte)Clamp(cr);
                        continue;
                    }

                    // YCbCr to RGB — the IJG fixed-point math
                    // (SCALEBITS=16, constants 1.40200/1.77200/0.34414/0.71414,
                    // one rounding constant folded into the G sum). Float math
                    // rounds a hair differently and lands ±1 off.
                    var r = yVal + ((91881 * (cr - 128) + 32768) >> 16);
                    var g = yVal + ((-22554 * (cb - 128) - 46802 * (cr - 128) + 32768) >> 16);
                    var b = yVal + ((116130 * (cb - 128) + 32768) >> 16);

                    Pixels[idx++] = (byte)Clamp(r);
                    Pixels[idx++] = (byte)Clamp(g);
                    Pixels[idx++] = (byte)Clamp(b);
                }
            }
        }
    }
}
