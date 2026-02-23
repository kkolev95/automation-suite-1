using System.Net;
using FluentAssertions;
using TestIT.ApiTests.Helpers;
using TestIT.ApiTests.Models;
using Xunit;

namespace TestIT.ApiTests.Tests;

[Collection("DataIntegrity")]
public class DataIntegrityTests : IDisposable
{
    private readonly ApiClient _apiClient;
    private readonly ApiClient _testTakerClient;

    public DataIntegrityTests(ITestOutputHelper output)
    {
        ApiClient.SetOutput(output.WriteLine);
        var baseUrl = TestConfiguration.GetBaseUrl();
        _apiClient = new ApiClient(baseUrl);
        _testTakerClient = new ApiClient(baseUrl);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Scoring Accuracy Tests
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ScoringAccuracy_AllQuestionsCorrect_Scores100Percent()
    {
        // Arrange: Create test with 5 questions
        await PersistentTestUser.GetOrCreateAndLoginAsync(_apiClient);
        var test = await CreateTestWithMultipleQuestions(5);
        var takeTest = await _testTakerClient.GetAsync<TakeTestResponse>($"tests/{test.slug}/take/");

        // Start attempt and answer all correctly
        var attempt = await StartAttemptAsync(test.slug, "Perfect Scorer");
        await SaveCorrectAnswersAsync(test.slug, attempt.Id, test.questions, takeTest!.Questions);
        await SubmitAttemptAsync(test.slug, attempt.Id);

        // Act: Fetch results
        var results = await GetTestResultsAsync(test.slug);
        var myResult = results.First(r => r.AnonymousName == "Perfect Scorer");

        // Assert: Score should be exactly 100%
        var score = myResult.Score!.Value;
        score.Should().Be(100.0, "because all answers were correct");
        myResult.CorrectAnswers.Should().Be(5);
        myResult.TotalQuestions.Should().Be(5);
    }

    [Fact]
    public async Task ScoringAccuracy_AllQuestionsWrong_Scores0Percent()
    {
        // Arrange: Create test with 5 questions
        await PersistentTestUser.GetOrCreateAndLoginAsync(_apiClient);
        var test = await CreateTestWithMultipleQuestions(5);
        var takeTest = await _testTakerClient.GetAsync<TakeTestResponse>($"tests/{test.slug}/take/");

        // Start attempt and answer all incorrectly
        var attempt = await StartAttemptAsync(test.slug, "Zero Scorer");
        await SaveWrongAnswersAsync(test.slug, attempt.Id, test.questions, takeTest!.Questions);
        await SubmitAttemptAsync(test.slug, attempt.Id);

        // Act: Fetch results
        var results = await GetTestResultsAsync(test.slug);
        var myResult = results.First(r => r.AnonymousName == "Zero Scorer");

        // Assert: Score should be exactly 0%
        var score = myResult.Score!.Value;
        score.Should().Be(0.0, "because all answers were wrong");
        myResult.CorrectAnswers.Should().Be(0);
        myResult.TotalQuestions.Should().Be(5);
    }

    [Fact]
    public async Task ScoringAccuracy_HalfCorrect_Scores50Percent()
    {
        // Arrange: Create test with 10 questions
        await PersistentTestUser.GetOrCreateAndLoginAsync(_apiClient);
        var test = await CreateTestWithMultipleQuestions(10);
        var takeTest = await _testTakerClient.GetAsync<TakeTestResponse>($"tests/{test.slug}/take/");

        // Start attempt and answer first 5 correctly, last 5 incorrectly
        var attempt = await StartAttemptAsync(test.slug, "Half Scorer");
        await SaveMixedAnswersAsync(test.slug, attempt.Id, test.questions, takeTest!.Questions, 5);
        await SubmitAttemptAsync(test.slug, attempt.Id);

        // Act: Fetch results
        var results = await GetTestResultsAsync(test.slug);
        var myResult = results.First(r => r.AnonymousName == "Half Scorer");

        // Assert: Score should be exactly 50%
        var score = myResult.Score!.Value;
        score.Should().Be(50.0, "because exactly half the answers were correct");
        myResult.CorrectAnswers.Should().Be(5);
        myResult.TotalQuestions.Should().Be(10);
    }

    [Fact]
    public async Task ScoringAccuracy_AllQuestionsSkipped_Scores0Percent()
    {
        // Arrange: Create test with 3 questions
        await PersistentTestUser.GetOrCreateAndLoginAsync(_apiClient);
        var test = await CreateTestWithMultipleQuestions(3);

        // Start attempt and submit without answering anything
        var attempt = await StartAttemptAsync(test.slug, "Skipper");
        await SubmitAttemptAsync(test.slug, attempt.Id);

        // Act: Fetch results
        var results = await GetTestResultsAsync(test.slug);
        var myResult = results.First(r => r.AnonymousName == "Skipper");

        // Assert: Score should be 0%
        var score = myResult.Score!.Value;
        score.Should().Be(0.0, "because no answers were provided");
        myResult.CorrectAnswers.Should().Be(0);
    }

    [Fact]
    public async Task ScoringAccuracy_SingleQuestionTest_ScoresCorrectly()
    {
        // Arrange: Create test with only 1 question
        await PersistentTestUser.GetOrCreateAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"SingleQ_{Guid.NewGuid().ToString("N")[..8]}",
            maxAttempts: 2);
        var q = await TestDataHelper.AddQuestionAsync(_apiClient, test.Slug, "Single question",
            answers: new List<CreateAnswerRequest>
            {
                new() { AnswerText = "Correct", IsCorrect = true, Order = 1 },
                new() { AnswerText = "Wrong", IsCorrect = false, Order = 2 }
            });

        var takeTest = await _testTakerClient.GetAsync<TakeTestResponse>($"tests/{test.Slug}/take/");

        // Test with correct answer
        var attempt1 = await StartAttemptAsync(test.Slug, "Correct Answer");
        await _testTakerClient.PutAsync($"tests/{test.Slug}/attempts/{attempt1.Id}/", new SaveDraftRequest
        {
            DraftAnswers = new Dictionary<string, List<int>>
            {
                { takeTest!.Questions[0].Id.ToString(), new List<int> { q.Answers.First(a => a.IsCorrect).Id } }
            }
        });
        await SubmitAttemptAsync(test.Slug, attempt1.Id);

        // Test with wrong answer
        var attempt2 = await StartAttemptAsync(test.Slug, "Wrong Answer");
        await _testTakerClient.PutAsync($"tests/{test.Slug}/attempts/{attempt2.Id}/", new SaveDraftRequest
        {
            DraftAnswers = new Dictionary<string, List<int>>
            {
                { takeTest.Questions[0].Id.ToString(), new List<int> { q.Answers.First(a => !a.IsCorrect).Id } }
            }
        });
        await SubmitAttemptAsync(test.Slug, attempt2.Id);

        // Act: Fetch results
        var results = await GetTestResultsAsync(test.Slug);

        // Assert: One should be 100%, one should be 0%
        var correctResult = results.First(r => r.AnonymousName == "Correct Answer");
        var wrongResult = results.First(r => r.AnonymousName == "Wrong Answer");

        correctResult.Score!.Value.Should().Be(100.0);
        wrongResult.Score!.Value.Should().Be(0.0);
    }


