using System.Net;
using FluentAssertions;
using TestIT.ApiTests.Helpers;
using TestIT.ApiTests.Models;
using Xunit;
using Xunit.Abstractions;

namespace TestIT.ApiTests.Tests;

/// <summary>
/// Verifies the max_attempts lifecycle when the limit is greater than one:
///   - After exhausting attempt 1, a second attempt can be started (max=2)
///   - After exhausting attempt 2, a third attempt is blocked with 403 (max=2)
///   - Each attempt is scored independently
///   - Submitting with zero answers scores 0%
///   - Submitting with only some questions answered scores proportionally
/// </summary>
public class MultipleAttemptsTests : IDisposable
{
    private readonly ApiClient _apiClient;   // test author
    private readonly ApiClient _anonClient;  // anonymous taker — same instance to share cookies

    public MultipleAttemptsTests(ITestOutputHelper output)
    {
        ApiClient.SetOutput(output.WriteLine);
        var baseUrl = TestConfiguration.GetBaseUrl();
        _apiClient  = new ApiClient(baseUrl);
        _anonClient = new ApiClient(baseUrl);
    }

    /// <summary>
    /// Creates a scorable test (max_attempts=2, 2 MC questions) and returns it along with
    /// the correct answer ID for Q1 and wrong answer ID for Q1.
    /// </summary>
    private async Task<(TestResponse test, int correctId, int wrongId)> CreateTwoAttemptTestAsync()
    {
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"MultiAtt_{Guid.NewGuid().ToString("N")[..8]}",
            maxAttempts: 2);

        var q = await TestDataHelper.AddQuestionAsync(_apiClient, test.Slug, "Q for multi-attempt",
            answers: new List<CreateAnswerRequest>
            {
                new() { AnswerText = "Correct",   IsCorrect = true,  Order = 1 },
                new() { AnswerText = "Incorrect",  IsCorrect = false, Order = 2 },
            });

