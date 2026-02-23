using System.Diagnostics;
using System.Net;
using FluentAssertions;
using TestIT.ApiTests.Helpers;
using TestIT.ApiTests.Models;
using Xunit;

namespace TestIT.ApiTests.Tests;

/// <summary>
/// Response latency SLA tests. Each test measures a single HTTP request in isolation
/// (setup data is created before the stopwatch starts) and asserts it completes within
/// a defined ceiling. Failures here indicate performance regressions, not functional bugs.
/// </summary>
public class PerformanceTests : IDisposable
{
    private readonly ApiClient _apiClient;
    private readonly ApiClient _anonClient;

    // ── SLA ceilings (configurable via appsettings.json or environment variables) ────
    // Default values can be overridden via PerformanceSla:ReadMs, PerformanceSla:ListMs, etc.
    private readonly int ReadSlaMs   = TestConfiguration.GetSlaMs("Read", defaultMs: 1500);   // GET single resource / profile
    private readonly int ListSlaMs   = TestConfiguration.GetSlaMs("List", defaultMs: 2000);   // GET list endpoints
    private readonly int WriteSlaMs  = TestConfiguration.GetSlaMs("Write", defaultMs: 2500);  // POST / PUT that create or update data
    private readonly int HeavySlaMs  = TestConfiguration.GetSlaMs("Heavy", defaultMs: 4000);  // scoring, analytics aggregation

    public PerformanceTests(ITestOutputHelper output)
    {
        ApiClient.SetOutput(output.WriteLine);
        var baseUrl = TestConfiguration.GetBaseUrl();
        _apiClient = new ApiClient(baseUrl);
        _anonClient = new ApiClient(baseUrl);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Authentication
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    [Trait("Category", "Performance")]
    [Trait("Priority", "P2")]
    public async Task Latency_Login_RespondsFast()
    {
        // Arrange: create an account to login with
        var (email, password) = await TestDataHelper.RegisterUserAsync(_apiClient);

        var loginRequest = new LoginRequest { Email = email, Password = password };

        // Act: time the login request only
        var sw = Stopwatch.StartNew();
        var response = await _apiClient.PostAsync("auth/login/", loginRequest);
        sw.Stop();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "login must succeed for the latency measurement to be valid");
        sw.ElapsedMilliseconds.Should().BeLessThan(WriteSlaMs,
            $"POST /auth/login/ took {sw.ElapsedMilliseconds}ms, exceeding the {WriteSlaMs}ms SLA");
    }

