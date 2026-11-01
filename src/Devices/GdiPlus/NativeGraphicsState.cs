using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Aspose.Pdf.Devices;

/// <summary>
/// Saves and restores a GDI+ surface's whole state - transform, clip and rendering settings - with
/// GDI+'s own save and restore, without the bookkeeping <see cref="Graphics.Save"/> adds around them.
/// </summary>
/// <remarks>
/// <para><see cref="Graphics.Save"/> calls the same native save, but first records the surface's
/// clip for its own use: it copies the clip region out of GDI+ to learn whether it is infinite. A
/// content stream that wraps every one of thousands of paths in <c>q</c> ... <c>Q</c> under a clip
/// of many outlines pays for a full copy of that region at every <c>q</c>: on a six-page drawing
/// with 27,578 of them, that copy was most of the time it took to print the page. The native save
/// keeps the very same state; only the copy is gone.</para>
/// <para>The surface's native handle is internal to System.Drawing. Where it cannot be reached -
/// a System.Drawing that names or types it otherwise - <see cref="TrySave"/> says so and the caller
/// keeps <see cref="Graphics.Save"/>, so the state saved is always the whole of it.</para>
/// </remarks>
[SupportedOSPlatform("windows")]
internal static class NativeGraphicsState
{
    /// <summary>GDI+'s status for a call that succeeded.</summary>
    private const int Ok = 0;

    /// <summary>Reads a surface's native handle, or null where this System.Drawing does not expose
    /// it as a plain handle.</summary>
    private static readonly Func<Graphics, IntPtr>? Handle = ResolveHandle();

    [DllImport("gdiplus.dll", ExactSpelling = true)]
    private static extern int GdipSaveGraphics(IntPtr graphics, out uint state);

    [DllImport("gdiplus.dll", ExactSpelling = true)]
    private static extern int GdipRestoreGraphics(IntPtr graphics, uint state);

    /// <summary>Saves the surface's state natively; false, with nothing saved, when the native
    /// handle cannot be reached or the save fails.</summary>
    internal static bool TrySave(Graphics graphics, out uint state)
    {
        state = 0;
        if (Handle is null) return false;
        var handle = Handle(graphics);
        return handle != IntPtr.Zero && GdipSaveGraphics(handle, out state) == Ok;
    }

    /// <summary>Restores a state <see cref="TrySave"/> saved on the same surface, discarding every
    /// state saved after it, as <see cref="Graphics.Restore"/> does.</summary>
    internal static void Restore(Graphics graphics, uint state)
    {
        if (Handle is null) return;
        var handle = Handle(graphics);
        if (handle != IntPtr.Zero) GdipRestoreGraphics(handle, state);
    }

    private static Func<Graphics, IntPtr>? ResolveHandle()
    {
#pragma warning disable S3011 // deliberate: reaches a non-public member when present, falls back when not
        var property = typeof(Graphics).GetProperty("NativeGraphics", BindingFlags.Instance | BindingFlags.NonPublic);
#pragma warning restore S3011
        var getter = property?.GetGetMethod(nonPublic: true);
        if (getter is null || getter.ReturnType != typeof(IntPtr)) return null;
        try
        {
            return (Func<Graphics, IntPtr>)getter.CreateDelegate(typeof(Func<Graphics, IntPtr>));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
