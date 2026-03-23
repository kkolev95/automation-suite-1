using System.Net;
using FluentAssertions;
using TestIT.ApiTests.Helpers;
using TestIT.ApiTests.Models;
using Xunit;
using Xunit.Abstractions;

namespace TestIT.ApiTests.Tests;

/// <summary>
/// Verifies the API handles high-frequency request bursts without server errors.
/// Tests confirm that any rate limiting in place returns well-formed 4xx responses
/// (HTTP 429 Too Many Requests) rather than crashing (5xx), and that non-rate-limited
/// endpoints continue to respond correctly under concurrent load.
/// </summary>
[Collection("RateLimiting")]
public class RateLimitingTests : IDisposable
{
    private readonly ApiClient _apiClient;
    private readonly string _baseUrl;
    private readonly List<ApiClient> _clients = new();

    public RateLimitingTests(ITestOutputHelper output)
    {
        ApiClient.SetOutput(output.WriteLine);
        _baseUrl = TestConfiguration.GetBaseUrl();
        _apiClient = new ApiClient(_baseUrl, enableRetry: false);
    }

    private ApiClient CreateClient()
    {
        var c = new ApiClient(_baseUrl, enableRetry: false);
        _clients.Add(c);
        return c;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Read endpoints under burst load
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    [Trait("Category", "RateLimit")]
    [Trait("Priority", "P2")]
    public async Task RateLimit_HighFrequencyPublicReads_NeverReturn500()
    {
        // Arrange: 30 concurrent GET /tests/public/ from separate clients
        const int concurrentReads = 30;
        var tasks = Enumerable.Range(0, concurrentReads).Select(_ => Task.Run(async () =>
        {
            var client = CreateClient();
            return (await client.GetAsync("tests/public/")).StatusCode;
        })).ToList();

        var results = await Task.WhenAll(tasks);

        // Assert: each response must be 200 or 429, never 5xx
        results.Should().AllSatisfy(status =>
        {
            ((int)status).Should().NotBe(500,
                $"high-frequency public list reads must not cause server errors — got {status}");
            status.Should().BeOneOf(
                new[] { HttpStatusCode.OK, HttpStatusCode.TooManyRequests },
                $"public reads under burst load must return 200 or 429, not {status}");
        }, "no 5xx responses allowed under high concurrency");

        // At least some reads must succeed
        results.Should().Contain(HttpStatusCode.OK,
            "at least some concurrent reads must succeed even under high load");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Auth endpoints under burst load
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    [Trait("Category", "RateLimit")]
    [Trait("Priority", "P2")]
    public async Task RateLimit_HighFrequencyLoginAttempts_NeverReturn500()
    {
        // Arrange: 25 sequential failed login attempts with distinct fake emails
        const int attempts = 25;
        var statusCodes = new List<HttpStatusCode>();

        for (int i = 0; i < attempts; i++)
        {
            var fakeEmail = $"noone_{i}_{Guid.NewGuid().ToString("N")[..6]}@example.com";
            var resp = await _apiClient.PostAsync("auth/login/",
                new LoginRequest { Email = fakeEmail, Password = "WrongPass123!" });
            statusCodes.Add(resp.StatusCode);
        }

        // Assert: 401 (wrong credentials) or 429 (rate limited), never 500
        statusCodes.Should().AllSatisfy(status =>
        {
            ((int)status).Should().NotBe(500,
                $"high-frequency failed logins must not cause server errors — got {status}");
            status.Should().BeOneOf(
                new[] { HttpStatusCode.Unauthorized, HttpStatusCode.TooManyRequests },
                $"failed login bursts must return 401 or 429, not {status}");
        }, "none of the 25 rapid login attempts should trigger a server error");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Write endpoints under burst load
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    [Trait("Category", "RateLimit")]
    [Trait("Priority", "P2")]
    public async Task RateLimit_HighFrequencyTestCreations_NeverReturn500()
    {
        // Arrange: authenticated client creating 20 tests in rapid succession
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        const int creations = 20;
        var statusCodes = new List<HttpStatusCode>();

        for (int i = 0; i < creations; i++)
        {
            var resp = await _apiClient.PostAsync("tests/", new CreateTestRequest
            {
                Title       = $"RLTest_{i}_{Guid.NewGuid().ToString("N")[..6]}",
                Visibility  = "link_only",
                MaxAttempts = 1
            });
            statusCodes.Add(resp.StatusCode);
        }

        // Assert: 201 (created) or 429 (rate limited), never 5xx
        statusCodes.Should().AllSatisfy(status =>
        {
            ((int)status).Should().NotBe(500,
                $"rapid test creation must not cause server errors — got {status}");
            status.Should().BeOneOf(
                new[] { HttpStatusCode.Created, HttpStatusCode.TooManyRequests },
                $"rapid test creation must return 201 or 429, not {status}");
        }, "none of the 20 rapid test creations should trigger a server error");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 429 response quality
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    [Trait("Category", "RateLimit")]
    [Trait("Priority", "P2")]
    public async Task RateLimit_If429Returned_ResponseBodyIsWellFormed()
    {
        // Arrange: 40 concurrent GET /tests/public/ to maximise chance of triggering 429
        const int concurrentReads = 40;
        var tasks = Enumerable.Range(0, concurrentReads).Select(_ => Task.Run(async () =>
        {
            var client = CreateClient();
            var resp = await client.GetAsync("tests/public/");
            var body = await resp.Content.ReadAsStringAsync();
            return (Status: resp.StatusCode, Body: body);
        })).ToList();

        var results = await Task.WhenAll(tasks);

        // Assert: no 5xx at all
        results.Should().AllSatisfy(r =>
            ((int)r.Status).Should().NotBe(500,
                $"no request should cause a server error — got {r.Status}"),
            "no 5xx responses allowed regardless of rate limiting");

        // If any 429s were returned, their bodies must be JSON (not HTML)
        var rateLimited = results.Where(r => r.Status == HttpStatusCode.TooManyRequests).ToList();
        foreach (var (_, body) in rateLimited)
        {
            body.TrimStart().Should().StartWith("{",
                "a 429 rate-limit response body must be a JSON object, not an HTML error page");
        }
    }

    public void Dispose()
    {
        foreach (var c in _clients)
            c.Dispose();
        _apiClient?.Dispose();
    }
}