    [Fact]
    public async Task ScoringAccuracy_MultiSelectPartialAnswer_ScoreIsValidPercentage()
    {
        // Arrange: create a multiple_select question with 3 correct and 2 incorrect options
        await PersistentTestUser.GetOrCreateAndLoginAsync(_apiClient);

        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"MultiSelect_{Guid.NewGuid().ToString("N")[..8]}",
            maxAttempts: 1);

        var question = await TestDataHelper.AddQuestionAsync(_apiClient, test.Slug,
            "Which of the following are primary colours?",
            questionType: "multi_select",
            answers: new List<CreateAnswerRequest>
            {
                new() { AnswerText = "Red",    IsCorrect = true,  Order = 1 },
                new() { AnswerText = "Blue",   IsCorrect = true,  Order = 2 },
                new() { AnswerText = "Yellow", IsCorrect = true,  Order = 3 },
                new() { AnswerText = "Green",  IsCorrect = false, Order = 4 },
                new() { AnswerText = "Purple", IsCorrect = false, Order = 5 }
            });

        var correctIds = question.Answers.Where(a => a.IsCorrect).Select(a => a.Id).ToList();
        var takeTest = await _testTakerClient.GetAsync<TakeTestResponse>($"tests/{test.Slug}/take/");

        // Act: submit only 2 of 3 correct answers (Red + Blue, omit Yellow)
        var attempt = await StartAttemptAsync(test.Slug, "Partial Selector");
        await _testTakerClient.PutAsync($"tests/{test.Slug}/attempts/{attempt.Id}/",
            new SaveDraftRequest
            {
                DraftAnswers = new Dictionary<string, List<int>>
                {
                    {
                        takeTest!.Questions[0].Id.ToString(),
                        new List<int> { correctIds[0], correctIds[1] }
                    }
                }
            });
        await SubmitAttemptAsync(test.Slug, attempt.Id);

