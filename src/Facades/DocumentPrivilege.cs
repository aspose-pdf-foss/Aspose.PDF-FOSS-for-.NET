using System;

namespace Aspose.Pdf.Facades;

/// <summary>
/// Represents document access permissions (P value in encryption dictionary).
/// </summary>
public sealed class DocumentPrivilege : IComparable<object>
{
    private int _flags;

    /// <summary>Create a privilege set with no permissions (most restrictive).</summary>
    public DocumentPrivilege()
    {
        _flags = 0;
    }

    internal DocumentPrivilege(int flags)
    {
        _flags = flags;
    }

    /// <summary>Create a copy of an existing privilege.</summary>
    public DocumentPrivilege(DocumentPrivilege other)
    {
        if (other is null) throw new ArgumentNullException(nameof(other));
        _flags = other._flags;
    }

    /// <summary>Allow printing (bit 3).</summary>
    public bool AllowPrint
    {
        get => (_flags & (1 << 2)) != 0;
        set => SetBit(2, value);
    }

    /// <summary>Allow modification of contents (bit 4).</summary>
    public bool AllowModifyContents
    {
        get => (_flags & (1 << 3)) != 0;
        set => SetBit(3, value);
    }

    /// <summary>Allow copying or extracting text and graphics (bit 5).</summary>
    public bool AllowCopy
    {
        get => (_flags & (1 << 4)) != 0;
        set => SetBit(4, value);
    }

    /// <summary>Allow adding or modifying annotations (bit 6).</summary>
    public bool AllowModifyAnnotations
    {
        get => (_flags & (1 << 5)) != 0;
        set => SetBit(5, value);
    }

    /// <summary>Allow filling in form fields (bit 9).</summary>
    public bool AllowFillIn
    {
        get => (_flags & (1 << 8)) != 0;
        set => SetBit(8, value);
    }

    /// <summary>Allow extracting text for accessibility (bit 10).</summary>
    public bool AllowScreenReaders
    {
        get => (_flags & (1 << 9)) != 0;
        set => SetBit(9, value);
    }

    /// <summary>Allow document assembly (bit 11).</summary>
    public bool AllowAssembly
    {
        get => (_flags & (1 << 10)) != 0;
        set => SetBit(10, value);
    }

    /// <summary>Allow degraded (low-resolution) printing. True when printing is
    /// permitted (bit 3) but high-quality printing (bit 12) is not.</summary>
    public bool AllowDegradedPrinting
    {
        get => (_flags & (1 << 2)) != 0 && (_flags & (1 << 11)) == 0;
        set => SetBit(11, !value);
    }

    /// <summary>Raw high-quality-printing permission (PDF bit 12), used when
    /// mapping to/from the encryption permission flags.</summary>
    internal bool HighQualityPrinting
    {
        get => (_flags & (1 << 11)) != 0;
        set => SetBit(11, value);
    }

    /// <summary>
    /// Composite change-allowed level derived from the modify permission bits.
    /// 0 = none; 1 = page assembly; 2 = fill-in form fields; 3 = annotations and
    /// fill-in; 4 = any change except page extraction; -1 = an unmapped combination.
    /// </summary>
    public int ChangeAllowLevel
    {
        get
        {
            if (!Bit(3) && !Bit(5) && !Bit(8) && !Bit(10)) return 0;
            if (!Bit(3) && !Bit(5) && !Bit(8) && Bit(10)) return 1;
            if (!Bit(3) && !Bit(5) && Bit(8) && !Bit(10)) return 2;
            if (!Bit(3) && Bit(5) && Bit(8) && !Bit(10)) return 3;
            if (Bit(3) && Bit(5) && Bit(8) && !Bit(10)) return 4;
            return -1;
        }
        set
        {
            switch (value)
            {
                case 1: SetBit(3, false); SetBit(5, false); SetBit(8, false); SetBit(10, true); break;
                case 2: SetBit(3, false); SetBit(5, false); SetBit(8, true); SetBit(10, false); break;
                case 3: SetBit(3, false); SetBit(5, true); SetBit(8, true); SetBit(10, false); break;
                case 4: SetBit(3, true); SetBit(5, true); SetBit(8, true); SetBit(10, false); break;
                default: SetBit(3, false); SetBit(5, false); SetBit(8, false); SetBit(10, false); break;
            }
        }
    }

