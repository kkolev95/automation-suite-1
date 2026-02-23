using System.Net;
using FluentAssertions;
using TestIT.ApiTests.Helpers;
using TestIT.ApiTests.Models;
using Xunit;

namespace TestIT.ApiTests.Tests;

public class TestTakingTests : IDisposable
{
    private readonly ApiClient _apiClient;  // author / authenticated
    private readonly ApiClient _anonClient; // anonymous / unauthenticated

    public TestTakingTests(ITestOutputHelper output)
    {
        ApiClient.SetOutput(output.WriteLine);
        var baseUrl = TestConfiguration.GetBaseUrl();
        _apiClient = new ApiClient(baseUrl);
        _anonClient = new ApiClient(baseUrl);
    }

    /// <summary>
    /// Creates an authenticated test with 2 MC questions.
    /// Returns enough IDs to submit both correct and incorrect answers.
    /// </summary>
    private async Task<(TestResponse test, int correctId1, int wrongId1, int correctId2, int wrongId2)> CreateScorableTestAsync()
    {
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);

        var test = await TestDataHelper.CreateTestAsync(
            _apiClient,
            $"Scorable_{Guid.NewGuid().ToString("N")[..8]}");

        var q1 = await TestDataHelper.AddQuestionAsync(_apiClient, test.Slug, "What is 1+1?",
            answers: new List<CreateAnswerRequest>
            {
                new() { AnswerText = "2", IsCorrect = true, Order = 1 },
                new() { AnswerText = "3", IsCorrect = false, Order = 2 }
            });

        var q2 = await TestDataHelper.AddQuestionAsync(_apiClient, test.Slug, "Capital of France?",
            answers: new List<CreateAnswerRequest>
            {
                new() { AnswerText = "London", IsCorrect = false, Order = 1 },
                new() { AnswerText = "Paris", IsCorrect = true, Order = 2 }
            });