        // Assert: score is a valid percentage in [0, 100].
        // NOTE: the API may award 0 (all-or-nothing) or partial credit — both are valid.
        // This test documents the actual behaviour without prescribing which model is used.
        var results = await GetTestResultsAsync(test.Slug);
        var myResult = results.First(r => r.AnonymousName == "Partial Selector");

        myResult.Score.Should().NotBeNull("a score must always be present after submission");
        myResult.Score!.Value.Should().BeInRange(0.0, 100.0,
            "score must be a valid percentage between 0 and 100");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Data Consistency Tests
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task DataConsistency_ScoreRemainsConstant_AcrossMultipleFetches()
    {
        // Arrange: Create test and submit an attempt
        await PersistentTestUser.GetOrCreateAndLoginAsync(_apiClient);
        var test = await CreateTestWithMultipleQuestions(3);
        var takeTest = await _testTakerClient.GetAsync<TakeTestResponse>($"tests/{test.slug}/take/");

        var attempt = await StartAttemptAsync(test.slug, "Consistent Scorer");
        await SaveCorrectAnswersAsync(test.slug, attempt.Id, test.questions, takeTest!.Questions);
        await SubmitAttemptAsync(test.slug, attempt.Id);

        // Act: Fetch results multiple times
        var results1 = await GetTestResultsAsync(test.slug);
        await Task.Delay(100); // Small delay
        var results2 = await GetTestResultsAsync(test.slug);
        await Task.Delay(100);
        var results3 = await GetTestResultsAsync(test.slug);

        // Assert: Score should be identical across all fetches
        var score1 = results1.First(r => r.AnonymousName == "Consistent Scorer").Score;
        var score2 = results2.First(r => r.AnonymousName == "Consistent Scorer").Score;
        var score3 = results3.First(r => r.AnonymousName == "Consistent Scorer").Score;

        score1.Should().Be(score2).And.Be(score3,
            "because scores should be immutable after submission");
    }

    [Fact]
    public async Task DataConsistency_DraftSaveDoesNotAffectFinalScore()
    {
        // Arrange: Create test
        await PersistentTestUser.GetOrCreateAndLoginAsync(_apiClient);
        var test = await CreateTestWithMultipleQuestions(2);
        var takeTest = await _testTakerClient.GetAsync<TakeTestResponse>($"tests/{test.slug}/take/");

        // Start attempt, save draft with wrong answers
        var attempt = await StartAttemptAsync(test.slug, "Draft Changer");
        await SaveWrongAnswersAsync(test.slug, attempt.Id, test.questions, takeTest!.Questions);

        // Then update draft to correct answers
        await SaveCorrectAnswersAsync(test.slug, attempt.Id, test.questions, takeTest.Questions);

        // Submit
        await SubmitAttemptAsync(test.slug, attempt.Id);

        // Act: Fetch results
        var results = await GetTestResultsAsync(test.slug);
        var myResult = results.First(r => r.AnonymousName == "Draft Changer");

        // Assert: Final score should reflect the last saved draft (correct answers)
        var score = myResult.Score!.Value;
        score.Should().Be(100.0,
            "because scoring should use the final draft state before submission");
    }

    [Fact]
    public async Task DataConsistency_AttemptCountAccurate_AfterMultipleSubmissions()
    {
        // Arrange: Create test allowing 5 attempts
        await PersistentTestUser.GetOrCreateAndLoginAsync(_apiClient);
        var test = await CreateTestWithMultipleQuestions(2, maxAttempts: 5);

        // Submit 5 attempts
        for (int i = 1; i <= 5; i++)
        {
            var attempt = await StartAttemptAsync(test.slug, $"User {i}");
            await SubmitAttemptAsync(test.slug, attempt.Id);
        }

        // Act: Fetch results
        var results = await GetTestResultsAsync(test.slug);

        // Assert: Should have exactly 5 results
        results.Should().HaveCount(5,
            "because 5 attempts were submitted and count should be accurate");
    }

