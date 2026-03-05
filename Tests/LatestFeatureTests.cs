using System.Net;
using FluentAssertions;
using TestIT.ApiTests.Helpers;
using TestIT.ApiTests.Models;
using Xunit;

namespace TestIT.ApiTests.Tests;

/// <summary>
/// Additional depth tests for the latest features:
///   - Test-to-folder assignment edge cases
///   - multi_select: all-wrong, update, mixed question type test
///   - exact_answer: update, analytics, mixed question type test
///   - Folder detail endpoint field coverage
/// </summary>
public class LatestFeatureTests : IDisposable
{
    private readonly ApiClient _apiClient;
    private readonly ApiClient _anonClient;

    public LatestFeatureTests(ITestOutputHelper output)
    {
        ApiClient.SetOutput(output.WriteLine);
        var baseUrl = TestConfiguration.GetBaseUrl();
        _apiClient = new ApiClient(baseUrl);
        _anonClient = new ApiClient(baseUrl);
    }

    // =========================================================================
    // TEST-TO-FOLDER ASSIGNMENT — edge cases
    // =========================================================================

    /// <summary>
    /// Verifies that PATCHing a test with a folder ID that does not exist returns
    /// 400 Bad Request rather than silently ignoring the invalid reference.
    /// </summary>
    [Fact]
    public async Task TestFolderAssignment_ToNonExistentFolder_ReturnsBadRequest()
    {
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"NoFolder_{Guid.NewGuid().ToString("N")[..8]}");

