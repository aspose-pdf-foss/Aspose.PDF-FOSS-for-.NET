// Types and instance-method overloads the netstandard2.0 and net48 BCLs lack. Each block
// is compiled only where the framework itself has no such member, so on the modern
// targets this file is empty and the framework's own implementation is the one in use.
// (Language-feature types and most convenience overloads come from the build-time polyfill
// packages; this file holds what they do not carry.)
using System.Runtime.CompilerServices;

#if !NET5_0_OR_GREATER
namespace System.Collections.Generic
{
    /// <summary>Reference identity as an equality comparer (.NET 5).</summary>
    internal sealed class ReferenceEqualityComparer : IEqualityComparer<object?>, IEqualityComparer
    {
        public static ReferenceEqualityComparer Instance { get; } = new();
        private ReferenceEqualityComparer() { }
        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);
        public int GetHashCode(object? obj) => RuntimeHelpers.GetHashCode(obj!);
    }
}
#endif

#if !NETCOREAPP3_0_OR_GREATER
namespace System.Runtime.CompilerServices
{
    /// <summary>The helper the compiler emits for <c>array[range]</c> (.NET Core 3.0).
    /// The BCL's own RuntimeHelpers keeps every other member; only this one is shadowed.</summary>
    internal static class RuntimeHelpers
    {
        public static T[] GetSubArray<T>(T[] array, Range range)
        {
            var (offset, length) = range.GetOffsetAndLength(array.Length);
            var result = new T[length];
            Array.Copy(array, offset, result, 0, length);
            return result;
        }

        // The shadow hides the framework's RuntimeHelpers from this assembly, so its identity
        // hash is reached through a delegate bound once to the real method.
        private static readonly Func<object?, int> s_identityHash = (Func<object?, int>)Delegate.CreateDelegate(
            typeof(Func<object?, int>),
            typeof(object).Assembly.GetType("System.Runtime.CompilerServices.RuntimeHelpers")!
                .GetMethod("GetHashCode", new[] { typeof(object) })!);

        public static int GetHashCode(object? o) => s_identityHash(o);

        // The compiler's well-known member behind `fixed (char* p = someString)`; any unsafe
        // code compiled beside this shadow (a merged build that allows it) resolves it here.
        private static readonly Func<int> s_offsetToStringData = (Func<int>)Delegate.CreateDelegate(
            typeof(Func<int>),
            typeof(object).Assembly.GetType("System.Runtime.CompilerServices.RuntimeHelpers")!
                .GetProperty("OffsetToStringData")!.GetGetMethod()!);

        public static int OffsetToStringData => s_offsetToStringData();
    }
}
#endif

#if NETSTANDARD2_0
namespace System.Security.Cryptography.X509Certificates
{
    /// <summary>DSA key accessors (.NET Core 2.0, .NET Framework 4.7.2); the netstandard2.0
    /// reference assembly is the only one of this project's targets without them.</summary>
    internal static class DSACertificateExtensions
    {
        public static DSA? GetDSAPrivateKey(X509Certificate2 certificate) => certificate.PrivateKey as DSA;
        public static DSA? GetDSAPublicKey(X509Certificate2 certificate) => certificate.PublicKey.Key as DSA;
    }
}
#endif

#if !NET7_0_OR_GREATER
namespace Aspose.Pdf
{
    internal static class Net7PolyfillExtensions
    {
        /// <summary>Stream.ReadExactly (.NET 7).</summary>
        public static void ReadExactly(this Stream stream, byte[] buffer)
        {
            var read = 0;
            while (read < buffer.Length)
            {
                var n = stream.Read(buffer, read, buffer.Length - read);
                if (n <= 0) throw new EndOfStreamException();
                read += n;
            }
        }

        /// <summary>Enumerable.OrderDescending (.NET 7).</summary>
        public static IOrderedEnumerable<T> OrderDescending<T>(this IEnumerable<T> source) => source.OrderByDescending(x => x);
    }
}
#endif

#if !NETCOREAPP2_1_OR_GREATER && !NETSTANDARD2_1_OR_GREATER
namespace Aspose.Pdf
{
    internal static class SpanStreamPolyfillExtensions
    {
        /// <summary>Stream.Write(ReadOnlySpan) (.NET Core 2.1).</summary>
        public static void Write(this Stream stream, ReadOnlySpan<byte> buffer)
        {
            var rented = System.Buffers.ArrayPool<byte>.Shared.Rent(buffer.Length);
            buffer.CopyTo(rented);
            stream.Write(rented, 0, buffer.Length);
            System.Buffers.ArrayPool<byte>.Shared.Return(rented);
        }

        /// <summary>Stream.Read(Span) (.NET Core 2.1).</summary>
        public static int Read(this Stream stream, Span<byte> buffer)
        {
            var rented = System.Buffers.ArrayPool<byte>.Shared.Rent(buffer.Length);
            var read = stream.Read(rented, 0, buffer.Length);
            rented.AsSpan(0, read).CopyTo(buffer);
            System.Buffers.ArrayPool<byte>.Shared.Return(rented);
            return read;
        }
    }
}
#endif

#if !NETCOREAPP2_0_OR_GREATER
namespace Aspose.Pdf
{
    internal static class BclPolyfillExtensions
    {
        /// <summary>ConditionalWeakTable.AddOrUpdate (.NET Core 2.0).</summary>
        public static void AddOrUpdate<TKey, TValue>(this ConditionalWeakTable<TKey, TValue> table, TKey key, TValue value)
            where TKey : class where TValue : class?
        {
            table.Remove(key);
            table.Add(key, value);
        }

        /// <summary>string.Replace(string, string, StringComparison) (.NET Core 2.0).</summary>
        public static string Replace(this string text, string oldValue, string? newValue, StringComparison comparison)
        {
            if (oldValue.Length == 0) throw new ArgumentException("String cannot be of zero length.", nameof(oldValue));
            var index = text.IndexOf(oldValue, comparison);
            if (index < 0) return text;
            var sb = new System.Text.StringBuilder(text.Length);
            var start = 0;
            while (index >= 0)
            {
                sb.Append(text, start, index - start).Append(newValue);
                start = index + oldValue.Length;
                index = text.IndexOf(oldValue, start, comparison);
            }
            return sb.Append(text, start, text.Length - start).ToString();
        }
    }
}
#endif
