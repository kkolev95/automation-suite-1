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

    [Fact]
    public async Task Analytics_AverageScore_ReflectsSubmissionScores()
    {
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"AnalyticsAvg_{Guid.NewGuid().ToString("N")[..8]}");
        var question = await TestDataHelper.AddQuestionAsync(_apiClient, test.Slug, "Average question",
            answers: new List<CreateAnswerRequest>
            {
                new() { AnswerText = "Correct", IsCorrect = true, Order = 1 },
                new() { AnswerText = "Wrong", IsCorrect = false, Order = 2 }
            });

        int correctId = question.Answers.First(a => a.IsCorrect).Id;
        var takeTest = await _anonClient.GetAsync<TakeTestResponse>($"tests/{test.Slug}/take/");

        // Submit 100% attempt
        var start1 = await _anonClient.PostAsync($"tests/{test.Slug}/attempts/",
            new StartAttemptRequest { AnonymousName = "AvgTester1" });
        var attempt1 = await _anonClient.DeserializeResponseAsync<AttemptResponse>(start1);
        await _anonClient.PutAsync($"tests/{test.Slug}/attempts/{attempt1!.Id}/", new SaveDraftRequest
        {
            DraftAnswers = new Dictionary<string, List<int>>
            {
                { takeTest!.Questions[0].Id.ToString(), new List<int> { correctId } }
            }
        });
        await _anonClient.PostAsync($"tests/{test.Slug}/attempts/{attempt1.Id}/submit/",
            new Dictionary<string, object>());

        // Submit 0% attempt using a fresh client (max_attempts is per client session)
        var client2 = new ApiClient(TestConfiguration.GetBaseUrl());
        var start2 = await client2.PostAsync($"tests/{test.Slug}/attempts/",
            new StartAttemptRequest { AnonymousName = "AvgTester2" });
        var attempt2 = await client2.DeserializeResponseAsync<AttemptResponse>(start2);
        await client2.PostAsync($"tests/{test.Slug}/attempts/{attempt2!.Id}/submit/",
            new Dictionary<string, object>());
        client2.Dispose();

        var response = await _apiClient.GetAsync($"analytics/tests/{test.Slug}/");
        var analytics = await _apiClient.DeserializeResponseAsync<AnalyticsResponse>(response);

        analytics!.AverageScore.Should().NotBeNull("because there are submitted attempts");
        analytics.AverageScore.Should().BeApproximately(50.0, 1.0,
            "because one 100% and one 0% attempt yields a 50% average");
    }

    [Fact]
    public async Task Analytics_CompletionRate_ReflectsAbandonedAttempts()
    {
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"AnalyticsCompletion_{Guid.NewGuid().ToString("N")[..8]}",
            maxAttempts: 1);
        await TestDataHelper.AddQuestionAsync(_apiClient, test.Slug, "Completion question");

        // Start but do NOT submit — abandoned attempt
        var start1 = await _anonClient.PostAsync($"tests/{test.Slug}/attempts/",
            new StartAttemptRequest { AnonymousName = "Abandoner" });
        start1.StatusCode.Should().Be(System.Net.HttpStatusCode.Created);

        // Start and submit
        var client2 = new ApiClient(TestConfiguration.GetBaseUrl());
        var start2 = await client2.PostAsync($"tests/{test.Slug}/attempts/",
            new StartAttemptRequest { AnonymousName = "Completer" });
        var attempt2 = await client2.DeserializeResponseAsync<AttemptResponse>(start2);
        await client2.PostAsync($"tests/{test.Slug}/attempts/{attempt2!.Id}/submit/",
            new Dictionary<string, object>());
        client2.Dispose();

        var response = await _apiClient.GetAsync($"analytics/tests/{test.Slug}/");
        var analytics = await _apiClient.DeserializeResponseAsync<AnalyticsResponse>(response);

        analytics!.CompletionRate.Should().NotBeNull("because there are started attempts");
        analytics.CompletionRate.Should().BeApproximately(50.0, 1.0,
            "because 1 of 2 started attempts was submitted");
    }

    [Fact]
    public async Task Analytics_QuestionStats_AnswerDistribution_ReflectsSubmissions()
    {
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"AnalyticsDist_{Guid.NewGuid().ToString("N")[..8]}");
        var question = await TestDataHelper.AddQuestionAsync(_apiClient, test.Slug, "Distribution question",
            answers: new List<CreateAnswerRequest>
            {
                new() { AnswerText = "Right", IsCorrect = true, Order = 1 },
                new() { AnswerText = "Wrong", IsCorrect = false, Order = 2 }
            });

        int correctId = question.Answers.First(a => a.IsCorrect).Id;
        int wrongId = question.Answers.First(a => !a.IsCorrect).Id;
        var takeTest = await _anonClient.GetAsync<TakeTestResponse>($"tests/{test.Slug}/take/");
        int questionId = takeTest!.Questions[0].Id;

        // Tester 1 picks correct answer
        var client1 = new ApiClient(TestConfiguration.GetBaseUrl());
        var s1 = await client1.PostAsync($"tests/{test.Slug}/attempts/",
            new StartAttemptRequest { AnonymousName = "DistTester1" });
        var a1 = await client1.DeserializeResponseAsync<AttemptResponse>(s1);
        await client1.PutAsync($"tests/{test.Slug}/attempts/{a1!.Id}/", new SaveDraftRequest
        {
            DraftAnswers = new Dictionary<string, List<int>> { { questionId.ToString(), new List<int> { correctId } } }
        });
        await client1.PostAsync($"tests/{test.Slug}/attempts/{a1.Id}/submit/", new Dictionary<string, object>());
        client1.Dispose();

        // Tester 2 picks wrong answer
        var client2 = new ApiClient(TestConfiguration.GetBaseUrl());
        var s2 = await client2.PostAsync($"tests/{test.Slug}/attempts/",
            new StartAttemptRequest { AnonymousName = "DistTester2" });
        var a2 = await client2.DeserializeResponseAsync<AttemptResponse>(s2);
        await client2.PutAsync($"tests/{test.Slug}/attempts/{a2!.Id}/", new SaveDraftRequest
        {
            DraftAnswers = new Dictionary<string, List<int>> { { questionId.ToString(), new List<int> { wrongId } } }
        });
        await client2.PostAsync($"tests/{test.Slug}/attempts/{a2.Id}/submit/", new Dictionary<string, object>());
        client2.Dispose();

        var response = await _apiClient.GetAsync($"analytics/tests/{test.Slug}/");
        var analytics = await _apiClient.DeserializeResponseAsync<AnalyticsResponse>(response);

        var qStats = analytics!.QuestionStats.Should().ContainSingle().Subject;
        qStats.TotalAnswered.Should().Be(2, "because two takers answered");
        qStats.CorrectCount.Should().Be(1, "because only one taker chose the correct answer");

        var dist = qStats.AnswerDistribution;
        dist.Should().HaveCount(2, "because the question has two answers");
        dist.First(a => a.IsCorrect).Count.Should().Be(1, "because one taker picked the correct answer");
        dist.First(a => !a.IsCorrect).Count.Should().Be(1, "because one taker picked the wrong answer");
    }

    [Fact]
    public async Task Analytics_QuestionStats_Difficulty_IsValidScore()
    {
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"AnalyticsDiff_{Guid.NewGuid().ToString("N")[..8]}");
        var question = await TestDataHelper.AddQuestionAsync(_apiClient, test.Slug, "Difficulty question",
            answers: new List<CreateAnswerRequest>
            {
                new() { AnswerText = "Correct", IsCorrect = true, Order = 1 },
                new() { AnswerText = "Wrong", IsCorrect = false, Order = 2 }
            });

        int correctId = question.Answers.First(a => a.IsCorrect).Id;
        var takeTest = await _anonClient.GetAsync<TakeTestResponse>($"tests/{test.Slug}/take/");

        // Everyone gets it right — should be easiest possible difficulty
        var start = await _anonClient.PostAsync($"tests/{test.Slug}/attempts/",
            new StartAttemptRequest { AnonymousName = "DiffTester" });
        var attempt = await _anonClient.DeserializeResponseAsync<AttemptResponse>(start);
        await _anonClient.PutAsync($"tests/{test.Slug}/attempts/{attempt!.Id}/", new SaveDraftRequest
        {
            DraftAnswers = new Dictionary<string, List<int>>
            {
                { takeTest!.Questions[0].Id.ToString(), new List<int> { correctId } }
            }
        });
        await _anonClient.PostAsync($"tests/{test.Slug}/attempts/{attempt.Id}/submit/",
            new Dictionary<string, object>());

        var response = await _apiClient.GetAsync($"analytics/tests/{test.Slug}/");
        var analytics = await _apiClient.DeserializeResponseAsync<AnalyticsResponse>(response);

        var qStats = analytics!.QuestionStats.Should().ContainSingle().Subject;
        qStats.Difficulty.Should().BeInRange(0.0, 1.0,
            "because difficulty must be a normalised score between 0 and 1");
    }

    [Fact]
    public async Task Analytics_PassRate_IsNullWithNoSubmissions_AndValidAfter()
    {
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"AnalyticsPass_{Guid.NewGuid().ToString("N")[..8]}");
        await TestDataHelper.AddQuestionAsync(_apiClient, test.Slug, "Pass question");

        // Before any submissions — pass_rate should be null
        var emptyResponse = await _apiClient.GetAsync($"analytics/tests/{test.Slug}/");
        var emptyAnalytics = await _apiClient.DeserializeResponseAsync<AnalyticsResponse>(emptyResponse);
        emptyAnalytics!.PassRate.Should().BeNull("because there are no submissions yet");

        // After a submission — pass_rate should be a valid percentage
        var start = await _anonClient.PostAsync($"tests/{test.Slug}/attempts/",
            new StartAttemptRequest { AnonymousName = "PassTester" });
        var attempt = await _anonClient.DeserializeResponseAsync<AttemptResponse>(start);
        await _anonClient.PostAsync($"tests/{test.Slug}/attempts/{attempt!.Id}/submit/",
            new Dictionary<string, object>());

        var response = await _apiClient.GetAsync($"analytics/tests/{test.Slug}/");
        var analytics = await _apiClient.DeserializeResponseAsync<AnalyticsResponse>(response);

        analytics!.PassRate.Should().NotBeNull("because there is at least one submission");
        analytics.PassRate.Should().BeInRange(0.0, 100.0,
            "because pass rate is a percentage");
    }

    public void Dispose()
    {
        _apiClient?.Dispose();
        _anonClient?.Dispose();
        _otherClient?.Dispose();
    }
}