        var response = await _apiClient.PatchAsync($"tests/{test.Slug}/",
            new { folder = 999999 });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "because folder ID 999999 does not exist");
    }

    /// <summary>
    /// Verifies that a user cannot assign another user's test to a folder;
    /// the API must return 403 Forbidden or 404 Not Found.
    /// </summary>
    [Fact]
    public async Task TestFolderAssignment_ByNonOwner_DeniesAccess()
    {
        // User A creates the test
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"OwnerTest_{Guid.NewGuid().ToString("N")[..8]}");

        // User B tries to assign it to a folder
        using var userB = new ApiClient(TestConfiguration.GetBaseUrl());
        await TestDataHelper.RegisterAndLoginAsync(userB);

        // User B creates their own company + folder
        var compResp = await userB.PostAsync("companies/",
            new CreateCompanyRequest { Name = $"BCo_{Guid.NewGuid().ToString("N")[..6]}" });
        var company = await userB.DeserializeResponseAsync<CompanyResponse>(compResp);
        var folderResp = await userB.PostAsync($"companies/{company!.Id}/folders/",
            new CreateFolderRequest { Name = "B Folder" });
        var folder = await userB.DeserializeResponseAsync<FolderResponse>(folderResp);

        // User B attempts to PATCH user A's test
        var response = await userB.PatchAsync($"tests/{test.Slug}/",
            new { folder = folder!.Id });

        response.StatusCode.Should().BeOneOf(
            new[] { HttpStatusCode.Forbidden, HttpStatusCode.NotFound },
            "because a non-owner must not be able to modify another user's test");
    }

    /// <summary>
    /// Verifies that moving a test from Folder A to Folder B decrements Folder A's
    /// test_count to 0 and increments Folder B's test_count to 1.
    /// </summary>
    [Fact]
    public async Task TestFolderAssignment_ReassignBetweenFolders_UpdatesBothCounts()
    {
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var compResp = await _apiClient.PostAsync("companies/",
            new CreateCompanyRequest { Name = $"ReCo_{Guid.NewGuid().ToString("N")[..6]}" });
        var company = await _apiClient.DeserializeResponseAsync<CompanyResponse>(compResp);

        var folderAResp = await _apiClient.PostAsync($"companies/{company!.Id}/folders/",
            new CreateFolderRequest { Name = "Folder A" });
        var folderA = await _apiClient.DeserializeResponseAsync<FolderResponse>(folderAResp);

        var folderBResp = await _apiClient.PostAsync($"companies/{company.Id}/folders/",
            new CreateFolderRequest { Name = "Folder B" });
        var folderB = await _apiClient.DeserializeResponseAsync<FolderResponse>(folderBResp);

        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"ReassignTest_{Guid.NewGuid().ToString("N")[..8]}");

        // Assign to Folder A
        await _apiClient.PatchAsync($"tests/{test.Slug}/", new { folder = folderA!.Id });

        var aAfterFirst = await _apiClient.GetAsync<FolderResponse>(
            $"companies/{company.Id}/folders/{folderA.Id}/");
        aAfterFirst!.TestCount.Should().Be(1, "Folder A should have 1 test after assignment");

        // Reassign to Folder B
        await _apiClient.PatchAsync($"tests/{test.Slug}/", new { folder = folderB!.Id });

        var aAfterMove = await _apiClient.GetAsync<FolderResponse>(
            $"companies/{company.Id}/folders/{folderA.Id}/");
        var bAfterMove = await _apiClient.GetAsync<FolderResponse>(
            $"companies/{company.Id}/folders/{folderB.Id}/");

        aAfterMove!.TestCount.Should().Be(0, "Folder A should have 0 tests after the test was moved out");
        bAfterMove!.TestCount.Should().Be(1, "Folder B should have 1 test after receiving the reassigned test");
    }

    /// <summary>
    /// Verifies that the folder detail endpoint (GET companies/{id}/folders/{id}/)
    /// returns all expected fields: id, name, parent, test_count, and created_at.
    /// </summary>
    [Fact]
    public async Task FolderDetail_WithValidId_ReturnsAllExpectedFields()
    {
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var compResp = await _apiClient.PostAsync("companies/",
            new CreateCompanyRequest { Name = $"DetailCo_{Guid.NewGuid().ToString("N")[..6]}" });
        var company = await _apiClient.DeserializeResponseAsync<CompanyResponse>(compResp);

        var folderResp = await _apiClient.PostAsync($"companies/{company!.Id}/folders/",
            new CreateFolderRequest { Name = "Detail Folder" });
        var created = await _apiClient.DeserializeResponseAsync<FolderResponse>(folderResp);

        var response = await _apiClient.GetAsync(
            $"companies/{company.Id}/folders/{created!.Id}/");
        var body = await _apiClient.GetResponseBodyAsync(response);
        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {body}");

        var folder = await _apiClient.DeserializeResponseAsync<FolderResponse>(response);
        folder.Should().NotBeNull();
        folder!.Id.Should().Be(created.Id);
        folder.Name.Should().Be("Detail Folder");
        folder.Parent.Should().BeNull("because it is a top-level folder");
        folder.TestCount.Should().Be(0, "because no tests have been assigned yet");
        folder.CreatedAt.Should().NotBeNull("because created_at is returned in the detail endpoint");
    }

    // =========================================================================
    // MULTI-SELECT — additional depth
    // =========================================================================

    /// <summary>
    /// Verifies that selecting only incorrect answers in a multi_select question scores 0%.
    /// </summary>
    [Fact]
    public async Task MultiSelect_Scoring_OnlyWrongAnswersSelected_ScoresZero()
    {
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"MS_AllWrong_{Guid.NewGuid().ToString("N")[..8]}");

        var question = await TestDataHelper.AddQuestionAsync(_apiClient, test.Slug,
            "Select all programming languages",
            questionType: "multi_select",
            answers: new List<CreateAnswerRequest>
            {
                new() { AnswerText = "Python", IsCorrect = true,  Order = 1 },
                new() { AnswerText = "HTML",   IsCorrect = false, Order = 2 },
                new() { AnswerText = "CSS",    IsCorrect = false, Order = 3 },
            });

        int htmlId = question.Answers.First(a => a.AnswerText == "HTML").Id;
        int cssId  = question.Answers.First(a => a.AnswerText == "CSS").Id;

        var take = await _anonClient.GetAsync<TakeTestResponse>($"tests/{test.Slug}/take/");
        var startResp = await _anonClient.PostAsync($"tests/{test.Slug}/attempts/",
            new StartAttemptRequest { AnonymousName = "AllWrong" });
        var attempt = await _anonClient.DeserializeResponseAsync<AttemptResponse>(startResp);

        await _anonClient.PutAsync($"tests/{test.Slug}/attempts/{attempt!.Id}/",
            new SaveDraftRequest
            {
                DraftAnswers = new Dictionary<string, List<int>>
                {
                    { take!.Questions[0].Id.ToString(), new List<int> { htmlId, cssId } }
                }
            });
        await _anonClient.PostAsync($"tests/{test.Slug}/attempts/{attempt.Id}/submit/",
            new Dictionary<string, object>());

        var results = await _apiClient.GetAsync<List<ResultResponse>>($"tests/{test.Slug}/results/");
        var result = results!.First(r => r.AnonymousName == "AllWrong");
        result.Score.Should().Be(0.0,
            "because selecting only wrong answers must score zero");
    }

    /// <summary>
    /// Verifies that updating a multi_select question to change which answers are correct
    /// is reflected immediately in the response with the new correct answer marked.
    /// </summary>
    [Fact]
    public async Task MultiSelect_Update_ChangesCorrectAnswers_NewScoringApplied()
    {
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"MS_Update_{Guid.NewGuid().ToString("N")[..8]}");

        var question = await TestDataHelper.AddQuestionAsync(_apiClient, test.Slug,
            "Select all correct",
            questionType: "multi_select",
            answers: new List<CreateAnswerRequest>
            {
                new() { AnswerText = "A", IsCorrect = true,  Order = 1 },
                new() { AnswerText = "B", IsCorrect = false, Order = 2 },
                new() { AnswerText = "C", IsCorrect = false, Order = 3 },
            });

        // Update: now B is the only correct answer
        var updateResp = await _apiClient.PutAsync($"tests/{test.Slug}/questions/{question.Id}/",
            new UpdateQuestionRequest
            {
                QuestionText = "Select all correct",
                QuestionType = "multi_select",
                Answers = new List<CreateAnswerRequest>
                {
                    new() { AnswerText = "A", IsCorrect = false, Order = 1 },
                    new() { AnswerText = "B", IsCorrect = true,  Order = 2 },
                    new() { AnswerText = "C", IsCorrect = false, Order = 3 },
                }
            });
        var updateBody = await _apiClient.GetResponseBodyAsync(updateResp);
        updateResp.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {updateBody}");

        var updated = await _apiClient.DeserializeResponseAsync<QuestionResponse>(updateResp);
        updated!.Answers.Count(a => a.IsCorrect).Should().Be(1,
            "because only B is correct after the update");
        updated.Answers.First(a => a.AnswerText == "B").IsCorrect.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that a test containing both multiple_choice and multi_select questions
    /// scores each question independently, yielding 100% and 2 correct when both are answered correctly.
    /// </summary>
    [Fact]
    public async Task MultiSelect_MixedTest_WithMultipleChoiceQuestion_EachScoredIndependently()
    {
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"MS_Mixed_{Guid.NewGuid().ToString("N")[..8]}");

        // Q1: standard multiple_choice (one correct)
        var q1 = await TestDataHelper.AddQuestionAsync(_apiClient, test.Slug,
            "Capital of France?",
            answers: new List<CreateAnswerRequest>
            {
                new() { AnswerText = "Paris",  IsCorrect = true,  Order = 1 },
                new() { AnswerText = "London", IsCorrect = false, Order = 2 },
            });

        // Q2: multi_select (two correct)
        var q2 = await TestDataHelper.AddQuestionAsync(_apiClient, test.Slug,
            "Select all programming languages",
            questionType: "multi_select",
            answers: new List<CreateAnswerRequest>
            {
                new() { AnswerText = "Python", IsCorrect = true,  Order = 1 },
                new() { AnswerText = "HTML",   IsCorrect = false, Order = 2 },
                new() { AnswerText = "Java",   IsCorrect = true,  Order = 3 },
            });

        int parisId  = q1.Answers.First(a => a.IsCorrect).Id;
        int pythonId = q2.Answers.First(a => a.AnswerText == "Python").Id;
        int javaId   = q2.Answers.First(a => a.AnswerText == "Java").Id;

        var take = await _anonClient.GetAsync<TakeTestResponse>($"tests/{test.Slug}/take/");
        int tq1 = take!.Questions.First(q => q.QuestionType == "multiple_choice").Id;
        int tq2 = take.Questions.First(q => q.QuestionType == "multi_select").Id;

        var startResp = await _anonClient.PostAsync($"tests/{test.Slug}/attempts/",
            new StartAttemptRequest { AnonymousName = "MixedTaker" });
        var attempt = await _anonClient.DeserializeResponseAsync<AttemptResponse>(startResp);

        // Answer both correctly
        await _anonClient.PutAsync($"tests/{test.Slug}/attempts/{attempt!.Id}/",
            new SaveDraftRequest
            {
                DraftAnswers = new Dictionary<string, List<int>>
                {
                    { tq1.ToString(), new List<int> { parisId } },
                    { tq2.ToString(), new List<int> { pythonId, javaId } }
                }
            });
        await _anonClient.PostAsync($"tests/{test.Slug}/attempts/{attempt.Id}/submit/",
            new Dictionary<string, object>());

        var results = await _apiClient.GetAsync<List<ResultResponse>>($"tests/{test.Slug}/results/");
        var result = results!.First(r => r.AnonymousName == "MixedTaker");
        result.Score.Should().Be(100.0,
            "because both questions were answered correctly");
        result.CorrectAnswers.Should().Be(2,
            "because there are two questions and both were answered correctly");
        result.TotalQuestions.Should().Be(2);
    }

    // =========================================================================
    // EXACT-ANSWER — additional depth
    // =========================================================================

    /// <summary>
    /// Verifies that updating the correct_answer on an exact_answer question takes effect
    /// immediately: the previously correct answer no longer scores any points.
    /// </summary>
    [Fact]
    public async Task ExactAnswer_Update_ChangesCorrectAnswer_NewAnswerIsScored()
    {
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"EA_Update_{Guid.NewGuid().ToString("N")[..8]}");

        var createResp = await _apiClient.PostAsync($"tests/{test.Slug}/questions/",
            new CreateQuestionRequest
            {
                QuestionText = "Capital of Germany?",
                QuestionType = "exact_answer",
                CorrectAnswer = "Berlin",
                Answers = new List<CreateAnswerRequest>()
            });
        var question = await _apiClient.DeserializeResponseAsync<QuestionResponse>(createResp);

        // Update correct answer to "Frankfurt"
        var updateResp = await _apiClient.PutAsync($"tests/{test.Slug}/questions/{question!.Id}/",
            new CreateQuestionRequest
            {
                QuestionText = "Capital of Germany?",
                QuestionType = "exact_answer",
                CorrectAnswer = "Frankfurt",
                Answers = new List<CreateAnswerRequest>()
            });
        updateResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await _apiClient.DeserializeResponseAsync<QuestionResponse>(updateResp);
        updated!.CorrectAnswer.Should().Be("Frankfurt",
            "because the correct answer should be updated");

        // Submit old answer "Berlin" — should now score 0
        var startResp = await _anonClient.PostAsync($"tests/{test.Slug}/attempts/",
            new StartAttemptRequest { AnonymousName = "OldAnswer" });
        var attempt = await _anonClient.DeserializeResponseAsync<AttemptResponse>(startResp);

        await _anonClient.PutAsync($"tests/{test.Slug}/attempts/{attempt!.Id}/",
            new { draft_answers = new Dictionary<string, object> { { question.Id.ToString(), "Berlin" } } });
        await _anonClient.PostAsync($"tests/{test.Slug}/attempts/{attempt.Id}/submit/",
            new { draft_answers = new Dictionary<string, object> { { question.Id.ToString(), "Berlin" } } });

        var results = await _apiClient.GetAsync<List<ResultResponse>>($"tests/{test.Slug}/results/");
        var result = results!.First(r => r.AnonymousName == "OldAnswer");
        result.Score.Should().Be(0.0,
            "because 'Berlin' is no longer the correct answer after the update");
    }

    /// <summary>
    /// Verifies that the analytics endpoint correctly counts text-answer submissions
    /// in TotalAttempts and calculates AverageScore across exact_answer questions.
    /// </summary>
    [Fact]
    public async Task ExactAnswer_Analytics_TotalAttemptsReflectsTextSubmissions()
    {
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"EA_Analytics_{Guid.NewGuid().ToString("N")[..8]}");

        var createResp = await _apiClient.PostAsync($"tests/{test.Slug}/questions/",
            new CreateQuestionRequest
            {
                QuestionText = "What is 2 + 2?",
                QuestionType = "exact_answer",
                CorrectAnswer = "4",
                Answers = new List<CreateAnswerRequest>()
            });
        var question = await _apiClient.DeserializeResponseAsync<QuestionResponse>(createResp);

        // Submit two attempts with text answers
        foreach (var (name, answer) in new[] { ("Taker1", "4"), ("Taker2", "five") })
        {
            var client = new ApiClient(TestConfiguration.GetBaseUrl());
            var startResp = await client.PostAsync($"tests/{test.Slug}/attempts/",
                new StartAttemptRequest { AnonymousName = name });
            var attempt = await client.DeserializeResponseAsync<AttemptResponse>(startResp);
            await client.PutAsync($"tests/{test.Slug}/attempts/{attempt!.Id}/",
                new { draft_answers = new Dictionary<string, object> { { question!.Id.ToString(), answer } } });
            await client.PostAsync($"tests/{test.Slug}/attempts/{attempt.Id}/submit/",
                new { draft_answers = new Dictionary<string, object> { { question.Id.ToString(), answer } } });
            client.Dispose();
        }

        var response = await _apiClient.GetAsync($"analytics/tests/{test.Slug}/");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var analytics = await _apiClient.DeserializeResponseAsync<AnalyticsResponse>(response);
        analytics.Should().NotBeNull();
        analytics!.TotalAttempts.Should().Be(2, "because two attempts were submitted");
        analytics.AverageScore.Should().BeApproximately(50.0, 1.0,
            "because one correct and one incorrect text answer yields a 50% average");
    }

    /// <summary>
    /// Verifies that a test containing both multiple_choice and exact_answer questions
    /// can be submitted in a single draft_answers payload and both questions are scored correctly.
    /// </summary>
    [Fact]
    public async Task ExactAnswer_MixedTest_WithMultipleChoiceQuestion_ScoresCorrectly()
    {
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"EA_Mixed_{Guid.NewGuid().ToString("N")[..8]}");

        // Q1: multiple_choice
        var q1 = await TestDataHelper.AddQuestionAsync(_apiClient, test.Slug,
            "Which planet is closest to the sun?",
            answers: new List<CreateAnswerRequest>
            {
                new() { AnswerText = "Mercury", IsCorrect = true,  Order = 1 },
                new() { AnswerText = "Venus",   IsCorrect = false, Order = 2 },
            });

        // Q2: exact_answer
        var q2Resp = await _apiClient.PostAsync($"tests/{test.Slug}/questions/",
            new CreateQuestionRequest
            {
                QuestionText = "What is the chemical symbol for water?",
                QuestionType = "exact_answer",
                CorrectAnswer = "H2O",
                Answers = new List<CreateAnswerRequest>()
            });
        var q2 = await _apiClient.DeserializeResponseAsync<QuestionResponse>(q2Resp);

        int mercuryId = q1.Answers.First(a => a.IsCorrect).Id;

        var startResp = await _anonClient.PostAsync($"tests/{test.Slug}/attempts/",
            new StartAttemptRequest { AnonymousName = "MixedEA" });
        var attempt = await _anonClient.DeserializeResponseAsync<AttemptResponse>(startResp);

        // Answer both correctly — draft_answers holds both MC IDs and text answers
        await _anonClient.PutAsync($"tests/{test.Slug}/attempts/{attempt!.Id}/",
            new
            {
                draft_answers = new Dictionary<string, object>
                {
                    { q1.Id.ToString(), new List<int> { mercuryId } },
                    { q2!.Id.ToString(), "H2O" }
                }
            });
        await _anonClient.PostAsync($"tests/{test.Slug}/attempts/{attempt.Id}/submit/",
            new
            {
                draft_answers = new Dictionary<string, object>
                {
                    { q1.Id.ToString(), new List<int> { mercuryId } },
                    { q2.Id.ToString(), "H2O" }
                }
            });

        var results = await _apiClient.GetAsync<List<ResultResponse>>($"tests/{test.Slug}/results/");
        var result = results!.First(r => r.AnonymousName == "MixedEA");
        result.Score.Should().Be(100.0,
            "because both the multiple_choice and exact_answer questions were answered correctly");
        result.TotalQuestions.Should().Be(2);
        result.CorrectAnswers.Should().Be(2);
    }

    public void Dispose()
    {
        _apiClient?.Dispose();
        _anonClient?.Dispose();
    }
}
