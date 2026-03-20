using System.Diagnostics;
using System.Net;
using FluentAssertions;
using TestIT.ApiTests.Helpers;
using TestIT.ApiTests.Models;
using Xunit;
using Xunit.Abstractions;

namespace TestIT.ApiTests.Tests;

/// <summary>
/// Verifies API robustness under adversarial and high-frequency conditions:
///   - Rapid repeated failed logins do not trigger server errors
///   - Concurrent DELETE of the same resource produces no 5xx responses
///   - Concurrent registration with the same email produces no 5xx responses
///   - Burst sequential PATCHes do not corrupt data (last-write-wins is consistent)
///   - A test with many questions can be retrieved within an acceptable time budget
/// </summary>
[Collection("Resilience")]
public class ResilienceTests : IDisposable
{
    private readonly ApiClient _apiClient;
    private readonly string _baseUrl;
    private readonly List<ApiClient> _clients = new();

    public ResilienceTests(ITestOutputHelper output)
    {
        ApiClient.SetOutput(output.WriteLine);
        _baseUrl = TestConfiguration.GetBaseUrl();
        _apiClient = new ApiClient(_baseUrl, enableRetry: false);
    }

    private ApiClient CreateClient()
    {
        var client = new ApiClient(_baseUrl, enableRetry: false);
        _clients.Add(client);
        return client;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Brute-force / burst traffic
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    [Trait("Category", "Resilience")]
    [Trait("Priority", "P2")]
    public async Task Resilience_RapidFailedLogins_NeverReturn500()
    {
        // Arrange: use a non-existent email so all attempts fail with 401
        var fakeEmail = $"rapid_fail_{Guid.NewGuid().ToString("N")[..8]}@example.com";
        const int attempts = 10;
        var statusCodes = new List<HttpStatusCode>();

        // Act: 10 rapid sequential failed login attempts
        for (int i = 0; i < attempts; i++)
        {
            var response = await _apiClient.PostAsync("auth/login/",
                new LoginRequest { Email = fakeEmail, Password = "WrongPass123!" });
            statusCodes.Add(response.StatusCode);
        }

        // Assert: every attempt must return 401 — no server errors under rapid brute-force
        statusCodes.Should().AllSatisfy(status =>
            ((int)status).Should().NotBe(500,
                $"rapid failed logins must not cause server errors — got {status}"),
            "none of the 10 rapid failed login attempts should trigger a 500");
        statusCodes.Should().AllSatisfy(s =>
            s.Should().Be(HttpStatusCode.Unauthorized,
                "wrong credentials must consistently return 401 even under rapid repetition"));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Concurrent conflicting operations
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    [Trait("Category", "Resilience")]
    [Trait("Priority", "P1")]
    public async Task Resilience_ConcurrentDelete_SameResource_NoServerErrors()
    {
        // Arrange: create a single test then concurrently delete it from multiple clients
        var (_, _, token) = await TestAccountManager.CreateAndTrackAccountAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"ConcDel_{Guid.NewGuid().ToString("N")[..8]}");

        const int concurrentDeletes = 8;
        var tasks = Enumerable.Range(0, concurrentDeletes).Select(_ => Task.Run(async () =>
        {
            var client = CreateClient();
            client.SetAuthToken(token);
            return (await client.DeleteAsync($"tests/{test.Slug}/")).StatusCode;
        })).ToList();

        var results = await Task.WhenAll(tasks);

        // Assert: the only acceptable outcomes are 204 (deleted) and 404 (already gone) — never 5xx
        results.Should().AllSatisfy(status =>
        {
            ((int)status).Should().NotBe(500,
                $"concurrent DELETE must not cause server errors — got {status}");
            status.Should().BeOneOf(
                new[] { HttpStatusCode.NoContent, HttpStatusCode.NotFound },
                $"concurrent DELETE must return 204 or 404, not {status}");
        }, "all concurrent DELETE results must be safe (no 5xx)");

        int successCount = results.Count(s => s == HttpStatusCode.NoContent);
        successCount.Should().BeGreaterThan(0,
            "at least one concurrent DELETE must succeed with 204 — the resource must have been deleted");
    }

    [Fact]
    [Trait("Category", "Resilience")]
    [Trait("Priority", "P1")]
    public async Task Resilience_ConcurrentDuplicateRegistration_NoServerErrors()
    {
        // Arrange: register an email once (confirmed 201), then concurrently attempt
        // to register the same email 5 more times — simulates a user double-clicking Submit
        var (email, password) = await TestDataHelper.RegisterUserAsync(_apiClient, autoCleanup: true);

        const int concurrentAttempts = 5;
        var tasks = Enumerable.Range(0, concurrentAttempts).Select(i => Task.Run(async () =>
        {
            var client = CreateClient();
            var response = await client.PostAsync("auth/register/", new RegisterRequest
            {
                Email           = email,
                Password        = password,
                PasswordConfirm = password,
                FirstName       = "Duplicate",
                LastName        = $"Attempt{i}"
            });
            return response.StatusCode;
        })).ToList();

        var results = await Task.WhenAll(tasks);

        // Assert: all 5 must be rejected with 400 (email taken); none must cause a server error
        results.Should().AllSatisfy(status =>
            ((int)status).Should().NotBe(500,
                "concurrent duplicate registration must not cause a server error"),
            "none of the duplicate registration attempts should trigger a 500");
        results.Should().AllSatisfy(s =>
            s.Should().Be(HttpStatusCode.BadRequest,
                "re-registering an already-taken email must return 400"),
            "every concurrent duplicate registration must be rejected with 400");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Write burst / data consistency
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    [Trait("Category", "Resilience")]
    [Trait("Priority", "P2")]
    public async Task Resilience_BurstPatches_LastWriteWins_NoDataCorruption()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"BurstPatch_{Guid.NewGuid().ToString("N")[..8]}");

        // Act: 10 rapid sequential PATCHes, each updating the description
        string lastDescription = string.Empty;
        for (int i = 0; i < 10; i++)
        {
            lastDescription = $"Burst description version {i}";
            var patchResp = await _apiClient.PatchAsync($"tests/{test.Slug}/",
                new { description = lastDescription });
            patchResp.StatusCode.Should().Be(HttpStatusCode.OK,
                $"PATCH #{i} must succeed — burst writes must not cause failures");
        }

        // Assert: final state reflects the last write (last-write-wins; no silent data loss)
        var finalTest = await _apiClient.GetAsync<TestResponse>($"tests/{test.Slug}/");
        finalTest!.Description.Should().Be(lastDescription,
            "after 10 rapid PATCHes the persisted description must match the final write");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Large payload retrieval
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    [Trait("Category", "Resilience")]
    [Trait("Priority", "P2")]
    public async Task Resilience_LargeTestWithManyQuestions_RetrievesWithinTimeBudget()
    {
        // Arrange: build a test with 25 questions
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"LargeTest_{Guid.NewGuid().ToString("N")[..8]}");

        const int questionCount = 25;
        for (int i = 1; i <= questionCount; i++)
            await TestDataHelper.AddQuestionAsync(_apiClient, test.Slug, $"Question {i}");

        // Act: time a single GET of the full test detail
        var sw = Stopwatch.StartNew();
        var response = await _apiClient.GetAsync<TestResponse>($"tests/{test.Slug}/");
        sw.Stop();

        // Assert
        response.Should().NotBeNull("large test must be retrievable");
        response!.Questions!.Count.Should().Be(questionCount,
            $"all {questionCount} questions must be returned in the response");
        sw.ElapsedMilliseconds.Should().BeLessThan(10_000,
            $"retrieving a test with {questionCount} questions took {sw.ElapsedMilliseconds}ms, " +
            $"which exceeds the 10-second budget");
    }

    public void Dispose()
    {
        foreach (var client in _clients)
            client.Dispose();
        _apiClient?.Dispose();
    }
}
