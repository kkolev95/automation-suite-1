using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using TestIT.ApiTests.Helpers;
using TestIT.ApiTests.Models;
using Xunit;
using Xunit.Abstractions;

namespace TestIT.ApiTests.Tests;

/// <summary>
/// Verifies HTTP/REST protocol compliance at the transport and content-negotiation level:
///   - DELETE idempotency (second delete → 404, not 5xx)
///   - Unknown request fields are silently ignored (robust parsing)
///   - Malformed JSON bodies are rejected gracefully (400/415, not 500)
///   - Empty body where JSON is required is rejected gracefully (400/415, not 500)
///   - All success responses carry Content-Type: application/json
///   - Error responses are JSON, not HTML error pages
///   - Unicode and emoji content round-trips without corruption
///   - Wrong Content-Type (text/plain) is rejected, not silently accepted
/// </summary>
public class HttpProtocolTests : IDisposable
{
    private readonly ApiClient _apiClient;
    private readonly HttpClient _rawHttp;
    private readonly string _baseUrl;

    public HttpProtocolTests(ITestOutputHelper output)
    {
        ApiClient.SetOutput(output.WriteLine);
        _baseUrl = TestConfiguration.GetBaseUrl();
        _apiClient = new ApiClient(_baseUrl);

        _rawHttp = new HttpClient
        {
            BaseAddress = new Uri(_baseUrl),
            DefaultRequestVersion = new Version(1, 1),
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact
        };
        _rawHttp.DefaultRequestHeaders.Add("Accept", "*/*");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Idempotency
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    [Trait("Category", "Protocol")]
    [Trait("Priority", "P1")]
    public async Task Protocol_DeleteIsIdempotent_SecondDeleteReturns404()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"DelIdem_{Guid.NewGuid().ToString("N")[..8]}");

        // First delete — must succeed
        var firstDelete = await _apiClient.DeleteAsync($"tests/{test.Slug}/");
        firstDelete.StatusCode.Should().Be(HttpStatusCode.NoContent,
            "first DELETE of an existing resource must return 204");

        // Act: second delete of the same already-deleted resource
        var secondDelete = await _apiClient.DeleteAsync($"tests/{test.Slug}/");

