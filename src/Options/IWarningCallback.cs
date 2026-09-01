#nullable disable
using System;
using System.Collections;

namespace Aspose.Pdf;

public interface IWarningCallback
{
    ReturnAction Warning(WarningInfo warning);
}
