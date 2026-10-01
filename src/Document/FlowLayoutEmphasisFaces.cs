using System.Linq;
using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.Forms;
using Aspose.Pdf.IO;
using Aspose.Pdf.IO.Filters;
using Aspose.Pdf.Optimization;
using Aspose.Pdf.Security;
using Aspose.Pdf.Tagged;
using DocumentPrivilege = Aspose.Pdf.Facades.DocumentPrivilege;
namespace Aspose.Pdf;

public sealed partial class Document
{
    private sealed partial class FlowLayout
    {
        /// <summary>The bold and italic members of the block's own family, resolved the way TextBuilder resolves a weight or slant flag, with the Standard-14 stand-in where no repository member exists.</summary>
        private static void ResolveEmphasisFaces(Text.TextFragment tf, out Text.FontData? boldData, out string boldFont, out Text.FontData? italicData, out string italicFont)
        {
            // The bold member of the block's own family, resolved exactly the way
            // TextBuilder resolves a bold flag on a repository-embedded face. Absent
            // (a core face, or no Bold file installed) the regular metrics stand in,
            // which is what the writer will draw with too.
            boldData = null;
            var family = tf.TextState.FontData?.FontName ?? tf.TextState.Font?.FontName
                ?? tf.TextState.FontName;
            if (!string.IsNullOrEmpty(family) && !Text.Standard14Fonts.IsCoreName(family)
                && !family.Contains("Bold", StringComparison.OrdinalIgnoreCase))
            {
                var styled = Text.FontRepository.FindFontData(family + " Bold");
                if (styled?.TtfData is not null
                    && styled.FontName?.Contains("Bold", StringComparison.OrdinalIgnoreCase) == true)
                    boldData = styled;
            }
            // Standard-14 stand-in for the bold runs when no repository Bold member
            // resolved: the bold variant of whatever core family the block maps to.
            var boldProbe = new Text.TextState
            {
                Font = tf.TextState.Font,
                FontData = tf.TextState.FontData,
                FontName = tf.TextState.FontName,
                IsBold = true,
                IsItalic = tf.TextState.IsItalic,
            };
            boldFont = Text.TextBuilder.MapToStandard14Public(boldProbe);
            // The italic member of the family, resolved the same way — an italic run is
            // measured in the face it will actually draw in.
            italicData = null;
            if (!string.IsNullOrEmpty(family) && !Text.Standard14Fonts.IsCoreName(family)
                && !family.Contains("Italic", StringComparison.OrdinalIgnoreCase))
            {
                var styledItalic = Text.FontRepository.FindFontData(family + " Italic");
                if (styledItalic?.TtfData is not null
                    && styledItalic.FontName?.Contains("Italic", StringComparison.OrdinalIgnoreCase) == true)
                    italicData = styledItalic;
            }
            var italicProbe = new Text.TextState
            {
                Font = tf.TextState.Font,
                FontData = tf.TextState.FontData,
                FontName = tf.TextState.FontName,
                IsBold = tf.TextState.IsBold,
                IsItalic = true,
            };
            italicFont = Text.TextBuilder.MapToStandard14Public(italicProbe);
        }
    }
}