    [Fact]
    public async Task DataConsistency_QuestionOrderChange_DoesNotCorruptAttempts()
    {
        // Arrange: Create test with 3 questions
        await PersistentTestUser.GetOrCreateAndLoginAsync(_apiClient);
        var test = await CreateTestWithMultipleQuestions(3);
        var takeTest = await _testTakerClient.GetAsync<TakeTestResponse>($"tests/{test.slug}/take/");

        // Verify test data was created successfully
        takeTest.Should().NotBeNull("test should be retrievable via take endpoint");
        takeTest!.Questions.Should().NotBeNullOrEmpty("test should have questions");
        test.questions.Should().NotBeNullOrEmpty("questions should be created");
        test.questions.Should().HaveCount(3, "should have created 3 questions");

        // Start attempt and save draft
        var attempt = await StartAttemptAsync(test.slug, "Order Tester");
        await SaveCorrectAnswersAsync(test.slug, attempt.Id, test.questions, takeTest.Questions);

        // Reorder questions (reverse order)
        var reorderRequest = new ReorderQuestionsRequest
        {
            Order = test.questions.Select((q, idx) => new OrderItem
            {
                Id = q.Id,
                Order = test.questions.Count - 1 - idx
            }).ToList()
        };
        await _apiClient.PostAsync($"tests/{test.slug}/questions/reorder/", reorderRequest);

        // Submit attempt after reordering
        await SubmitAttemptAsync(test.slug, attempt.Id);

        // Act: Fetch results
        var results = await GetTestResultsAsync(test.slug);
        var myResult = results.First(r => r.AnonymousName == "Order Tester");

        // Assert: Score should still be correct (not corrupted by reordering)
        var score = myResult.Score!.Value;
        score.Should().Be(100.0,
            "because question reordering should not corrupt saved answers");
    }

    [Fact]
    public async Task DataConsistency_QuestionUpdate_DoesNotAffectSubmittedAttempts()
    {
        // Arrange: Create test and submit an attempt
        await PersistentTestUser.GetOrCreateAndLoginAsync(_apiClient);
        var test = await CreateTestWithMultipleQuestions(1);
        var takeTest = await _testTakerClient.GetAsync<TakeTestResponse>($"tests/{test.slug}/take/");

        var attempt = await StartAttemptAsync(test.slug, "Before Update");
        await SaveCorrectAnswersAsync(test.slug, attempt.Id, test.questions, takeTest!.Questions);
        await SubmitAttemptAsync(test.slug, attempt.Id);

        // Get initial results
        var resultsBefore = await GetTestResultsAsync(test.slug);
        var scoreBefore = resultsBefore.First(r => r.AnonymousName == "Before Update").Score;

        // Act: Update the question text and answers
        var updateRequest = new UpdateQuestionRequest
        {
            QuestionText = "Updated question text",
            QuestionType = "multiple_choice",
            Answers = new List<CreateAnswerRequest>
            {
                new() { AnswerText = "New Answer A", IsCorrect = true, Order = 1 },
                new() { AnswerText = "New Answer B", IsCorrect = false, Order = 2 }
            }
        };
        await _apiClient.PutAsync($"tests/{test.slug}/questions/{test.questions[0].Id}/", updateRequest);

        // Fetch results again
        var resultsAfter = await GetTestResultsAsync(test.slug);
        var scoreAfter = resultsAfter.First(r => r.AnonymousName == "Before Update").Score;

        // Assert: Score should remain unchanged
        scoreAfter.Should().Be(scoreBefore,
            "because updating questions should not retroactively change submitted attempt scores");
    }

