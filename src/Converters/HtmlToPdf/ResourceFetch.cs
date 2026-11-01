using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The longest one resource may take, redirects and body included.</summary>
    private static readonly TimeSpan FetchRequestTimeout = TimeSpan.FromSeconds(10);

    /// <summary>The time all the fetches of one conversion may take together: a page naming hundreds of
    /// unreachable resources still converts in bounded time.</summary>
    private static readonly TimeSpan FetchConversionBudget = TimeSpan.FromSeconds(60);

    /// <summary>The largest resource accepted; a larger one is left unloaded.</summary>
    private const int FetchMaxBytes = 32 * 1024 * 1024;

    private const int FetchMaxRedirects = 5;

    private const int FetchChunkBytes = 81920;

    private const string FetchUserAgent = "Mozilla/5.0";

    /// <summary>The fetch state of the conversion running on this thread, or null outside one.</summary>
    [ThreadStatic] private static FetchSession? CurrentFetch;

    private sealed class FetchSession(ICredentials? credentials)
    {
        internal ICredentials? Credentials { get; } = credentials;

        private readonly Stopwatch _clock = Stopwatch.StartNew();

        internal TimeSpan Remaining => FetchConversionBudget - _clock.Elapsed;
    }

    /// <summary>Made on the first fetch only: a platform without a network stack never builds it.</summary>
    private static readonly Lazy<HttpClient> AnonymousFetchClient = new(() => NewFetchClient(null));

    /// <summary>What was fetched with an application's credentials stays with those credentials: a later
    /// conversion without them is never handed a resource that needed them.</summary>
    private static readonly ConditionalWeakTable<ICredentials, ConcurrentDictionary<string, byte[]?>> CredentialedFetchCache = new();

    /// <summary>Runs <paramref name="convert"/> with its own fetch budget and the options' credentials; a
    /// conversion started inside another shares the outer one's.</summary>
    private static T WithFetchSession<T>(HtmlLoadOptions? options, Func<T> convert)
    {
        var outer = CurrentFetch;
        CurrentFetch ??= new FetchSession(options?.ExternalResourcesCredentials);
        try { return convert(); }
        finally { CurrentFetch = outer; }
    }

    /// <summary>Fetch a remote resource the way a browser does: http and https only, no retries, a redirect
    /// followed only on its own scheme, a short time per request within the conversion's budget, a size cap,
    /// the load options' credentials answering an authentication challenge. Null on any failure - the caller
    /// then falls back to the alt-text/placeholder path exactly as for an unreadable local file - and on the
    /// WebAssembly edition, which opens no connection. Shared with the markdown converter.</summary>
    internal static byte[]? FetchRemoteImage(string url)
    {
        if (Compat.IsWebAssembly() || AsWebUri(url) is not { } uri) return null;
        var session = CurrentFetch;
        var credentials = session?.Credentials;
        var cache = credentials is null
            ? RemoteImageCache
            : CredentialedFetchCache.GetValue(credentials, _ => new ConcurrentDictionary<string, byte[]?>(StringComparer.Ordinal));
        if (cache.TryGetValue(url, out var known)) return known;
        var timeout = session is null || session.Remaining >= FetchRequestTimeout ? FetchRequestTimeout : session.Remaining;
        if (timeout <= TimeSpan.Zero) return null;
        var bytes = Fetch(uri, credentials, timeout);
        // A miss cut short by the budget is not the resource's answer: a later conversion asks again.
        if (bytes is not null || timeout == FetchRequestTimeout) cache.TryAdd(url, bytes);
        return bytes;
    }

    private static byte[]? Fetch(Uri uri, ICredentials? credentials, TimeSpan timeout)
    {
        var own = credentials is null ? null : NewFetchClient(credentials);
        try
        {
            var client = own ?? AnonymousFetchClient.Value;
            using var cancel = new CancellationTokenSource(timeout);
            for (var hop = 0; hop <= FetchMaxRedirects; hop++)
            {
                using var response = client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancel.Token)
                    .GetAwaiter().GetResult();
                if (!IsRedirect(response.StatusCode))
                    return response.IsSuccessStatusCode && !(response.Content.Headers.ContentLength > FetchMaxBytes)
                        ? ReadCapped(response.Content, cancel.Token)
                        : null;
                // Never from https down to http, never to a file or another scheme.
                if (response.Headers.Location is not { } location
                    || !Uri.TryCreate(uri, location, out var next) || next.Scheme != uri.Scheme)
                    return null;
                uri = next;
            }
            return null;
        }
        catch { return null; } // unreachable, refused, timed out or malformed: unloaded, like a missing file
        finally { own?.Dispose(); }
    }

    private static bool IsRedirect(HttpStatusCode status)
        => status is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect or (HttpStatusCode)308;

    /// <summary>The body, or null when it is empty or passes <see cref="FetchMaxBytes"/> (a server may send
    /// no length, or a false one).</summary>
    private static byte[]? ReadCapped(HttpContent content, CancellationToken cancel)
    {
        using var stream = Compat.ReadAsStream(content);
        using var body = new MemoryStream();
        var chunk = new byte[FetchChunkBytes];
        int read;
        while ((read = stream.ReadAsync(chunk, 0, chunk.Length, cancel).GetAwaiter().GetResult()) > 0)
        {
            if (body.Length + read > FetchMaxBytes) return null;
            body.Write(chunk, 0, read);
        }
        return body.Length > 0 ? body.ToArray() : null;
    }

    private static HttpClient NewFetchClient(ICredentials? credentials)
    {
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, Credentials = credentials })
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        // Some CDNs refuse requests without a user agent.
        client.DefaultRequestHeaders.UserAgent.ParseAdd(FetchUserAgent);
        return client;
    }
}
