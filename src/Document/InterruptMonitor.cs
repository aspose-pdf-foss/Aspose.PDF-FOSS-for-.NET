#nullable disable
using System;
using System.Collections;

namespace Aspose.Pdf;

/// <summary>A handle for asking a long-running operation to stop. Nothing in this library checks it, so interrupting has no effect.</summary>
public class InterruptMonitor
{
    /// <summary>Creates an interrupt monitor.</summary>
    public InterruptMonitor() { }

    /// <summary>Requests that the monitored operation stop. Has no effect in this library.</summary>
    public void Interrupt() { }
}
