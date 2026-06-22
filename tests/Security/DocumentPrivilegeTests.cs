using Aspose.Pdf.Facades;
using Xunit;

namespace Aspose.Pdf.Tests.Security;

public sealed class DocumentPrivilegeTests
{
    [Fact]
    public void AllowAll_HasAllPermissions()
    {
        var priv = DocumentPrivilege.AllowAll;
        Assert.True(priv.AllowPrint);
        Assert.True(priv.AllowModifyContents);
        Assert.True(priv.AllowCopy);
        Assert.True(priv.AllowModifyAnnotations);
        Assert.True(priv.AllowFillIn);
        Assert.True(priv.AllowScreenReaders);
        Assert.True(priv.AllowAssembly);
        // AllowDegradedPrinting reports the "printing restricted to degraded
        // quality" condition. AllowAll permits full-quality printing, so that
        // restriction is not in effect and the flag is false.
        Assert.False(priv.AllowDegradedPrinting);
    }

    [Fact]
    public void ForbidAll_HasNoPermissions()
    {
        var priv = DocumentPrivilege.ForbidAll;
        Assert.False(priv.AllowPrint);
        Assert.False(priv.AllowModifyContents);
        Assert.False(priv.AllowCopy);
        Assert.False(priv.AllowModifyAnnotations);
        Assert.False(priv.AllowFillIn);
        Assert.False(priv.AllowScreenReaders);
        Assert.False(priv.AllowAssembly);
        Assert.False(priv.AllowDegradedPrinting);
    }

    [Fact]
    public void CustomPermissions_SetAndGet()
    {
        var priv = new DocumentPrivilege
        {
            AllowPrint = true,
            AllowCopy = true,
        };

        Assert.True(priv.AllowPrint);
        Assert.True(priv.AllowCopy);
        Assert.False(priv.AllowModifyContents);
        Assert.False(priv.AllowModifyAnnotations);
        Assert.False(priv.AllowFillIn);
    }

    [Fact]
    public void SetPermission_CanBeToggled()
    {
        var priv = new DocumentPrivilege();
        Assert.False(priv.AllowPrint);

        priv.AllowPrint = true;
        Assert.True(priv.AllowPrint);

        priv.AllowPrint = false;
        Assert.False(priv.AllowPrint);
    }

    [Fact]
    public void AllPermissions_Individually()
    {
        var priv = new DocumentPrivilege
        {
            AllowPrint = true,
            AllowModifyContents = true,
            AllowCopy = true,
            AllowModifyAnnotations = true,
            AllowFillIn = true,
            AllowScreenReaders = true,
            AllowAssembly = true,
            AllowDegradedPrinting = true,
        };

        Assert.True(priv.AllowPrint);
        Assert.True(priv.AllowModifyContents);
        Assert.True(priv.AllowCopy);
        Assert.True(priv.AllowModifyAnnotations);
        Assert.True(priv.AllowFillIn);
        Assert.True(priv.AllowScreenReaders);
        Assert.True(priv.AllowAssembly);
        Assert.True(priv.AllowDegradedPrinting);
    }

    [Fact]
    public void Encrypt_WithCustomPrivileges_RoundTrip()
    {
        using var doc = Document.Create();
        doc.Pages.Add();

        var priv = new DocumentPrivilege
        {
            AllowPrint = true,
            AllowCopy = true,
        };

        doc.Encrypt("user", "owner", priv);
        var encrypted = doc.ToArray();

        using var opened = Document.Open(encrypted, "user");
        var readPriv = new DocumentPrivilege(opened.Permissions);
        Assert.True(readPriv.AllowPrint);
        Assert.True(readPriv.AllowCopy);
        Assert.False(readPriv.AllowModifyContents);
    }
}
