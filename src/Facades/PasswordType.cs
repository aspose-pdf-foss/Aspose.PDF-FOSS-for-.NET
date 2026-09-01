#nullable disable
using System;
using System.Collections;

namespace Aspose.Pdf;

/// <summary>Identifies which password (if any) is in effect on an
/// encrypted PDF.</summary>
public enum PasswordType
{
    /// <summary>The PDF is not encrypted.</summary>
    None,
    /// <summary>The user (open) password was supplied.</summary>
    User,
    /// <summary>The owner (edit) password was supplied.</summary>
    Owner,
    /// <summary>Encrypted PDF that has not been decrypted; password
    /// type cannot yet be determined.</summary>
    Inaccessible,
}