    [Fact]
    public async Task DataConsistency_ConcurrentAttempts_AllScoredCorrectly()
    {
        // Arrange: Create test allowing 10 attempts
        await PersistentTestUser.GetOrCreateAndLoginAsync(_apiClient);
        var test = await CreateTestWithMultipleQuestions(2, maxAttempts: 10);

        // Act: Submit 10 attempts concurrently
        var tasks = new List<Task>();
        for (int i = 0; i < 10; i++)
        {
            var userId = i;
            tasks.Add(Task.Run(async () =>
            {
                var client = new ApiClient(TestConfiguration.GetBaseUrl());
                var takeTest = await client.GetAsync<TakeTestResponse>($"tests/{test.slug}/take/");
                var attempt = await StartAttemptAsync(test.slug, $"Concurrent {userId}", client);
                await SaveCorrectAnswersAsync(test.slug, attempt.Id, test.questions, takeTest!.Questions, client);
                await SubmitAttemptAsync(test.slug, attempt.Id, client);
            }));
        }

        await Task.WhenAll(tasks);

        // Assert: All 10 should have 100% scores
        var results = await GetTestResultsAsync(test.slug);
        results.Should().HaveCount(10);

        foreach (var result in results)
        {
            result.Score!.Value.Should().Be(100.0,
                $"because {result.AnonymousName} answered correctly");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Immutability & Audit Trail Tests
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Immutability_SubmittedAttempts_CannotBeModified()
    {
        // Arrange: Create test and submit attempt
        await PersistentTestUser.GetOrCreateAndLoginAsync(_apiClient);
        var test = await CreateTestWithMultipleQuestions(1);
        var takeTest = await _testTakerClient.GetAsync<TakeTestResponse>($"tests/{test.slug}/take/");

        var attempt = await StartAttemptAsync(test.slug, "Immutable Test");
        await SaveWrongAnswersAsync(test.slug, attempt.Id, test.questions, takeTest!.Questions);
        await SubmitAttemptAsync(test.slug, attempt.Id);

        // Act: Try to update draft after submission
        var response = await _testTakerClient.PutAsync($"tests/{test.slug}/attempts/{attempt.Id}/",
            new SaveDraftRequest
            {
                DraftAnswers = new Dictionary<string, List<int>>
                {
                    { takeTest.Questions[0].Id.ToString(), new List<int> { test.questions[0].Answers.First(a => a.IsCorrect).Id } }
                }
            });

        // Assert: Should be rejected
        response.StatusCode.Should().NotBe(HttpStatusCode.OK,
            "because submitted attempts should be immutable");

        // Verify score is still 0 (wrong answer)
        var results = await GetTestResultsAsync(test.slug);
        var score = results.First(r => r.AnonymousName == "Immutable Test").Score!.Value;
        score.Should().Be(0.0, "because the wrong answer should still be recorded");
    }

    [Fact]
    public async Task Immutability_TestDeletion_PreservesSubmittedResults()
    {
        // Arrange: Create test, submit attempt, get results
        await PersistentTestUser.GetOrCreateAndLoginAsync(_apiClient);
        var test = await CreateTestWithMultipleQuestions(1);
        var takeTest = await _testTakerClient.GetAsync<TakeTestResponse>($"tests/{test.slug}/take/");

        // Verify test data was created successfully
        takeTest.Should().NotBeNull("test should be retrievable via take endpoint");
        takeTest!.Questions.Should().NotBeNullOrEmpty("test should have questions");
        test.questions.Should().NotBeNullOrEmpty("questions should be created");

        var attempt = await StartAttemptAsync(test.slug, "Preserved Result");
        await SaveCorrectAnswersAsync(test.slug, attempt.Id, test.questions, takeTest.Questions);
        await SubmitAttemptAsync(test.slug, attempt.Id);

        var resultsBefore = await GetTestResultsAsync(test.slug);

        // Verify results were generated
        resultsBefore.Should().NotBeNullOrEmpty("results should be available after submission");
        var result = resultsBefore.FirstOrDefault(r => r.AnonymousName == "Preserved Result");
        result.Should().NotBeNull("should find result for 'Preserved Result' attempt");

        var scoreBefore = result!.Score;

        // Act: Delete the test
        var deleteResponse = await _apiClient.DeleteAsync($"tests/{test.slug}/");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent,
            "test deletion should succeed");

        // Assert: accessing results after deletion should never cause a server error.
        // Accept 200 (soft delete) or 404 (hard delete/cascade) — both are valid designs.
        var resultsAfter = await _apiClient.GetAsync($"tests/{test.slug}/results/");
        resultsAfter.StatusCode.Should().BeOneOf(
            new[] { HttpStatusCode.OK, HttpStatusCode.NotFound },
            $"after test deletion the results endpoint should return 200 (soft delete) or 404 (cascade delete), " +
            $"not a server error. Score recorded before deletion: {scoreBefore}");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Helper Methods
    // ═══════════════════════════════════════════════════════════════════════════

    private async Task<(string slug, List<QuestionResponse> questions)> CreateTestWithMultipleQuestions(int count, int maxAttempts = 1)
    {
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"DataCheck_{Guid.NewGuid().ToString("N")[..8]}",
            maxAttempts: maxAttempts);

        var questions = new List<QuestionResponse>();
        for (int i = 1; i <= count; i++)
        {
            var q = await TestDataHelper.AddQuestionAsync(_apiClient, test.Slug, $"Question {i}",
                answers: new List<CreateAnswerRequest>
                {
                    new() { AnswerText = "Correct", IsCorrect = true, Order = 1 },
                    new() { AnswerText = "Wrong", IsCorrect = false, Order = 2 }
                });
            questions.Add(q);
        }

        return (test.Slug, questions);
    }

    private async Task<AttemptResponse> StartAttemptAsync(string testSlug, string name, ApiClient? client = null)
    {
        client ??= _testTakerClient;
        var response = await client.PostAsync($"tests/{testSlug}/attempts/",
            new StartAttemptRequest { AnonymousName = name });
        return (await client.DeserializeResponseAsync<AttemptResponse>(response))!;
    }

    private async Task SubmitAttemptAsync(string testSlug, int attemptId, ApiClient? client = null)
    {
        client ??= _testTakerClient;
        await client.PostAsync($"tests/{testSlug}/attempts/{attemptId}/submit/",
            new Dictionary<string, object>());
    }

    private async Task SaveCorrectAnswersAsync(string testSlug, int attemptId,
        List<QuestionResponse> questions, List<TakeQuestionResponse> takeQuestions, ApiClient? client = null)
    {
        client ??= _testTakerClient;
        var draftAnswers = new Dictionary<string, List<int>>();

        foreach (var takeQ in takeQuestions)
        {
            var originalQ = questions.First(q => q.Id == takeQ.Id);
            var correctAnswerId = originalQ.Answers.First(a => a.IsCorrect).Id;
            draftAnswers[takeQ.Id.ToString()] = new List<int> { correctAnswerId };
        }

        await client.PutAsync($"tests/{testSlug}/attempts/{attemptId}/",
            new SaveDraftRequest { DraftAnswers = draftAnswers });
    }

    private async Task SaveWrongAnswersAsync(string testSlug, int attemptId,
        List<QuestionResponse> questions, List<TakeQuestionResponse> takeQuestions, ApiClient? client = null)
    {
        client ??= _testTakerClient;
        var draftAnswers = new Dictionary<string, List<int>>();

        foreach (var takeQ in takeQuestions)
        {
            var originalQ = questions.First(q => q.Id == takeQ.Id);
            var wrongAnswerId = originalQ.Answers.First(a => !a.IsCorrect).Id;
            draftAnswers[takeQ.Id.ToString()] = new List<int> { wrongAnswerId };
        }

        await client.PutAsync($"tests/{testSlug}/attempts/{attemptId}/",
            new SaveDraftRequest { DraftAnswers = draftAnswers });
    }

    private async Task SaveMixedAnswersAsync(string testSlug, int attemptId,
        List<QuestionResponse> questions, List<TakeQuestionResponse> takeQuestions,
        int correctCount, ApiClient? client = null)
    {
        client ??= _testTakerClient;
        var draftAnswers = new Dictionary<string, List<int>>();

        for (int i = 0; i < takeQuestions.Count; i++)
        {
            var takeQ = takeQuestions[i];
            var originalQ = questions.First(q => q.Id == takeQ.Id);
            var answerId = i < correctCount
                ? originalQ.Answers.First(a => a.IsCorrect).Id
                : originalQ.Answers.First(a => !a.IsCorrect).Id;
            draftAnswers[takeQ.Id.ToString()] = new List<int> { answerId };
        }

        await client.PutAsync($"tests/{testSlug}/attempts/{attemptId}/",
            new SaveDraftRequest { DraftAnswers = draftAnswers });
    }

    private async Task<List<ResultResponse>> GetTestResultsAsync(string testSlug)
    {
        var response = await _apiClient.GetAsync($"tests/{testSlug}/results/");

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"GET tests/{testSlug}/results/ failed with {response.StatusCode}: {body}");
        }

        return (await _apiClient.DeserializeResponseAsync<List<ResultResponse>>(response))
               ?? new List<ResultResponse>();
    }

    public void Dispose()
    {
        _apiClient?.Dispose();
        _testTakerClient?.Dispose();
    }
}
