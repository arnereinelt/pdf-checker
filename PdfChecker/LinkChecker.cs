using System.Net;

namespace PdfChecker;

internal readonly record struct LinkCheckResult(string Url, bool IsBroken, bool IsProtected, string Status);

/// <summary>
/// Verifies that the web links of a PDF are still reachable. Every URL is requested only once per run.
/// </summary>
internal static class LinkChecker
{
    private static readonly Dictionary<string, LinkCheckResult> Cache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly HttpClient Client = CreateClient();

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 10
        };

        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        // Some sites reject requests without a common user agent.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; PdfChecker/1.0)");

        return client;
    }

    internal static LinkCheckResult Check(string url)
    {
        if (Cache.TryGetValue(url, out LinkCheckResult cached))
        {
            return cached;
        }

        LinkCheckResult result = CheckAsync(url).GetAwaiter().GetResult();
        Cache[url] = result;

        return result;
    }

    private static async Task<LinkCheckResult> CheckAsync(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return new LinkCheckResult(url, true, false, "Invalid URL");
        }

        try
        {
            HttpResponseMessage response = await SendAsync(uri, HttpMethod.Head);

            // Not every server implements HEAD; retry with GET before reporting a defect.
            if (response.StatusCode is HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotImplemented or HttpStatusCode.Forbidden)
            {
                response.Dispose();
                response = await SendAsync(uri, HttpMethod.Get);
            }

            using (response)
            {
                // The target exists but requires authentication - that is not a broken link.
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    return new LinkCheckResult(url, false, true, $"{(int)response.StatusCode} {response.StatusCode} (protected)");
                }

                string status = $"{(int)response.StatusCode} {response.StatusCode}";
                return new LinkCheckResult(url, !response.IsSuccessStatusCode, false, status);
            }
        }
        catch (TaskCanceledException)
        {
            return new LinkCheckResult(url, true, false, "Timeout");
        }
        catch (HttpRequestException ex)
        {
            return new LinkCheckResult(url, true, false, ex.HttpRequestError.ToString());
        }
        catch (Exception ex)
        {
            return new LinkCheckResult(url, true, false, ex.GetType().Name);
        }
    }

    private static async Task<HttpResponseMessage> SendAsync(Uri uri, HttpMethod method)
    {
        using var request = new HttpRequestMessage(method, uri);
        return await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
    }
}
