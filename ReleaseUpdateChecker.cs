using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace WplaceColorWatch
{

internal static class ReleaseUpdateChecker
{
    internal const string RepositoryUrl = "https://github.com/Nooko331/wplace_canYouHelpMe";
    internal const string LatestReleaseUrl = RepositoryUrl + "/releases/latest";
    private const string LatestReleaseApiUrl = "https://api.github.com/repos/Nooko331/wplace_canYouHelpMe/releases/latest";
    private const string ReleaseTagPath = "/Nooko331/wplace_canYouHelpMe/releases/tag/";

    internal static async Task<(string? Tag, string? Failure)> CheckAsync()
    {
        using var redirect = CreateClient(allowAutoRedirect: false);
        using var api = CreateClient(allowAutoRedirect: true);
        return await CheckAsync(redirect, api).ConfigureAwait(false);
    }

    private static HttpClient CreateClient(bool allowAutoRedirect)
    {
        var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = allowAutoRedirect })
        {
            Timeout = TimeSpan.FromSeconds(6)
        };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("wplace_canYouHelpMe-update-check");
        return http;
    }

    // 网页成功时无需消耗共享 IP 的 GitHub API 配额；每个来源最多请求一次。
    internal static async Task<(string? Tag, string? Failure)> CheckAsync(HttpClient redirect, HttpClient api)
    {
        string redirectError;
        try
        {
            return (await GetTagViaRedirectAsync(redirect).ConfigureAwait(false), null);
        }
        catch (Exception ex)
        {
            redirectError = DescribeException(ex);
        }

        try
        {
            return (await GetTagViaApiAsync(api).ConfigureAwait(false), null);
        }
        catch (Exception ex)
        {
            return (null, $"redirect=({redirectError}); api=({DescribeException(ex)})");
        }
    }

    private static async Task<string> GetTagViaRedirectAsync(HttpClient http)
    {
        // 只需 Location 头，不下载页面正文；SSL 和证书验证仍使用系统默认设置。
        using var response = await http.GetAsync(LatestReleaseUrl, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
        int status = (int)response.StatusCode;
        if (status != 301 && status != 302 && status != 303 && status != 307 && status != 308)
        {
            throw CreateResponseException("redirect", response);
        }

        var location = response.Headers.Location;
        if (location == null)
        {
            throw new InvalidOperationException("redirect response missing Location");
        }

        var target = new Uri(new Uri(LatestReleaseUrl), location);
        if (target.Scheme != Uri.UriSchemeHttps || !target.IsDefaultPort ||
            !string.Equals(target.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(target.UserInfo) ||
            !target.AbsolutePath.StartsWith(ReleaseTagPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("redirect Location is not a release tag in this repository");
        }

        return RequireTag(Uri.UnescapeDataString(target.AbsolutePath.Substring(ReleaseTagPath.Length)));
    }

    private static async Task<string> GetTagViaApiAsync(HttpClient http)
    {
        using var response = await http.GetAsync(LatestReleaseApiUrl).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw CreateResponseException("api", response);
        }

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        if (json.RootElement.ValueKind != JsonValueKind.Object ||
            !json.RootElement.TryGetProperty("tag_name", out var tag) || tag.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException("api response missing string tag_name");
        }

        return RequireTag(tag.GetString());
    }

    private static string RequireTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            throw new InvalidOperationException("release tag is empty");
        }
        return tag;
    }

    private static HttpRequestException CreateResponseException(string source, HttpResponseMessage response)
    {
        var details = new List<string> { $"{source} status {(int)response.StatusCode} {response.ReasonPhrase}" };
        foreach (var name in new[] { "X-RateLimit-Remaining", "X-RateLimit-Reset", "Retry-After" })
        {
            if (response.Headers.TryGetValues(name, out var values))
            {
                details.Add($"{name}={string.Join(",", values)}");
            }
        }
        return new HttpRequestException(string.Join("; ", details), null, response.StatusCode);
    }

    private static string DescribeException(Exception exception)
    {
        var details = new List<string>();
        for (Exception? current = exception; current != null; current = current.InnerException)
        {
            string category = current is HttpRequestException request ? $", {request.HttpRequestError}" : string.Empty;
            string message = current.Message.Replace('\r', ' ').Replace('\n', ' ');
            details.Add($"{current.GetType().Name} (0x{current.HResult:X8}{category}): {message}");
        }
        return string.Join(" -> ", details);
    }
}
}