    [Fact]
    [Trait("Category", "Performance")]
    [Trait("Priority", "P2")]
    public async Task Latency_GetProfile_RespondsFast()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);

        // Act
        var sw = Stopwatch.StartNew();
        var response = await _apiClient.GetAsync("auth/me/");
        sw.Stop();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        sw.ElapsedMilliseconds.Should().BeLessThan(ReadSlaMs,
            $"GET /auth/me/ took {sw.ElapsedMilliseconds}ms, exceeding the {ReadSlaMs}ms SLA");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Test management
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    [Trait("Category", "Performance")]
    [Trait("Priority", "P2")]
    public async Task Latency_CreateTest_RespondsFast()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var request = new CreateTestRequest
        {
            Title       = $"Perf_{Guid.NewGuid().ToString("N")[..8]}",
            Description = "Latency SLA test"
        };

        // Act
        var sw = Stopwatch.StartNew();
        var response = await _apiClient.PostAsync("tests/", request);
        sw.Stop();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        sw.ElapsedMilliseconds.Should().BeLessThan(WriteSlaMs,
            $"POST /tests/ took {sw.ElapsedMilliseconds}ms, exceeding the {WriteSlaMs}ms SLA");
    }

    [Fact]
    [Trait("Category", "Performance")]
    [Trait("Priority", "P2")]
    public async Task Latency_ListTests_RespondsFast()
    {
        // Arrange: ensure there is at least one test to list
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        await TestDataHelper.CreateTestAsync(_apiClient, $"PerfList_{Guid.NewGuid().ToString("N")[..8]}");

        // Act
        var sw = Stopwatch.StartNew();
        var response = await _apiClient.GetAsync("tests/");
        sw.Stop();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        sw.ElapsedMilliseconds.Should().BeLessThan(ListSlaMs,
            $"GET /tests/ took {sw.ElapsedMilliseconds}ms, exceeding the {ListSlaMs}ms SLA");
    }

    [Fact]
    [Trait("Category", "Performance")]
    [Trait("Priority", "P2")]
    public async Task Latency_GetTestDetail_RespondsFast()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"PerfDetail_{Guid.NewGuid().ToString("N")[..8]}");

        // Act
        var sw = Stopwatch.StartNew();
        var response = await _apiClient.GetAsync($"tests/{test.Slug}/");
        sw.Stop();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        sw.ElapsedMilliseconds.Should().BeLessThan(ReadSlaMs,
            $"GET /tests/{{slug}}/ took {sw.ElapsedMilliseconds}ms, exceeding the {ReadSlaMs}ms SLA");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Test taking
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    [Trait("Category", "Performance")]
    [Trait("Priority", "P2")]
    public async Task Latency_GetTakeEndpoint_RespondsFast()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"PerfTake_{Guid.NewGuid().ToString("N")[..8]}",
            visibility: "public");
        await TestDataHelper.AddQuestionAsync(_apiClient, test.Slug, "Latency question");

        // Act
        var sw = Stopwatch.StartNew();
        var response = await _anonClient.GetAsync($"tests/{test.Slug}/take/");
        sw.Stop();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        sw.ElapsedMilliseconds.Should().BeLessThan(ReadSlaMs,
            $"GET /tests/{{slug}}/take/ took {sw.ElapsedMilliseconds}ms, exceeding the {ReadSlaMs}ms SLA");
    }

    [Fact]
    [Trait("Category", "Performance")]
    [Trait("Priority", "P2")]
    public async Task Latency_StartAttempt_RespondsFast()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"PerfAttempt_{Guid.NewGuid().ToString("N")[..8]}");

        // Act
        var sw = Stopwatch.StartNew();
        var response = await _anonClient.PostAsync($"tests/{test.Slug}/attempts/",
            new StartAttemptRequest { AnonymousName = "Perf Taker" });
        sw.Stop();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        sw.ElapsedMilliseconds.Should().BeLessThan(WriteSlaMs,
            $"POST /tests/{{slug}}/attempts/ took {sw.ElapsedMilliseconds}ms, exceeding the {WriteSlaMs}ms SLA");
    }

    [Fact]
    [Trait("Category", "Performance")]
    [Trait("Priority", "P2")]
    public async Task Latency_SubmitAttempt_RespondsFast()
    {
        // Arrange: set up test, question, and start an attempt
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"PerfSubmit_{Guid.NewGuid().ToString("N")[..8]}");
        var question = await TestDataHelper.AddQuestionAsync(_apiClient, test.Slug, "Submit perf Q");

        var takeTest = await _anonClient.GetAsync<TakeTestResponse>($"tests/{test.Slug}/take/");

        var startResp = await _anonClient.PostAsync($"tests/{test.Slug}/attempts/",
            new StartAttemptRequest { AnonymousName = "Submit Perf" });
        var attempt = await _anonClient.DeserializeResponseAsync<AttemptResponse>(startResp);

        await _anonClient.PutAsync($"tests/{test.Slug}/attempts/{attempt!.Id}/",
            new SaveDraftRequest
            {
                DraftAnswers = new Dictionary<string, List<int>>
                {
                    {
                        takeTest!.Questions[0].Id.ToString(),
                        new List<int> { question.Answers.First(a => a.IsCorrect).Id }
                    }
                }
            });

        // Act: time the submit (scoring computation) only
        var sw = Stopwatch.StartNew();
        var response = await _anonClient.PostAsync(
            $"tests/{test.Slug}/attempts/{attempt.Id}/submit/",
            new Dictionary<string, object>());
        sw.Stop();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        sw.ElapsedMilliseconds.Should().BeLessThan(HeavySlaMs,
            $"POST /tests/{{slug}}/attempts/{{id}}/submit/ took {sw.ElapsedMilliseconds}ms, " +
            $"exceeding the {HeavySlaMs}ms SLA");
    }

    [Fact]
    [Trait("Category", "Performance")]
    [Trait("Priority", "P2")]
    public async Task Latency_GetResults_RespondsFast()
    {
        // Arrange: create test, submit an attempt, then time the results fetch
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"PerfResults_{Guid.NewGuid().ToString("N")[..8]}");
        await TestDataHelper.AddQuestionAsync(_apiClient, test.Slug, "Results perf Q");

        var startResp = await _anonClient.PostAsync($"tests/{test.Slug}/attempts/",
            new StartAttemptRequest { AnonymousName = "Results Perf" });
        var attempt = await _anonClient.DeserializeResponseAsync<AttemptResponse>(startResp);
        await _anonClient.PostAsync($"tests/{test.Slug}/attempts/{attempt!.Id}/submit/",
            new Dictionary<string, object>());

        // Act
        var sw = Stopwatch.StartNew();
        var response = await _apiClient.GetAsync($"tests/{test.Slug}/results/");
        sw.Stop();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        sw.ElapsedMilliseconds.Should().BeLessThan(ListSlaMs,
            $"GET /tests/{{slug}}/results/ took {sw.ElapsedMilliseconds}ms, exceeding the {ListSlaMs}ms SLA");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Analytics
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    [Trait("Category", "Performance")]
    [Trait("Priority", "P2")]
    public async Task Latency_GetAnalytics_RespondsFast()
    {
        // Arrange: create test with a submission so analytics has data to aggregate
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"PerfAnalytics_{Guid.NewGuid().ToString("N")[..8]}");
        await TestDataHelper.AddQuestionAsync(_apiClient, test.Slug, "Analytics perf Q");

        var startResp = await _anonClient.PostAsync($"tests/{test.Slug}/attempts/",
            new StartAttemptRequest { AnonymousName = "Analytics Perf" });
        var attempt = await _anonClient.DeserializeResponseAsync<AttemptResponse>(startResp);
        await _anonClient.PostAsync($"tests/{test.Slug}/attempts/{attempt!.Id}/submit/",
            new Dictionary<string, object>());

        // Act: time the analytics aggregation only
        var sw = Stopwatch.StartNew();
        var response = await _apiClient.GetAsync($"analytics/tests/{test.Slug}/");
        sw.Stop();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        sw.ElapsedMilliseconds.Should().BeLessThan(HeavySlaMs,
            $"GET /analytics/tests/{{slug}}/ took {sw.ElapsedMilliseconds}ms, " +
            $"exceeding the {HeavySlaMs}ms SLA");
    }

    public void Dispose()
    {
        _apiClient?.Dispose();
        _anonClient?.Dispose();
    }
}
