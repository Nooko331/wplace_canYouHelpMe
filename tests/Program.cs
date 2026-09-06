using System.Net;
using System.Net.Http;
using System.Security.Authentication;
using WplaceColorWatch;

internal static class Program
{
    private static readonly (string Name, Func<Task> Run)[] Cases =
    {
        ("RedirectSuccessSkipsApiAndResponseBody", RedirectSuccessSkipsApiAndResponseBody),
        ("RelativeLocationDecodesTag", RelativeLocationDecodesTag),
        ("TlsFailureFallsBackToApi", TlsFailureFallsBackToApi),
        ("TimeoutFallsBackToApi", TimeoutFallsBackToApi),
        ("TlsAndRateLimitPreserveDiagnosticsWithoutRetry", TlsAndRateLimitPreserveDiagnosticsWithoutRetry),
        ("InvalidRedirectsFallBackToApi", InvalidRedirectsFallBackToApi),
        ("InvalidApiResponsesReturnFailure", InvalidApiResponsesReturnFailure),
        ("OfflineReturnsFailureWithoutThrowing", OfflineReturnsFailureWithoutThrowing),
        ("WarningIsPersistedWithoutDebug", WarningIsPersistedWithoutDebug)
    };

    private static async Task<int> Main(string[] args)
    {
        if (args.Contains("--list"))
        {
            foreach (var test in Cases) Console.WriteLine(test.Name);
            return 0;
        }
        if (args.Contains("--live"))
        {
            var result = await ReleaseUpdateChecker.CheckAsync();
            Console.WriteLine(result.Tag == null ? result.Failure : $"Live latest release: {result.Tag}");
            return result.Tag == null ? 1 : 0;
        }

        int failures = 0;
        foreach (var test in Cases)
        {
            try
            {
                await test.Run();
                Console.WriteLine($"PASS {test.Name}");
            }
            catch (Exception ex)
            {
                failures++;
                Console.WriteLine($"FAIL {test.Name}: {ex}");
            }
        }
        Console.WriteLine($"{Cases.Length - failures}/{Cases.Length} passed");
        return failures == 0 ? 0 : 1;
    }

    private static async Task RedirectSuccessSkipsApiAndResponseBody()
    {
        using var fixture = new Fixture(() => Redirect("https://github.com/Nooko331/wplace_canYouHelpMe/releases/tag/v1.6.1"));
        var result = await fixture.Check();
        Require(result.Tag == "v1.6.1" && result.Failure == null, "Expected redirect release");
        Require(fixture.Web.Calls == 1 && fixture.Api.Calls == 0, "Web success must not consume API quota");
    }

    private static async Task RelativeLocationDecodesTag()
    {
        using var fixture = new Fixture(() => Redirect("/Nooko331/wplace_canYouHelpMe/releases/tag/v1.6.1%2Bfix"));
        var result = await fixture.Check();
        Require(result.Tag == "v1.6.1+fix" && fixture.Api.Calls == 0, "Relative/escaped Location was not parsed");
    }

    private static async Task TlsFailureFallsBackToApi()
    {
        using var fixture = new Fixture(() => throw TlsFailure());
        var result = await fixture.Check();
        Require(result.Tag == "v1.6.1" && result.Failure == null, "API fallback did not recover TLS failure");
        Require(fixture.Web.Calls == 1 && fixture.Api.Calls == 1, "Unexpected retries");
    }

    private static async Task TimeoutFallsBackToApi()
    {
        using var fixture = new Fixture(() => throw new TaskCanceledException("request timeout", new TimeoutException("6 seconds elapsed")));
        var result = await fixture.Check();
        Require(result.Tag == "v1.6.1" && result.Failure == null, "Timeout should allow API fallback");
    }

    private static async Task TlsAndRateLimitPreserveDiagnosticsWithoutRetry()
    {
        foreach (var status in new[] { HttpStatusCode.Forbidden, HttpStatusCode.TooManyRequests })
        {
            using var fixture = new Fixture(() => throw TlsFailure(), () =>
            {
                var response = new HttpResponseMessage(status) { ReasonPhrase = "rate limit exceeded" };
                response.Headers.TryAddWithoutValidation("X-RateLimit-Remaining", "0");
                response.Headers.TryAddWithoutValidation("X-RateLimit-Reset", "1788670800");
                response.Headers.TryAddWithoutValidation("Retry-After", "60");
                return response;
            });
            var result = await fixture.Check();
            Require(result.Tag == null && result.Failure != null, "Both failures must not report a version");
            foreach (var text in new[] { "redirect=(", "SecureConnectionError", "AuthenticationException", "certificate chain is untrusted", "0x", "api status " + (int)status, "X-RateLimit-Remaining=0", "X-RateLimit-Reset=1788670800", "Retry-After=60" })
                Require(result.Failure!.Contains(text), $"Missing diagnostic: {text}");
            Require(fixture.Web.Calls == 1 && fixture.Api.Calls == 1, "Do not retry rate-limited API");
        }
    }

