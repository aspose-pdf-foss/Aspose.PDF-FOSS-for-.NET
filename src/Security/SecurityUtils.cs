// Copyright (c) 2001-2026 Aspose Pty Ltd. All Rights Reserved.

using System;
using System.Globalization;

namespace Aspose.Pdf.Security
{
    /// <summary>
    /// Small internal helpers shared by the security/crypto code and its conformance
    /// tests (e.g. parsing the hex-encoded message/digest vectors of the NIST SHA-3
    /// validation suite). Kept internal — not part of the FOSS public surface.
    /// </summary>
    internal static class SecurityUtils
    {
        /// <summary>
        /// Converts a hexadecimal string (optionally containing whitespace) to a byte
        /// array. An empty string yields an empty array. The digit count must be even.
        /// </summary>
        /// <param name="hex">The hex string to decode.</param>
        /// <returns>The decoded bytes.</returns>
        public static byte[] HexToByteArray(string hex)
        {
            if (hex == null)
            {
                throw new ArgumentNullException("hex");
            }

            // Tolerate embedded whitespace (spaces / newlines) sometimes present in vectors.
            int count = 0;
            for (int i = 0; i < hex.Length; i++)
            {
                if (!char.IsWhiteSpace(hex[i]))
                {
                    count++;
                }
            }

            if ((count & 1) != 0)
            {
                throw new FormatException("Hex string must contain an even number of digits.");
            }

            byte[] result = new byte[count / 2];
            int nibbleIndex = 0;
            int hi = 0;
            for (int i = 0; i < hex.Length; i++)
            {
                char c = hex[i];
                if (char.IsWhiteSpace(c))
                {
                    continue;
                }

                int value = HexValue(c);
                if ((nibbleIndex & 1) == 0)
                {
                    hi = value;
                }
                else
                {
                    result[nibbleIndex / 2] = (byte)((hi << 4) | value);
                }
                nibbleIndex++;
            }

            return result;
        }

        private static int HexValue(char c)
        {
            if (c >= '0' && c <= '9')
            {
                return c - '0';
            }
            if (c >= 'a' && c <= 'f')
            {
                return c - 'a' + 10;
            }
            if (c >= 'A' && c <= 'F')
            {
                return c - 'A' + 10;
            }
            throw new FormatException(string.Format(CultureInfo.InvariantCulture, "Invalid hex character '{0}'.", c));
        }
    }
}
