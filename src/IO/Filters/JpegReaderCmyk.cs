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
            Pixels = new byte[Width * Height * 3];
            for (var y = 0; y < Height; y++)
            {
                for (var x = 0; x < Width; x++)
                {
                    var sy0 = y * _components[0].V / _maxV;
                    var sx0 = x * _components[0].H / _maxH;
                    int s0 = buffers[0][sy0 * bufWidths[0] + sx0];

                    var sy1 = y * _components[1].V / _maxV;
                    var sx1 = x * _components[1].H / _maxH;
                    int s1 = buffers[1][sy1 * bufWidths[1] + sx1];

                    var sy2 = y * _components[2].V / _maxV;
                    var sx2 = x * _components[2].H / _maxH;
                    int s2 = buffers[2][sy2 * bufWidths[2] + sx2];

                    var sy3 = y * _components[3].V / _maxV;
                    var sx3 = x * _components[3].H / _maxH;
                    int k = buffers[3][sy3 * bufWidths[3] + sx3];

                    if (InvertCmyk && !ycck)
                    {
                        // /Decode [1 0 …]: the file stores Adobe-inverted CMYK
                        // (255 = full ink); flip back to direct ink amounts before
                        // the (1-C)(1-K) conversion below.
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

                    int r, g, b;
                    if (ycck)
                    {
                        r = chR * (255 - k) / 255;
                        g = chG * (255 - k) / 255;
                        b = chB * (255 - k) / 255;
                    }
                    else
                    {
                        // Raw CMYK (Adobe transform 0 or no APP14 marker): the scan samples
                        // are ink amounts, so RGB = (1-C)(1-K). The earlier "Adobe ⇒ inverted
                        // (C·K)" special case produced black headers on CMYK-JPEG logos
                        // — the decoder already yields ink values, not the
                        // inverted-stored values that formula assumed.
                        r = (255 - chR) * (255 - k) / 255;
                        g = (255 - chG) * (255 - k) / 255;
                        b = (255 - chB) * (255 - k) / 255;
                    }

                    var idx = (y * Width + x) * 3;
                    Pixels[idx] = (byte)r;
                    Pixels[idx + 1] = (byte)g;
                    Pixels[idx + 2] = (byte)b;
                }
            }
            Components = 3; // output buffer is RGB
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
            for (var y = 0; y < Height; y++)
            {
                for (var x = 0; x < Width; x++)
                {
                    // Upsample chroma components
                    var sy0 = y * _components[0].V / _maxV;
                    var sx0 = x * _components[0].H / _maxH;
                    int yVal = buffers[0][sy0 * bufWidths[0] + sx0];

                    var sy1 = y * _components[1].V / _maxV;
                    var sx1 = x * _components[1].H / _maxH;
                    int cb = buffers[1][sy1 * bufWidths[1] + sx1];

                    var sy2 = y * _components[2].V / _maxV;
                    var sx2 = x * _components[2].H / _maxH;
                    int cr = buffers[2][sy2 * bufWidths[2] + sx2];

                    var idx = (y * Width + x) * 3;
                    if (!ycbcr)
                    {
                        // Samples are already R, G, B.
                        Pixels[idx] = (byte)Clamp(yVal);
                        Pixels[idx + 1] = (byte)Clamp(cb);
                        Pixels[idx + 2] = (byte)Clamp(cr);
                        continue;
                    }

                    // YCbCr to RGB — the IJG fixed-point math
                    // (SCALEBITS=16, constants 1.40200/1.77200/0.34414/0.71414,
                    // one rounding constant folded into the G sum). Float math
                    // rounds a hair differently and lands ±1 off.
                    var r = yVal + ((91881 * (cr - 128) + 32768) >> 16);
                    var g = yVal + ((-22554 * (cb - 128) - 46802 * (cr - 128) + 32768) >> 16);
                    var b = yVal + ((116130 * (cb - 128) + 32768) >> 16);

                    Pixels[idx] = (byte)Clamp(r);
                    Pixels[idx + 1] = (byte)Clamp(g);
                    Pixels[idx + 2] = (byte)Clamp(b);
                }
            }
        }
    }
}
