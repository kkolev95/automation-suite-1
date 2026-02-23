using System.Net;
using FluentAssertions;
using TestIT.ApiTests.Helpers;
using TestIT.ApiTests.Models;
using Xunit;

namespace TestIT.ApiTests.Tests;

public class AnalyticsTests : IDisposable
{
    private readonly ApiClient _apiClient;  // test author
    private readonly ApiClient _anonClient; // for submitting attempts
    private readonly ApiClient _otherClient; // a different authenticated user

    public AnalyticsTests(ITestOutputHelper output)
    {
        ApiClient.SetOutput(output.WriteLine);
        var baseUrl = TestConfiguration.GetBaseUrl();
        _apiClient = new ApiClient(baseUrl);
        _anonClient = new ApiClient(baseUrl);
        _otherClient = new ApiClient(baseUrl);
    }

    [Fact]
    public async Task Analytics_NoSubmissions_ReturnsZeroStats()
    {
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);

        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"AnalyticsEmpty_{Guid.NewGuid().ToString("N")[..8]}");
        await TestDataHelper.AddQuestionAsync(_apiClient, test.Slug, "Lonely question");

        var response = await _apiClient.GetAsync($"analytics/tests/{test.Slug}/");
        var body = await _apiClient.GetResponseBodyAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {body}");

        var analytics = await _apiClient.DeserializeResponseAsync<AnalyticsResponse>(response);
        analytics.Should().NotBeNull();
        analytics!.TotalAttempts.Should().Be(0);
    }

    [Fact]
    public async Task Analytics_AfterSubmission_ReflectsAttemptData()
    {
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);

        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"AnalyticsStats_{Guid.NewGuid().ToString("N")[..8]}");

        var question = await TestDataHelper.AddQuestionAsync(_apiClient, test.Slug, "Stats question",
            answers: new List<CreateAnswerRequest>
            {
                new() { AnswerText = "Right", IsCorrect = true, Order = 1 },
                new() { AnswerText = "Wrong", IsCorrect = false, Order = 2 }
            });

        int correctId = question.Answers.First(a => a.IsCorrect).Id;

        // Get question IDs from the public take endpoint
        var takeTest = await _anonClient.GetAsync<TakeTestResponse>($"tests/{test.Slug}/take/");

        // Start attempt, save draft, then submit (scoring uses saved draft)
        var startResponse = await _anonClient.PostAsync($"tests/{test.Slug}/attempts/",
            new StartAttemptRequest { AnonymousName = "Stats Tester" });
        var attempt = await _anonClient.DeserializeResponseAsync<AttemptResponse>(startResponse);

        await _anonClient.PutAsync($"tests/{test.Slug}/attempts/{attempt!.Id}/", new SaveDraftRequest
        {
            DraftAnswers = new Dictionary<string, List<int>>
            {
                { takeTest!.Questions[0].Id.ToString(), new List<int> { correctId } }
            }
        });
        await _anonClient.PostAsync($"tests/{test.Slug}/attempts/{attempt.Id}/submit/",
            new Dictionary<string, object>());

        // Fetch analytics as author
        var response = await _apiClient.GetAsync($"analytics/tests/{test.Slug}/");
        var body = await _apiClient.GetResponseBodyAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {body}");

        var analytics = await _apiClient.DeserializeResponseAsync<AnalyticsResponse>(response);
        analytics.Should().NotBeNull();
        analytics!.TotalAttempts.Should().Be(1);
        analytics.QuestionStats.Should().HaveCount(1);
    }

    [Fact]
    public async Task Analytics_WhenUnauthenticated_DeniesAccess()
    {
        // Create a test first so the slug is real
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"AnalyticsNoAuth_{Guid.NewGuid().ToString("N")[..8]}");

        // Clear auth and try to access
        _apiClient.ClearAuthToken();

        var response = await _apiClient.GetAsync($"analytics/tests/{test.Slug}/");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "because authentication is required");
    }

    [Fact]
    public async Task Analytics_AsNonAuthor_DeniesAccessToOtherUsersTest()
    {
        // Create test as author
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"AnalyticsAuth_{Guid.NewGuid().ToString("N")[..8]}");

        // Different user tries to access analytics
        await TestDataHelper.RegisterAndLoginAsync(_otherClient);

        var response = await _otherClient.GetAsync($"analytics/tests/{test.Slug}/");

        // API may return 403 (forbidden) or 404 (hide existence) — both are correct
        response.StatusCode.Should().BeOneOf(
            new[] { HttpStatusCode.Forbidden, HttpStatusCode.NotFound },
            "because only the test author can view analytics");
    }

    public void Dispose()
    {
        _apiClient?.Dispose();
        _anonClient?.Dispose();
        _otherClient?.Dispose();
    }
}