        // Assert: REST DELETE is idempotent — 404 on subsequent calls, never 5xx
        secondDelete.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "second DELETE of an already-deleted resource must return 404, not cause a server error");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Request body robustness
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    [Trait("Category", "Protocol")]
    [Trait("Priority", "P2")]
    public async Task Protocol_UnknownJsonFields_AreIgnored_NotRejected()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        string uniqueTitle = $"UnknownFields_{Guid.NewGuid().ToString("N")[..8]}";

        // Act: POST with extra unknown fields alongside the valid ones
        var response = await _apiClient.PostAsync("tests/", new
        {
            title              = uniqueTitle,
            visibility         = "link_only",
            max_attempts       = 3,
            show_answers_after = false,
            _unknown_field_xyz = "should be silently ignored",
            another_unknown    = 42
        });
        var body = await response.Content.ReadAsStringAsync();

        // Assert: robust APIs silently ignore unrecognised fields rather than rejecting the request
        response.StatusCode.Should().Be(HttpStatusCode.Created,
            $"unknown JSON fields must be silently ignored, not cause a 4xx rejection. Body: {body}");
    }

    [Fact]
    [Trait("Category", "Protocol")]
    [Trait("Priority", "P1")]
    public async Task Protocol_MalformedJsonBody_IsRejectedGracefully_NotServerError()
    {
        // Arrange: POST /auth/login/ is public (no auth needed) — use it to test malformed body
        var request = new HttpRequestMessage(HttpMethod.Post, "auth/login/")
        {
            Content = new StringContent("{ this_is: not !! valid json }", Encoding.UTF8, "application/json")
        };

        // Act
        var response = await _rawHttp.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        // Assert: must return 400 or 415, never 5xx
        ((int)response.StatusCode).Should().BeOneOf(new[] { 400, 415 },
            $"malformed JSON must be rejected with 400/415, not cause a server error. " +
            $"Status: {(int)response.StatusCode}, Body: {body[..Math.Min(300, body.Length)]}");
    }

    [Fact]
    [Trait("Category", "Protocol")]
    [Trait("Priority", "P1")]
    public async Task Protocol_EmptyBody_WhenJsonRequired_IsRejectedGracefully()
    {
        // Arrange: POST /auth/register/ requires a JSON body with required fields
        var request = new HttpRequestMessage(HttpMethod.Post, "auth/register/")
        {
            Content = new StringContent(string.Empty, Encoding.UTF8, "application/json")
        };

        // Act
        var response = await _rawHttp.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        // Assert: empty JSON body must be rejected gracefully — 400 or 415, never 500
        ((int)response.StatusCode).Should().BeOneOf(new[] { 400, 415 },
            $"empty body must be rejected gracefully. " +
            $"Status: {(int)response.StatusCode}, Body: {body[..Math.Min(300, body.Length)]}");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Response headers
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    [Trait("Category", "Protocol")]
    [Trait("Priority", "P2")]
    public async Task Protocol_SuccessResponses_HaveJsonContentType()
    {
        // Arrange: use a public endpoint — no auth required
        // Act
        var response = await _rawHttp.GetAsync("tests/public/");
        var mediaType = response.Content.Headers.ContentType?.MediaType;

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "public test list must be accessible without authentication");
        mediaType.Should().Be("application/json",
            "all API success responses must declare Content-Type: application/json");
    }

    [Fact]
    [Trait("Category", "Protocol")]
    [Trait("Priority", "P2")]
    public async Task Protocol_ErrorResponses_AreJsonNotHtml()
    {
        // Arrange: trigger a 401 by logging in with wrong credentials (no auth setup needed)
        var request = new HttpRequestMessage(HttpMethod.Post, "auth/login/")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { email = "nobody@example.com", password = "WrongPass99!" }),
                Encoding.UTF8, "application/json")
        };

        // Act
        var response = await _rawHttp.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        var mediaType = response.Content.Headers.ContentType?.MediaType;

        // Assert: error responses must be JSON, not an HTML error page
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "wrong credentials must return 401");
        mediaType.Should().Be("application/json",
            $"error responses must be JSON, not HTML. Body: {body[..Math.Min(200, body.Length)]}");
        body.TrimStart().Should().StartWith("{",
            "error response body must be a JSON object, not an HTML document or plain text");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Content handling
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    [Trait("Category", "Protocol")]
    [Trait("Priority", "P2")]
    public async Task Protocol_UnicodeAndEmoji_InTestTitle_RoundTripCorrectly()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        string unicodeTitle =
            $"Unicode \U0001f3af Ñoño — café résumé {Guid.NewGuid().ToString("N")[..4]}";

        // Act: create a test with Unicode/emoji in the title
        var createResp = await _apiClient.PostAsync("tests/", new CreateTestRequest
        {
            Title      = unicodeTitle,
            Visibility = "link_only",
            MaxAttempts = 1
        });
        createResp.StatusCode.Should().Be(HttpStatusCode.Created,
            "Unicode and emoji characters must be accepted in test titles");
        var created = await _apiClient.DeserializeResponseAsync<TestResponse>(createResp);

        // Fetch back and verify round-trip fidelity
        var fetched = await _apiClient.GetAsync<TestResponse>($"tests/{created!.Slug}/");

        // Assert
        fetched!.Title.Should().Be(unicodeTitle,
            "Unicode and emoji characters must survive a write/read round-trip without corruption");
    }

    [Fact]
    [Trait("Category", "Protocol")]
    [Trait("Priority", "P2")]
    public async Task Protocol_WrongContentType_IsRejectedOrHandledGracefully()
    {
        // Arrange: POST /auth/login/ with Content-Type: text/plain instead of application/json
        var request = new HttpRequestMessage(HttpMethod.Post, "auth/login/")
        {
            Content = new StringContent(
                "{\"email\":\"test@example.com\",\"password\":\"Test123!\"}",
                Encoding.UTF8,
                "text/plain")
        };

        // Act
        var response = await _rawHttp.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        int statusCode = (int)response.StatusCode;

        // Assert: wrong Content-Type must never cause a server error (5xx),
        // and should return a proper client-side error (400/401/415)
        statusCode.Should().NotBe(500,
            $"wrong Content-Type must not cause a server error. " +
            $"Status: {statusCode}, Body: {body[..Math.Min(200, body.Length)]}");
        statusCode.Should().BeInRange(400, 499,
            $"wrong Content-Type must result in a 4xx client error. " +
            $"Status: {statusCode}, Body: {body[..Math.Min(200, body.Length)]}");
    }

    public void Dispose()
    {
        _apiClient?.Dispose();
        _rawHttp?.Dispose();
    }
}