    /// <summary>
    /// Composite copy-allowed level derived from the copy and accessibility bits.
    /// 0 = none; 1 = accessibility extraction only; 2 = full copy; -1 = unmapped.
    /// </summary>
    public int CopyAllowLevel
    {
        get
        {
            if (!Bit(4) && !Bit(9)) return 0;
            if (!Bit(4) && Bit(9)) return 1;
            if (Bit(4) && Bit(9)) return 2;
            return -1;
        }
        set
        {
            if (value == 1) { SetBit(4, false); SetBit(9, true); }
            else if (value == 2) { SetBit(4, true); SetBit(9, true); }
            else { SetBit(4, false); SetBit(9, false); }
        }
    }

    /// <summary>
    /// Composite print-allowed level derived from the print and print-quality bits.
    /// 0 = none; 1 = low-resolution printing; 2 = high-resolution printing; -1 = unmapped.
    /// </summary>
    public int PrintAllowLevel
    {
        get
        {
            if (!Bit(2)) return 0;
            if (Bit(2) && !Bit(11)) return 1;
            if (Bit(2) && Bit(11)) return 2;
            return -1;
        }
        set
        {
            if (value == 1) { SetBit(2, true); SetBit(11, false); }
            else if (value == 2) { SetBit(2, true); SetBit(11, true); }
            else { SetBit(2, false); SetBit(11, false); }
        }
    }

    /// <summary>All permissions granted.</summary>
    public static DocumentPrivilege AllowAll => new(-1);
    /// <summary>No permissions (most restrictive).</summary>
    public static DocumentPrivilege ForbidAll => new(0);
    /// <summary>Allows printing only.</summary>
    public static DocumentPrivilege Print => new(1 << 2);
    /// <summary>Allows modifying contents only.</summary>
    public static DocumentPrivilege ModifyContents => new(1 << 3);
    /// <summary>Allows copying only.</summary>
    public static DocumentPrivilege Copy => new(1 << 4);
    /// <summary>Allows modifying annotations only.</summary>
    public static DocumentPrivilege ModifyAnnotations => new(1 << 5);
    /// <summary>Allows filling in form fields only.</summary>
    public static DocumentPrivilege FillIn => new(1 << 8);
    /// <summary>Allows screen-reader extraction only.</summary>
    public static DocumentPrivilege ScreenReaders => new(1 << 9);
    /// <summary>Allows document assembly only.</summary>
    public static DocumentPrivilege Assembly => new(1 << 10);
    /// <summary>Allows degraded printing only.</summary>
    public static DocumentPrivilege DegradedPrinting => new(1 << 11);

    /// <summary>The raw permission flags value.</summary>
    internal int Flags => _flags;

    /// <summary>
    /// The raw permission flags as an integer (the /P value semantics). Mirrors
    /// <see cref="Document.Permissions"/>: <see cref="AllowAll"/> yields -1 (all bits set).
    /// </summary>
    public int Value => _flags;

    public int CompareTo(object? obj)
    {
        if (obj is null) return 1;
        if (obj is DocumentPrivilege dp) return _flags.CompareTo(dp._flags);
        throw new ArgumentException("Cannot compare DocumentPrivilege to " + obj.GetType().Name);
    }

    private bool Bit(int bit) => (_flags & (1 << bit)) != 0;

    private void SetBit(int bit, bool value)
    {
        if (value)
            _flags |= 1 << bit;
        else
            _flags &= ~(1 << bit);
    }
}
