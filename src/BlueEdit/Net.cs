using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Authentication;

/// <summary>
/// Every HttpClient of the editor, with two failures of a bare Linux said in words (docs/linux.md). Without OpenSSL's
/// libssl (Debian 12's container image) .NET ends the whole process at the first https request: it prints "No usable
/// version of libssl was found" and aborts, unsaved text lost (measured 23 October 2026); so an https request is
/// refused first, saying what to install. Without trusted root certificates (Ubuntu 24.04's image has no
/// ca-certificates) the connection fails as "The SSL connection could not be established, see inner exception"; the
/// message gets the reason and what to install, and stays a SecureConnectionError, which a download does not retry.
/// </summary>
static class Net
{
    public static HttpClient Client(TimeSpan timeout) => new(new Guard(new HttpClientHandler())) { Timeout = timeout };

    // the names .NET tries for libssl, in its order
    static readonly string[] LibSsl = ["libssl.so.3", "libssl.so.1.1", "libssl.so.1.0.2", "libssl.so.1.0.0", "libssl.so.10", "libssl.so"];

    static readonly Lazy<bool> NoOpenSsl = new(() => OperatingSystem.IsLinux() && !LibSsl.Any(n =>
    {
        if (!NativeLibrary.TryLoad(n, out var h)) return false;
        NativeLibrary.Free(h);
        return true;
    }));

    /// <summary>Why the request cannot be made here, or null.</summary>
    internal static string? Refusal(Uri uri, bool noOpenSsl) =>
        noOpenSsl && uri.Scheme == Uri.UriSchemeHttps
            ? $"{uri.Host}: https needs OpenSSL (libssl), which this system lacks; on Debian or Ubuntu: sudo apt install openssl ca-certificates"
            : null;

    /// <summary>A failed TLS connection said with its reason, and on Linux what is likely missing; other failures as they are.</summary>
    internal static HttpRequestException Explained(HttpRequestException ex, Uri uri, bool linux)
    {
        if (ex.HttpRequestError != HttpRequestError.SecureConnectionError || ex.InnerException is not AuthenticationException a) return ex;
        var hint = linux ? "; no trusted root certificates here? On Debian or Ubuntu: sudo apt install ca-certificates" : "";
        return new HttpRequestException(HttpRequestError.SecureConnectionError, $"{uri.Host}: the secure connection failed: {a.Message}{hint}", ex);
    }

    sealed class Guard(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken ct)
        {
            Check(request);
            try { return base.Send(request, ct); }
            catch (HttpRequestException ex) { throw Explained(ex, request.RequestUri!, OperatingSystem.IsLinux()); }
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Check(request);
            try { return await base.SendAsync(request, ct); }
            catch (HttpRequestException ex) { throw Explained(ex, request.RequestUri!, OperatingSystem.IsLinux()); }
        }

        static void Check(HttpRequestMessage request)
        {
            if (Refusal(request.RequestUri!, NoOpenSsl.Value) is { } why) throw new HttpRequestException(HttpRequestError.SecureConnectionError, why);
        }
    }
}
