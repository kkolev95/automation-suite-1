using System.Net;
using FluentAssertions;
using TestIT.ApiTests.Helpers;
using TestIT.ApiTests.Models;
using Xunit;
using Xunit.Abstractions;

namespace TestIT.ApiTests.Tests;

/// <summary>
/// Verifies that pagination-style query parameters (limit, offset) are handled gracefully
/// even though the API does not implement server-side pagination:
///   - Limit=0 does not cause a server error
///   - Negative limit does not cause a server error
///   - Non-numeric limit does not cause a server error
///   - Very large offset does not cause a server error
///   - Combined limit+offset are silently ignored and all items returned
///   - A large number of public tests are all returned without silent truncation
/// </summary>
public class PaginationBoundaryTests : IDisposable
{
    private readonly ApiClient _apiClient;
    private readonly ApiClient _anonClient;

    public PaginationBoundaryTests(ITestOutputHelper output)
    {
        ApiClient.SetOutput(output.WriteLine);
        var baseUrl = TestConfiguration.GetBaseUrl();
        _apiClient = new ApiClient(baseUrl);
        _anonClient = new ApiClient(baseUrl);
    }

    [Fact]
    [Trait("Category", "Boundary")]
    [Trait("Priority", "P2")]
    public async Task Pagination_LimitZero_DoesNotCauseError()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        await TestDataHelper.CreateTestAsync(_apiClient,
            $"LimitZero_{Guid.NewGuid().ToString("N")[..8]}");

        // Act: ?limit=0 — should not blow up the server
        var response = await _apiClient.GetAsync("tests/?limit=0");
        var body = await response.Content.ReadAsStringAsync();

        // Assert: must never be a server error; 200 (ignored) or 400 (rejected) are acceptable
        ((int)response.StatusCode).Should().NotBe(500,
            $"limit=0 must not cause a server error. Body: {body[..Math.Min(200, body.Length)]}");
        response.StatusCode.Should().BeOneOf(
            new[] { HttpStatusCode.OK, HttpStatusCode.BadRequest },
            $"limit=0 must return 200 or 400, not {response.StatusCode}");
    }

    [Fact]
    [Trait("Category", "Boundary")]
    [Trait("Priority", "P2")]
    public async Task Pagination_NegativeLimit_DoesNotCauseServerError()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);

        // Act: ?limit=-1 — negative pagination params should not crash the server
        var response = await _apiClient.GetAsync("tests/?limit=-1");
        var body = await response.Content.ReadAsStringAsync();

        // Assert
        ((int)response.StatusCode).Should().NotBe(500,
            $"limit=-1 must not cause a server error. Body: {body[..Math.Min(200, body.Length)]}");
        response.StatusCode.Should().BeOneOf(
            new[] { HttpStatusCode.OK, HttpStatusCode.BadRequest },
            $"limit=-1 must return 200 or 400, not {response.StatusCode}");
    }

    [Fact]
    [Trait("Category", "Boundary")]
    [Trait("Priority", "P2")]
    public async Task Pagination_NonNumericLimit_DoesNotCauseServerError()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);

        // Act: ?limit=abc — non-numeric pagination params should not crash
        var response = await _apiClient.GetAsync("tests/?limit=abc");
        var body = await response.Content.ReadAsStringAsync();

        // Assert
        ((int)response.StatusCode).Should().NotBe(500,
            $"limit=abc must not cause a server error. Body: {body[..Math.Min(200, body.Length)]}");
        response.StatusCode.Should().BeOneOf(
            new[] { HttpStatusCode.OK, HttpStatusCode.BadRequest },
            $"limit=abc must return 200 (ignored) or 400 (rejected), not {response.StatusCode}");
    }

    [Fact]
    [Trait("Category", "Boundary")]
    [Trait("Priority", "P2")]
    public async Task Pagination_VeryLargeOffset_DoesNotCauseServerError()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);

        // Act: ?offset=999999999 — astronomically large offset must not crash
        var response = await _apiClient.GetAsync("tests/?offset=999999999");
        var body = await response.Content.ReadAsStringAsync();

        // Assert: may return all items (if ignoring offset) or an empty list, but never 5xx
        ((int)response.StatusCode).Should().NotBe(500,
            $"offset=999999999 must not cause a server error. Body: {body[..Math.Min(200, body.Length)]}");
        response.StatusCode.Should().BeOneOf(
            new[] { HttpStatusCode.OK, HttpStatusCode.BadRequest },
            $"huge offset must return 200 or 400, not {response.StatusCode}");
    }

    [Fact]
    [Trait("Category", "Boundary")]
    [Trait("Priority", "P2")]
    public async Task Pagination_BothLimitAndOffset_AreIgnoredAndAllItemsReturned()
    {
        // Arrange: create 5 tests
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var createdSlugs = new List<string>();
        for (int i = 1; i <= 5; i++)
        {
            var t = await TestDataHelper.CreateTestAsync(_apiClient,
                $"PagCombo_{i}_{Guid.NewGuid().ToString("N")[..6]}");
            createdSlugs.Add(t.Slug);
        }

        // Act: combined limit + offset — the API ignores these and returns everything
        var response = await _apiClient.GetAsync("tests/?limit=2&offset=1");
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "combined limit+offset must not cause a rejection — the API ignores them");

        var tests = await _apiClient.DeserializeResponseAsync<List<TestResponse>>(response);
        var slugs = tests!.Select(t => t.Slug).ToList();

        // Assert: all 5 tests appear — no truncation
        foreach (var slug in createdSlugs)
        {
            slugs.Should().Contain(slug,
                $"test '{slug}' must appear — pagination params are ignored and all items returned");
        }
    }

    [Fact]
    [Trait("Category", "Boundary")]
    [Trait("Priority", "P2")]
    public async Task PublicList_WithManyPublicTests_ReturnsAllWithoutTruncation()
    {
        // Arrange: create 12 public tests
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var publicSlugs = new List<string>();
        for (int i = 1; i <= 12; i++)
        {
            var t = await TestDataHelper.CreateTestAsync(_apiClient,
                $"ManyPublic_{i}_{Guid.NewGuid().ToString("N")[..6]}",
                visibility: "public");
            publicSlugs.Add(t.Slug);
        }

        // Act: anonymous GET of the public list
        var response = await _anonClient.GetAsync("tests/public/");
        response.StatusCode.Should().Be(HttpStatusCode.OK, "public test list must be accessible");

        var tests = await _anonClient.DeserializeResponseAsync<List<TestResponse>>(response);
        var returnedSlugs = tests!.Select(t => t.Slug).ToList();

        // Assert: all 12 public tests must be present — no silent truncation
        foreach (var slug in publicSlugs)
        {
            returnedSlugs.Should().Contain(slug,
                $"public test '{slug}' must appear in GET /tests/public/ — no silent truncation");
        }
    }

    public void Dispose()
    {
        _apiClient?.Dispose();
        _anonClient?.Dispose();
    }
}