    private static async Task InvalidRedirectsFallBackToApi()
    {
        foreach (var location in new string?[] { null, "https://example.com/releases/tag/v9.0.0", "https://github.com/login", "http://github.com/Nooko331/wplace_canYouHelpMe/releases/tag/v9.0.0", "/Nooko331/wplace_canYouHelpMe/releases/tag/", "/Nooko331/wplace_canYouHelpMe/releases/tag/%20" })
        {
            using var fixture = new Fixture(() => Redirect(location));
            var result = await fixture.Check();
            Require(result.Tag == "v1.6.1" && fixture.Api.Calls == 1, $"Invalid Location accepted: {location}");
        }
        using var wrongStatus = new Fixture(() => Redirect("/Nooko331/wplace_canYouHelpMe/releases/tag/v9.0.0", HttpStatusCode.Forbidden));
        Require((await wrongStatus.Check()).Tag == "v1.6.1", "Non-redirect status must not supply a release tag");
    }

    private static async Task InvalidApiResponsesReturnFailure()
    {
        foreach (var body in new[] { "not json", "null", "[]", "{}", "{\"tag_name\":null}", "{\"tag_name\":123}", "{\"tag_name\":\" \"}" })
        {
            using var fixture = new Fixture(() => throw TlsFailure(), () => Json(body));
            var result = await fixture.Check();
            Require(result.Tag == null && result.Failure!.Contains("api=("), $"Invalid API body accepted: {body}");
        }
    }

    private static async Task OfflineReturnsFailureWithoutThrowing()
    {
        using var fixture = new Fixture(
            () => throw new HttpRequestException(HttpRequestError.NameResolutionError, "DNS lookup failed"),
            () => throw new HttpRequestException(HttpRequestError.ConnectionError, "Connection refused"));
        var result = await fixture.Check();
        Require(result.Tag == null && result.Failure!.Contains("NameResolutionError") && result.Failure.Contains("ConnectionError"), "Offline diagnostics were lost");
    }

    private static Task WarningIsPersistedWithoutDebug()
    {
        Logger.Init(debug: false);
        string marker = "[update] regression " + Guid.NewGuid().ToString("N");
        Logger.Warning(marker);
        string path = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath)!, "wplace_canYouHelpMe_error_log.txt");
        Require(File.ReadAllLines(path).Any(line => line.Contains(" WARN " + marker)), "Warning was not persisted without debug logging");
        Logger.Shutdown();
        return Task.CompletedTask;
    }

    private static HttpRequestException TlsFailure() => new(HttpRequestError.SecureConnectionError,
        "The SSL connection could not be established, see inner exception.",
        new AuthenticationException("certificate chain is untrusted"));

    private static HttpResponseMessage Redirect(string? location, HttpStatusCode status = HttpStatusCode.Found)
    {
        var response = new HttpResponseMessage(status) { Content = new UnreadableBody() };
        if (location != null) response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
        return response;
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly StubHandler Web;
        internal readonly StubHandler Api;
        private readonly HttpClient _webClient;
        private readonly HttpClient _apiClient;

        internal Fixture(Func<HttpResponseMessage> web, Func<HttpResponseMessage>? api = null)
        {
            Web = new StubHandler("github.com", web);
            Api = new StubHandler("api.github.com", api ?? (() => Json("{\"tag_name\":\"v1.6.1\"}")));
            _webClient = new HttpClient(Web);
            _apiClient = new HttpClient(Api);
        }

        internal Task<(string? Tag, string? Failure)> Check() => ReleaseUpdateChecker.CheckAsync(_webClient, _apiClient);
        public void Dispose() { _webClient.Dispose(); _apiClient.Dispose(); }
    }

    private sealed class StubHandler(string host, Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        internal int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Require(request.Method == HttpMethod.Get && request.RequestUri?.Host == host && request.RequestUri.AbsolutePath.EndsWith("/releases/latest"), "Unexpected update request");
            return Task.FromResult(respond());
        }
    }

    private sealed class UnreadableBody : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => throw new IOException("Redirect body must not be downloaded");
        protected override bool TryComputeLength(out long length) { length = 1; return true; }
    }
}
