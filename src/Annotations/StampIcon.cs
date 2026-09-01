using Aspose.Pdf.Core;
using Aspose.Pdf.Functions;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Annotations;

/// <summary>Named stamp-icon style for <see cref="StampAnnotation"/> (/Name entry, PDF 32000 §12.5.6.14).</summary>
public enum StampIcon
{
    Draft = 0,
    Approved,
    Experimental,
    NotApproved,
    AsIs,
    Expired,
    NotForPublicRelease,
    Confidential,
    Final,
    Sold,
    Departmental,
    ForComment,
    ForPublicRelease,
    TopSecret,
}