        return (test,
            q1.Answers.First(a => a.IsCorrect).Id,
            q1.Answers.First(a => !a.IsCorrect).Id,
            q2.Answers.First(a => a.IsCorrect).Id,
            q2.Answers.First(a => !a.IsCorrect).Id);
    }

    [Fact]
    [Trait("Category", "Smoke")]
    [Trait("Priority", "P0")]
    public async Task TestAccess_PublicTest_AllowsAnonymousAccess()
    {
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);

        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"PublicTake_{Guid.NewGuid().ToString("N")[..8]}", visibility: "public");
        await TestDataHelper.AddQuestionAsync(_apiClient, test.Slug, "Sample question");

        // Anonymous client fetches the test
        var response = await _anonClient.GetAsync($"tests/{test.Slug}/take/");
        var body = await _anonClient.GetResponseBodyAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {body}");

        var takeTest = await _anonClient.DeserializeResponseAsync<TakeTestResponse>(response);
        takeTest.Should().NotBeNull();
        takeTest!.Title.Should().StartWith("PublicTake_");
        takeTest.Questions.Should().HaveCount(1);
    }

    [Fact]
    public async Task TestAccess_PasswordProtected_BlocksWithoutPassword()
    {
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);

        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"PwProtected_{Guid.NewGuid().ToString("N")[..8]}",
            visibility: "password_protected",
            password: "secret123");

        var response = await _anonClient.GetAsync($"tests/{test.Slug}/take/");
        var body = await _anonClient.GetResponseBodyAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, $"Response: {body}");

        var pwResponse = await _anonClient.DeserializeResponseAsync<PasswordRequiredResponse>(response);
        pwResponse.Should().NotBeNull();
        pwResponse!.RequiresPassword.Should().BeTrue();
    }

    [Fact]
    public async Task PasswordVerification_WithCorrectPassword_GrantsAccess()
    {
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);

        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"VerifyPW_{Guid.NewGuid().ToString("N")[..8]}",
            visibility: "password_protected",
            password: "secret123");

        var response = await _anonClient.PostAsync($"tests/{test.Slug}/verify-password/",
            new VerifyPasswordRequest { Password = "secret123" });
        var body = await _anonClient.GetResponseBodyAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {body}");

        var verified = await _anonClient.DeserializeResponseAsync<VerifyPasswordResponse>(response);
        verified.Should().NotBeNull();
        verified!.Verified.Should().BeTrue();
    }

    [Fact]
    public async Task PasswordVerification_WithWrongPassword_DeniesAccess()
    {
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);

        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"WrongPW_{Guid.NewGuid().ToString("N")[..8]}",
            visibility: "password_protected",
            password: "secret123");

        var response = await _anonClient.PostAsync($"tests/{test.Slug}/verify-password/",
            new VerifyPasswordRequest { Password = "wrongpassword" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "because wrong password should be rejected");
    }

    [Fact]
    [Trait("Category", "Smoke")]
    [Trait("Priority", "P0")]
    public async Task AttemptStart_AsAnonymousUser_CreatesAttemptSuccessfully()
    {
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);

        var test = await TestDataHelper.CreateTestAsync(_apiClient, $"AnonStart_{Guid.NewGuid().ToString("N")[..8]}");
        await TestDataHelper.AddQuestionAsync(_apiClient, test.Slug, "Q1");

        var response = await _anonClient.PostAsync($"tests/{test.Slug}/attempts/",
            new StartAttemptRequest { AnonymousName = "Anonymous Tester" });
        var body = await _anonClient.GetResponseBodyAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.Created, $"Response: {body}");

        var attempt = await _anonClient.DeserializeResponseAsync<AttemptResponse>(response);
        attempt.Should().NotBeNull();
        attempt!.Id.Should().BePositive();
    }

    [Fact]
    [Trait("Category", "Smoke")]
    [Trait("Priority", "P0")]
    public async Task DraftSave_WithValidAnswers_SavesDraftSuccessfully()
    {
        var (test, correctId1, _, _, _) = await CreateScorableTestAsync();

        // Get question IDs from the take endpoint
        var takeTest = await _anonClient.GetAsync<TakeTestResponse>($"tests/{test.Slug}/take/");
        takeTest.Should().NotBeNull();

        // Start attempt
        var startResponse = await _anonClient.PostAsync($"tests/{test.Slug}/attempts/",
            new StartAttemptRequest { AnonymousName = "Draft Tester" });
        var attempt = await _anonClient.DeserializeResponseAsync<AttemptResponse>(startResponse);

        // Save draft with one answer
        var draftRequest = new SaveDraftRequest
        {
            DraftAnswers = new Dictionary<string, List<int>>
            {
                { takeTest!.Questions[0].Id.ToString(), new List<int> { correctId1 } }
            }
        };

        var response = await _anonClient.PutAsync($"tests/{test.Slug}/attempts/{attempt!.Id}/", draftRequest);
        var body = await _anonClient.GetResponseBodyAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {body}");
    }

    [Fact]
    [Trait("Category", "Smoke")]
    [Trait("Priority", "P0")]
    public async Task AttemptSubmission_AllCorrectAnswers_ScoresFullMarks()
    {
        var (test, correctId1, _, correctId2, _) = await CreateScorableTestAsync();

        var takeTest = await _anonClient.GetAsync<TakeTestResponse>($"tests/{test.Slug}/take/");

        // Start attempt
        var startResponse = await _anonClient.PostAsync($"tests/{test.Slug}/attempts/",
            new StartAttemptRequest { AnonymousName = "Perfect Scorer" });
        var attempt = await _anonClient.DeserializeResponseAsync<AttemptResponse>(startResponse);

        // Save draft with correct answers first (scoring is based on saved draft)
        var draftRequest = new SaveDraftRequest
        {
            DraftAnswers = new Dictionary<string, List<int>>
            {
                { takeTest!.Questions[0].Id.ToString(), new List<int> { correctId1 } },
                { takeTest.Questions[1].Id.ToString(), new List<int> { correctId2 } }
            }
        };
        await _anonClient.PutAsync($"tests/{test.Slug}/attempts/{attempt!.Id}/", draftRequest);

        // Submit the attempt
        var submitResponse = await _anonClient.PostAsync($"tests/{test.Slug}/attempts/{attempt.Id}/submit/",
            new Dictionary<string, object>());
        submitResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Score is in the results endpoint (only accessible by author)
        var resultsResponse = await _apiClient.GetAsync($"tests/{test.Slug}/results/");
        var body = await _apiClient.GetResponseBodyAsync(resultsResponse);
        resultsResponse.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {body}");

        var results = await _apiClient.DeserializeResponseAsync<List<ResultResponse>>(resultsResponse);
        results.Should().NotBeNull();

        var myResult = results!.First(r => r.AnonymousName == "Perfect Scorer");
        myResult.Score!.Value.Should().Be(100.0);
        myResult.CorrectAnswers.Should().Be(2);
        myResult.TotalQuestions.Should().Be(2);
    }

    [Fact]
    public async Task AttemptSubmission_AllWrongAnswers_ScoresZeroMarks()
    {
        var (test, _, wrongId1, _, wrongId2) = await CreateScorableTestAsync();

        var takeTest = await _anonClient.GetAsync<TakeTestResponse>($"tests/{test.Slug}/take/");

        var startResponse = await _anonClient.PostAsync($"tests/{test.Slug}/attempts/",
            new StartAttemptRequest { AnonymousName = "Zero Scorer" });
        var attempt = await _anonClient.DeserializeResponseAsync<AttemptResponse>(startResponse);

        // Save draft with wrong answers
        var draftRequest = new SaveDraftRequest
        {
            DraftAnswers = new Dictionary<string, List<int>>
            {
                { takeTest!.Questions[0].Id.ToString(), new List<int> { wrongId1 } },
                { takeTest.Questions[1].Id.ToString(), new List<int> { wrongId2 } }
            }
        };
        await _anonClient.PutAsync($"tests/{test.Slug}/attempts/{attempt!.Id}/", draftRequest);

        // Submit
        var submitResponse = await _anonClient.PostAsync($"tests/{test.Slug}/attempts/{attempt.Id}/submit/",
            new Dictionary<string, object>());
        submitResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Check score via results
        var resultsResponse = await _apiClient.GetAsync($"tests/{test.Slug}/results/");
        var results = await _apiClient.DeserializeResponseAsync<List<ResultResponse>>(resultsResponse);

        var myResult = results!.First(r => r.AnonymousName == "Zero Scorer");
        myResult.Score!.Value.Should().Be(0.0);
        myResult.CorrectAnswers.Should().Be(0);
    }

    [Fact]
    public async Task AttemptSubmission_AlreadySubmitted_PreventsDoubleSubmission()
    {
        var (test, correctId1, _, _, _) = await CreateScorableTestAsync();

        var takeTest = await _anonClient.GetAsync<TakeTestResponse>($"tests/{test.Slug}/take/");

        var startResponse = await _anonClient.PostAsync($"tests/{test.Slug}/attempts/",
            new StartAttemptRequest { AnonymousName = "Double Submitter" });
        var attempt = await _anonClient.DeserializeResponseAsync<AttemptResponse>(startResponse);

        // Save draft and submit once
        await _anonClient.PutAsync($"tests/{test.Slug}/attempts/{attempt!.Id}/", new SaveDraftRequest
        {
            DraftAnswers = new Dictionary<string, List<int>>
            {
                { takeTest!.Questions[0].Id.ToString(), new List<int> { correctId1 } }
            }
        });
        await _anonClient.PostAsync($"tests/{test.Slug}/attempts/{attempt.Id}/submit/",
            new Dictionary<string, object>());

        // Second submit should fail
        var response = await _anonClient.PostAsync($"tests/{test.Slug}/attempts/{attempt.Id}/submit/",
            new Dictionary<string, object>());

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "because attempt has already been submitted");
    }

    [Fact]
    public async Task ResultsRetrieval_AsAuthor_ReturnsAllSubmissions()
    {
        var (test, correctId1, _, correctId2, _) = await CreateScorableTestAsync();

        var takeTest = await _anonClient.GetAsync<TakeTestResponse>($"tests/{test.Slug}/take/");

        // Start, save draft, and submit an attempt
        var startResponse = await _anonClient.PostAsync($"tests/{test.Slug}/attempts/",
            new StartAttemptRequest { AnonymousName = "Results Tester" });
        var attempt = await _anonClient.DeserializeResponseAsync<AttemptResponse>(startResponse);

        await _anonClient.PutAsync($"tests/{test.Slug}/attempts/{attempt!.Id}/", new SaveDraftRequest
        {
            DraftAnswers = new Dictionary<string, List<int>>
            {
                { takeTest!.Questions[0].Id.ToString(), new List<int> { correctId1 } },
                { takeTest.Questions[1].Id.ToString(), new List<int> { correctId2 } }
            }
        });
        await _anonClient.PostAsync($"tests/{test.Slug}/attempts/{attempt.Id}/submit/",
            new Dictionary<string, object>());

        // Author retrieves results
        var response = await _apiClient.GetAsync($"tests/{test.Slug}/results/");
        var body = await _apiClient.GetResponseBodyAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {body}");

        var results = await _apiClient.DeserializeResponseAsync<List<ResultResponse>>(response);
        results.Should().NotBeNull();
        results.Should().NotBeEmpty();
        results!.Should().Contain(r => r.AnonymousName == "Results Tester");
    }

    [Fact]
    public async Task MaxAttempts_OnceExhausted_BlocksFurtherAttempts()
    {
        // Arrange: create test allowing exactly 2 attempts
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);

        var test = await TestDataHelper.CreateTestAsync(
            _apiClient,
            $"MaxAttempts_{Guid.NewGuid().ToString("N")[..8]}",
            maxAttempts: 2);
        await TestDataHelper.AddQuestionAsync(_apiClient, test.Slug, "Q1");

        // Use up all allowed attempts
        for (int i = 1; i <= 2; i++)
        {
            var startResp = await _anonClient.PostAsync($"tests/{test.Slug}/attempts/",
                new StartAttemptRequest { AnonymousName = $"Taker {i}" });
            startResp.StatusCode.Should().Be(HttpStatusCode.Created,
                $"attempt {i} should succeed because it is within the limit");

            var attempt = await _anonClient.DeserializeResponseAsync<AttemptResponse>(startResp);
            await _anonClient.PostAsync($"tests/{test.Slug}/attempts/{attempt!.Id}/submit/",
                new Dictionary<string, object>());
        }

        // Act: third attempt should be rejected
        var response = await _anonClient.PostAsync($"tests/{test.Slug}/attempts/",
            new StartAttemptRequest { AnonymousName = "Taker 3" });
        var body = await _anonClient.GetResponseBodyAsync(response);

        // Assert: 400 Bad Request or 403 Forbidden — both are valid enforcement responses
        response.StatusCode.Should().BeOneOf(
            new[] { HttpStatusCode.BadRequest, HttpStatusCode.Forbidden },
            $"because the max attempt limit of 2 has been reached. Response: {body}");
    }

    [Fact]
    public async Task ShowAnswersAfter_WhenFalse_DoesNotRevealCorrectAnswers()
    {
        // Arrange: create test with ShowAnswersAfter = false
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);

        var test = await TestDataHelper.CreateTestAsync(
            _apiClient,
            $"HideAnswers_{Guid.NewGuid().ToString("N")[..8]}",
            showAnswersAfter: false);

        var q = await TestDataHelper.AddQuestionAsync(_apiClient, test.Slug, "What is 2+2?",
            answers: new List<CreateAnswerRequest>
            {
                new() { AnswerText = "4", IsCorrect = true,  Order = 1 },
                new() { AnswerText = "5", IsCorrect = false, Order = 2 }
            });

        var startResponse = await _anonClient.PostAsync($"tests/{test.Slug}/attempts/",
            new StartAttemptRequest { AnonymousName = "Answer Checker" });
        var attempt = await _anonClient.DeserializeResponseAsync<AttemptResponse>(startResponse);

        var takeTest = await _anonClient.GetAsync<TakeTestResponse>($"tests/{test.Slug}/take/");
        await _anonClient.PutAsync($"tests/{test.Slug}/attempts/{attempt!.Id}/",
            new SaveDraftRequest
            {
                DraftAnswers = new Dictionary<string, List<int>>
                {
                    { takeTest!.Questions[0].Id.ToString(), new List<int> { q.Answers.First(a => a.IsCorrect).Id } }
                }
            });
        await _anonClient.PostAsync($"tests/{test.Slug}/attempts/{attempt.Id}/submit/",
            new Dictionary<string, object>());

        // Act: re-fetch the take endpoint after submission
        var takeAfter = await _anonClient.GetAsync<TakeTestResponse>($"tests/{test.Slug}/take/");

        // Assert: no answer should reveal is_correct = true
        takeAfter.Should().NotBeNull();
        takeAfter!.Questions.Should().NotBeEmpty();
        foreach (var question in takeAfter.Questions)
        {
            question.Answers.Should().AllSatisfy(a =>
                a.IsCorrect.Should().BeFalse(
                    "because ShowAnswersAfter=false must not expose correct answers on the take endpoint"));
        }
    }

    [Fact]
    public async Task ShowAnswersAfter_WhenTrue_ScoreIsRecordedAndTakeEndpointRemainsAccessible()
    {
        // Arrange: create test with ShowAnswersAfter = true
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);

        var test = await TestDataHelper.CreateTestAsync(
            _apiClient,
            $"ShowAnswers_{Guid.NewGuid().ToString("N")[..8]}",
            showAnswersAfter: true);

        var q = await TestDataHelper.AddQuestionAsync(_apiClient, test.Slug, "Capital of Italy?",
            answers: new List<CreateAnswerRequest>
            {
                new() { AnswerText = "Rome",  IsCorrect = true,  Order = 1 },
                new() { AnswerText = "Milan", IsCorrect = false, Order = 2 }
            });

        var startResponse = await _anonClient.PostAsync($"tests/{test.Slug}/attempts/",
            new StartAttemptRequest { AnonymousName = "Reveal Checker" });
        var attempt = await _anonClient.DeserializeResponseAsync<AttemptResponse>(startResponse);

        var takeTest = await _anonClient.GetAsync<TakeTestResponse>($"tests/{test.Slug}/take/");
        await _anonClient.PutAsync($"tests/{test.Slug}/attempts/{attempt!.Id}/",
            new SaveDraftRequest
            {
                DraftAnswers = new Dictionary<string, List<int>>
                {
                    { takeTest!.Questions[0].Id.ToString(), new List<int> { q.Answers.First(a => a.IsCorrect).Id } }
                }
            });
        await _anonClient.PostAsync($"tests/{test.Slug}/attempts/{attempt.Id}/submit/",
            new Dictionary<string, object>());

        // Act: author retrieves results
        var resultsResponse = await _apiClient.GetAsync($"tests/{test.Slug}/results/");
        var body = await _apiClient.GetResponseBodyAsync(resultsResponse);
        resultsResponse.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {body}");

        var results = await _apiClient.DeserializeResponseAsync<List<ResultResponse>>(resultsResponse);
        results.Should().NotBeNull();

        // Assert: correct answer was recorded and scored — ShowAnswersAfter must not suppress scoring
        var myResult = results!.First(r => r.AnonymousName == "Reveal Checker");
        myResult.Score.Should().NotBeNull("a score must be present after submission");
        myResult.Score!.Value.Should().Be(100.0,
            "because the correct answer was submitted");

        // Take endpoint must remain accessible after submission (not gated by ShowAnswersAfter)
        var takeAfter = await _anonClient.GetAsync<TakeTestResponse>($"tests/{test.Slug}/take/");
        takeAfter.Should().NotBeNull("take endpoint must remain available after submission");
    }

    public void Dispose()
    {
        _apiClient?.Dispose();
        _anonClient?.Dispose();
    }
}