        int correct = q.Answers.First(a => a.IsCorrect).Id;
        int wrong   = q.Answers.First(a => !a.IsCorrect).Id;
        return (test, correct, wrong);
    }

    private async Task<AttemptResponse> StartAttemptAsync(TestResponse test, string name = "Taker")
    {
        var startResp = await _anonClient.PostAsync($"tests/{test.Slug}/attempts/",
            new StartAttemptRequest { AnonymousName = name });
        startResp.StatusCode.Should().Be(HttpStatusCode.Created,
            $"starting an attempt must succeed. Status: {startResp.StatusCode}");
        return (await _anonClient.DeserializeResponseAsync<AttemptResponse>(startResp))!;
    }

    private async Task SubmitAsync(TestResponse test, AttemptResponse attempt,
        int? answerId = null, int? questionId = null)
    {
        object body = answerId.HasValue && questionId.HasValue
            ? new SaveDraftRequest
              {
                  DraftAnswers = new Dictionary<string, List<int>>
                  {
                      { questionId.Value.ToString(), new List<int> { answerId.Value } }
                  }
              }
            : new Dictionary<string, object>();

        await _anonClient.PostAsync($"tests/{test.Slug}/attempts/{attempt.Id}/submit/", body);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // max_attempts = 2 lifecycle
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    [Trait("Category", "Functional")]
    [Trait("Priority", "P1")]
    public async Task MaxAttempts_WhenTwo_SecondAttemptIsAllowedAfterFirstSubmit()
    {
        // Arrange
        var (test, correct, _) = await CreateTwoAttemptTestAsync();
        var q = (await _apiClient.GetAsync<TestResponse>($"tests/{test.Slug}/"))!
                    .Questions![0];

        // Submit attempt 1
        var attempt1 = await StartAttemptAsync(test, "MultiTaker");
        await SubmitAsync(test, attempt1, correct, q.Id);

        // Act: start attempt 2 — must be allowed (only 1 of 2 used so far)
        var startResp2 = await _anonClient.PostAsync($"tests/{test.Slug}/attempts/",
            new StartAttemptRequest { AnonymousName = "MultiTaker" });

        // Assert
        startResp2.StatusCode.Should().Be(HttpStatusCode.Created,
            "when max_attempts=2 a second attempt must be allowed after the first is submitted");
    }

    [Fact]
    [Trait("Category", "Functional")]
    [Trait("Priority", "P1")]
    public async Task MaxAttempts_WhenTwo_ThirdAttemptIsBlocked()
    {
        // Arrange
        var (test, correct, _) = await CreateTwoAttemptTestAsync();
        var q = (await _apiClient.GetAsync<TestResponse>($"tests/{test.Slug}/"))!
                    .Questions![0];

        // Submit both allowed attempts
        var attempt1 = await StartAttemptAsync(test, "BlockTaker");
        await SubmitAsync(test, attempt1, correct, q.Id);

        var attempt2 = await StartAttemptAsync(test, "BlockTaker");
        await SubmitAsync(test, attempt2, correct, q.Id);

        // Act: attempt 3 — must be blocked
        var startResp3 = await _anonClient.PostAsync($"tests/{test.Slug}/attempts/",
            new StartAttemptRequest { AnonymousName = "BlockTaker" });

        // Assert
        startResp3.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "when max_attempts=2 a third attempt must be blocked with 403");
    }

    [Fact]
    [Trait("Category", "Functional")]
    [Trait("Priority", "P1")]
    public async Task MaxAttempts_EachAttemptScoredIndependently()
    {
        // Arrange: use a 2-question test with max_attempts=3 so we can do two differently-scored attempts
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"IndepScore_{Guid.NewGuid().ToString("N")[..8]}",
            maxAttempts: 3);

        var q1 = await TestDataHelper.AddQuestionAsync(_apiClient, test.Slug, "Q1",
            answers: new List<CreateAnswerRequest>
            {
                new() { AnswerText = "Right", IsCorrect = true,  Order = 1 },
                new() { AnswerText = "Wrong", IsCorrect = false, Order = 2 },
            });
        var q2 = await TestDataHelper.AddQuestionAsync(_apiClient, test.Slug, "Q2",
            answers: new List<CreateAnswerRequest>
            {
                new() { AnswerText = "Right", IsCorrect = true,  Order = 1 },
                new() { AnswerText = "Wrong", IsCorrect = false, Order = 2 },
            });

        int q1right = q1.Answers.First(a => a.IsCorrect).Id;
        int q2right = q2.Answers.First(a => a.IsCorrect).Id;
        int q1wrong = q1.Answers.First(a => !a.IsCorrect).Id;

        // Attempt 1: get both right → 100%
        var att1 = await StartAttemptAsync(test, "IndepTaker");
        await _anonClient.PostAsync($"tests/{test.Slug}/attempts/{att1.Id}/submit/",
            new SaveDraftRequest
            {
                DraftAnswers = new Dictionary<string, List<int>>
                {
                    { q1.Id.ToString(), new List<int> { q1right } },
                    { q2.Id.ToString(), new List<int> { q2right } },
                }
            });

        // Attempt 2: get both wrong → 0%
        var att2 = await StartAttemptAsync(test, "IndepTaker");
        await _anonClient.PostAsync($"tests/{test.Slug}/attempts/{att2.Id}/submit/",
            new SaveDraftRequest
            {
                DraftAnswers = new Dictionary<string, List<int>>
                {
                    { q1.Id.ToString(), new List<int> { q1wrong } },
                }
            });

        // Assert: result list must contain two entries with different scores
        var results = await _apiClient.GetAsync<List<ResultResponse>>($"tests/{test.Slug}/results/");
        var takerResults = results!.Where(r => r.AnonymousName == "IndepTaker").ToList();
        takerResults.Should().HaveCount(2, "two attempts were submitted by the same taker");

        var scores = takerResults.Select(r => r.Score).OrderBy(s => s).ToList();
        scores[0].Should().Be(0.0,  "the wrong-answers attempt must score 0%");
        scores[1].Should().Be(100.0, "the all-correct attempt must score 100%");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Empty and partial submissions
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    [Trait("Category", "Functional")]
    [Trait("Priority", "P1")]
    public async Task EmptySubmission_WithNoAnswers_ScoresZeroPercent()
    {
        // Arrange: 2-question test
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"EmptySub_{Guid.NewGuid().ToString("N")[..8]}");
        await TestDataHelper.AddQuestionAsync(_apiClient, test.Slug, "Q1");
        await TestDataHelper.AddQuestionAsync(_apiClient, test.Slug, "Q2");

        // Act: submit with an empty answer dict
        var attempt = await StartAttemptAsync(test, "EmptyTaker");
        var submitResp = await _anonClient.PostAsync(
            $"tests/{test.Slug}/attempts/{attempt.Id}/submit/",
            new Dictionary<string, object>());
        submitResp.StatusCode.Should().Be(HttpStatusCode.OK,
            "submitting with no answers must be accepted (zero score, not rejected)");

        // Assert
        var results = await _apiClient.GetAsync<List<ResultResponse>>($"tests/{test.Slug}/results/");
        var result = results!.First(r => r.AnonymousName == "EmptyTaker");
        result.Score.Should().Be(0.0,
            "submitting with no answers must result in a 0% score");
        result.CorrectAnswers.Should().Be(0,
            "zero answers means zero correct answers");
    }

    [Fact]
    [Trait("Category", "Functional")]
    [Trait("Priority", "P1")]
    public async Task PartialSubmission_AnsweringOnlyOneOfTwoQuestions_ScoresFiftyPercent()
    {
        // Arrange: 2-question test
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"PartialSub_{Guid.NewGuid().ToString("N")[..8]}");

        var q1 = await TestDataHelper.AddQuestionAsync(_apiClient, test.Slug, "Q1 — answer this",
            answers: new List<CreateAnswerRequest>
            {
                new() { AnswerText = "Right", IsCorrect = true,  Order = 1 },
                new() { AnswerText = "Wrong", IsCorrect = false, Order = 2 },
            });
        await TestDataHelper.AddQuestionAsync(_apiClient, test.Slug, "Q2 — skip this");

        int rightId = q1.Answers.First(a => a.IsCorrect).Id;

        // Act: answer Q1 correctly, leave Q2 unanswered
        var attempt = await StartAttemptAsync(test, "PartialTaker");
        await _anonClient.PostAsync($"tests/{test.Slug}/attempts/{attempt.Id}/submit/",
            new SaveDraftRequest
            {
                DraftAnswers = new Dictionary<string, List<int>>
                {
                    { q1.Id.ToString(), new List<int> { rightId } }
                }
            });

        // Assert: 1 correct out of 2 questions = 50%
        var results = await _apiClient.GetAsync<List<ResultResponse>>($"tests/{test.Slug}/results/");
        var result = results!.First(r => r.AnonymousName == "PartialTaker");
        result.Score.Should().Be(50.0,
            "answering 1 of 2 questions correctly must score 50%");
        result.CorrectAnswers.Should().Be(1);
        result.TotalQuestions.Should().Be(2);
    }

    [Fact]
    [Trait("Category", "Validation")]
    [Trait("Priority", "P2")]
    public async Task DoubleSubmit_SecondSubmitAfterSuccess_IsRejected()
    {
        // Arrange
        var (test, correct, _) = await CreateTwoAttemptTestAsync();
        var q = (await _apiClient.GetAsync<TestResponse>($"tests/{test.Slug}/"))!.Questions![0];

        var attempt = await StartAttemptAsync(test, "DoubleTaker");
        await SubmitAsync(test, attempt, correct, q.Id);

        // Act: submit the same attempt again
        var secondSubmit = await _anonClient.PostAsync(
            $"tests/{test.Slug}/attempts/{attempt.Id}/submit/",
            new Dictionary<string, object>());

        // Assert: already-submitted attempt must be rejected
        secondSubmit.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "attempting to submit an already-submitted attempt must return 400");
    }

    public void Dispose()
    {
        _apiClient?.Dispose();
        _anonClient?.Dispose();
    }
}
