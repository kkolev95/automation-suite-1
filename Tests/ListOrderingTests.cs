using System.Net;
using FluentAssertions;
using TestIT.ApiTests.Helpers;
using TestIT.ApiTests.Models;
using Xunit;
using Xunit.Abstractions;

namespace TestIT.ApiTests.Tests;

/// <summary>
/// Verifies ordering and completeness guarantees on list endpoints:
///   - GET /tests/           → newest first, plain array, pagination params ignored
///   - GET /tests/{slug}/results/ → newest submitted first
///   - GET /tests/public/    → plain array, all public tests included
/// </summary>
public class ListOrderingTests : IDisposable
{
    private readonly ApiClient _apiClient;

    public ListOrderingTests(ITestOutputHelper output)
    {
        ApiClient.SetOutput(output.WriteLine);
        _apiClient = new ApiClient(TestConfiguration.GetBaseUrl());
    }

    [Fact]
    [Trait("Category", "Functional")]
    [Trait("Priority", "P1")]
    public async Task TestList_IsOrderedNewestFirst()
    {
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);

        // Create 3 tests in sequence — track the slug of the last one
        string? lastSlug = null;
        for (int i = 1; i <= 3; i++)
        {
            var test = await TestDataHelper.CreateTestAsync(_apiClient,
                $"OrderTest_{i}_{Guid.NewGuid().ToString("N")[..6]}");
            lastSlug = test.Slug;
        }

        var response = await _apiClient.GetAsync("tests/");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var tests = await _apiClient.DeserializeResponseAsync<List<TestResponse>>(response);
        tests.Should().NotBeNullOrEmpty("test list must contain the created tests");
        tests![0].Slug.Should().Be(lastSlug,
            "the most recently created test must appear first (newest-first ordering)");
    }

    [Fact]
    [Trait("Category", "Functional")]
    [Trait("Priority", "P1")]
    public async Task TestList_ContainsAllCreatedTests()
    {
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);

        var createdSlugs = new List<string>();
        for (int i = 1; i <= 3; i++)
        {
            var test = await TestDataHelper.CreateTestAsync(_apiClient,
                $"CompleteTest_{i}_{Guid.NewGuid().ToString("N")[..6]}");
            createdSlugs.Add(test.Slug);
        }

        var response = await _apiClient.GetAsync("tests/");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var tests = await _apiClient.DeserializeResponseAsync<List<TestResponse>>(response);
        var returnedSlugs = tests!.Select(t => t.Slug).ToList();

        foreach (var slug in createdSlugs)
        {
            returnedSlugs.Should().Contain(slug,
                $"test '{slug}' was created but is missing from the list response");
        }
    }

    [Fact]
    [Trait("Category", "Functional")]
    [Trait("Priority", "P1")]
    public async Task TestList_PaginationParamsIgnored_ReturnsAllItems()
    {
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);

        var createdSlugs = new List<string>();
        for (int i = 1; i <= 3; i++)
        {
            var test = await TestDataHelper.CreateTestAsync(_apiClient,
                $"PaginationTest_{i}_{Guid.NewGuid().ToString("N")[..6]}");
            createdSlugs.Add(test.Slug);
        }

        // Request with limit=1 — the API ignores this and returns everything
        var response = await _apiClient.GetAsync("tests/?limit=1");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var tests = await _apiClient.DeserializeResponseAsync<List<TestResponse>>(response);
        var returnedSlugs = tests!.Select(t => t.Slug).ToList();

        foreach (var slug in createdSlugs)
        {
            returnedSlugs.Should().Contain(slug,
                $"pagination param 'limit=1' must be ignored — all tests including '{slug}' should be returned");
        }
    }

    [Fact]
    [Trait("Category", "Functional")]
    [Trait("Priority", "P1")]
    public async Task ResultsList_IsOrderedNewestSubmittedFirst()
    {
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"ResultsOrder_{Guid.NewGuid().ToString("N")[..8]}");
        await TestDataHelper.AddQuestionAsync(_apiClient, test.Slug, "Ordering Q");

        // Submit 3 attempts in sequence with distinct names
        var names = new[] { "First", "Second", "Third" };
        foreach (var name in names)
        {
            using var anonClient = new ApiClient(TestConfiguration.GetBaseUrl());
            var startResp = await anonClient.PostAsync($"tests/{test.Slug}/attempts/",
                new StartAttemptRequest { AnonymousName = name });
            startResp.StatusCode.Should().Be(HttpStatusCode.Created);
            var attempt = await anonClient.DeserializeResponseAsync<AttemptResponse>(startResp);

            await anonClient.PostAsync(
                $"tests/{test.Slug}/attempts/{attempt!.Id}/submit/",
                new Dictionary<string, object>());
        }

        var resultsResp = await _apiClient.GetAsync($"tests/{test.Slug}/results/");
        resultsResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var results = await _apiClient.DeserializeResponseAsync<List<ResultResponse>>(resultsResp);

        results.Should().HaveCountGreaterThanOrEqualTo(3);

        // Verify timestamps are non-increasing (newest first)
        for (int i = 0; i < results!.Count - 1; i++)
        {
            var current = results[i].SubmittedAt;
            var next = results[i + 1].SubmittedAt;
            if (current != null && next != null)
            {
                string.Compare(current, next, StringComparison.Ordinal).Should().BeGreaterThanOrEqualTo(0,
                    $"result at index {i} (submitted_at='{current}') should be newer than index {i + 1} (submitted_at='{next}')");
            }
        }
    }

    [Fact]
    [Trait("Category", "Functional")]
    [Trait("Priority", "P1")]
    public async Task PublicTestList_IsPlainArray_ContainsPublicTests()
    {
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"PublicList_{Guid.NewGuid().ToString("N")[..8]}",
            visibility: "public");

        // Public endpoint is unauthenticated
        using var anonClient = new ApiClient(TestConfiguration.GetBaseUrl());
        var response = await anonClient.GetAsync("tests/public/");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        body.TrimStart().Should().StartWith("[",
            "public test list must be a plain JSON array, not a paginated envelope");

        var tests = await anonClient.DeserializeResponseAsync<List<TestResponse>>(response);
        tests.Should().Contain(t => t.Slug == test.Slug,
            "a newly created public test must appear in GET /tests/public/");
    }

    public void Dispose() => _apiClient?.Dispose();
}
