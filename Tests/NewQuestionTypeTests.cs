using System.Net;
using FluentAssertions;
using TestIT.ApiTests.Helpers;
using TestIT.ApiTests.Models;
using Xunit;

namespace TestIT.ApiTests.Tests;

/// <summary>
/// Tests for the two new question types:
///   - multi_select  : multiple correct answers, all-or-nothing scoring
///   - exact_answer  : free-text input matched against a stored correct answer
/// </summary>
public class NewQuestionTypeTests : IDisposable
{
    private readonly ApiClient _apiClient;  // test author (authenticated)
    private readonly ApiClient _anonClient; // anonymous test taker

    public NewQuestionTypeTests(ITestOutputHelper output)
    {
        ApiClient.SetOutput(output.WriteLine);
        var baseUrl = TestConfiguration.GetBaseUrl();
        _apiClient = new ApiClient(baseUrl);
        _anonClient = new ApiClient(baseUrl);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<(TestResponse test, AttemptResponse attempt, TakeTestResponse take)>
        StartAttemptAsync(string name = "Tester")
    {
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"NQT_{Guid.NewGuid().ToString("N")[..8]}");
        var take = await _anonClient.GetAsync<TakeTestResponse>($"tests/{test.Slug}/take/");
        var startResp = await _anonClient.PostAsync($"tests/{test.Slug}/attempts/",
            new StartAttemptRequest { AnonymousName = name });
        var attempt = await _anonClient.DeserializeResponseAsync<AttemptResponse>(startResp);
        return (test, attempt!, take!);
    }

    private async Task<ResultResponse?> GetResultAsync(string slug, string testerName)
    {
        var results = await _apiClient.GetAsync<List<ResultResponse>>($"tests/{slug}/results/");
        return results?.FirstOrDefault(r => r.AnonymousName == testerName);
    }

    // =========================================================================
    // MULTI-SELECT  (multiple correct answers)
    // =========================================================================

    /// <summary>
    /// Verifies that a multi_select question with two correct answers is created successfully
    /// and the API returns 201 with the correct question type and answer count.
    /// </summary>
    [Fact]
    public async Task MultiSelect_Creation_WithMultipleCorrectAnswers_Succeeds()
    {
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"MS_Create_{Guid.NewGuid().ToString("N")[..8]}");

        var response = await _apiClient.PostAsync($"tests/{test.Slug}/questions/",
            new CreateQuestionRequest
            {
                QuestionText = "Which are programming languages?",
                QuestionType = "multi_select",
                Answers = new List<CreateAnswerRequest>
                {
                    new() { AnswerText = "Python", IsCorrect = true,  Order = 1 },
                    new() { AnswerText = "HTML",   IsCorrect = false, Order = 2 },
                    new() { AnswerText = "Java",   IsCorrect = true,  Order = 3 },
                    new() { AnswerText = "CSS",    IsCorrect = false, Order = 4 },
                }
            });

        var body = await _apiClient.GetResponseBodyAsync(response);
        response.StatusCode.Should().Be(HttpStatusCode.Created, $"Response: {body}");

        var question = await _apiClient.DeserializeResponseAsync<QuestionResponse>(response);
        question.Should().NotBeNull();
        question!.QuestionType.Should().Be("multi_select");
        question.Answers.Should().HaveCount(4);
        question.Answers.Count(a => a.IsCorrect).Should().Be(2,
            "because two answers are marked correct");
    }

    /// <summary>
    /// Verifies that creating a multi_select question with no correct answers is rejected
    /// with 400 Bad Request, enforcing the minimum-one-correct-answer constraint.
    /// </summary>
    [Fact]
    public async Task MultiSelect_Creation_WithNoCorrectAnswer_RejectsRequest()
    {
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"MS_NoCorrect_{Guid.NewGuid().ToString("N")[..8]}");

        var response = await _apiClient.PostAsync($"tests/{test.Slug}/questions/",
            new CreateQuestionRequest
            {
                QuestionText = "All wrong question",
                QuestionType = "multi_select",
                Answers = new List<CreateAnswerRequest>
                {
                    new() { AnswerText = "Wrong A", IsCorrect = false, Order = 1 },
                    new() { AnswerText = "Wrong B", IsCorrect = false, Order = 2 },
                }
            });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "because multi_select questions must have at least one correct answer");
    }

    /// <summary>
    /// Verifies that selecting every required correct answer in a multi_select question
    /// and no wrong answers scores 100%.
    /// </summary>
    [Fact]
    public async Task MultiSelect_Scoring_AllCorrectAnswersSelected_ScoresFullMarks()
    {
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"MS_AllCorrect_{Guid.NewGuid().ToString("N")[..8]}");

        var question = await TestDataHelper.AddQuestionAsync(_apiClient, test.Slug,
            "Select all programming languages",
            questionType: "multi_select",
            answers: new List<CreateAnswerRequest>
            {
                new() { AnswerText = "Python", IsCorrect = true,  Order = 1 },
                new() { AnswerText = "HTML",   IsCorrect = false, Order = 2 },
                new() { AnswerText = "Java",   IsCorrect = true,  Order = 3 },
            });

        int pythonId = question.Answers.First(a => a.AnswerText == "Python").Id;
        int javaId   = question.Answers.First(a => a.AnswerText == "Java").Id;

        var take = await _anonClient.GetAsync<TakeTestResponse>($"tests/{test.Slug}/take/");
        int takeQId = take!.Questions[0].Id;

        var startResp = await _anonClient.PostAsync($"tests/{test.Slug}/attempts/",
            new StartAttemptRequest { AnonymousName = "AllCorrect" });
        var attempt = await _anonClient.DeserializeResponseAsync<AttemptResponse>(startResp);

        await _anonClient.PutAsync($"tests/{test.Slug}/attempts/{attempt!.Id}/",
            new SaveDraftRequest
            {
                DraftAnswers = new Dictionary<string, List<int>>
                {
                    { takeQId.ToString(), new List<int> { pythonId, javaId } }
                }
            });
        var submitResp = await _anonClient.PostAsync(
            $"tests/{test.Slug}/attempts/{attempt.Id}/submit/",
            new Dictionary<string, object>());
        submitResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await GetResultAsync(test.Slug, "AllCorrect");
        result.Should().NotBeNull();
        result!.Score.Should().Be(100.0, "because all correct answers were selected");
        result.CorrectAnswers.Should().Be(1, "because the single multi-select question was answered correctly");
    }

    /// <summary>
    /// Verifies the all-or-nothing scoring rule: selecting only a subset of the required
    /// correct answers scores 0%, even though some selected answers are correct.
    /// </summary>
    [Fact]
    public async Task MultiSelect_Scoring_OnlyPartialCorrectAnswers_ScoresZero()
    {
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"MS_Partial_{Guid.NewGuid().ToString("N")[..8]}");

        var question = await TestDataHelper.AddQuestionAsync(_apiClient, test.Slug,
            "Select all programming languages",
            questionType: "multi_select",
            answers: new List<CreateAnswerRequest>
            {
                new() { AnswerText = "Python", IsCorrect = true,  Order = 1 },
                new() { AnswerText = "HTML",   IsCorrect = false, Order = 2 },
                new() { AnswerText = "Java",   IsCorrect = true,  Order = 3 },
            });

        int pythonId = question.Answers.First(a => a.AnswerText == "Python").Id;

        var take = await _anonClient.GetAsync<TakeTestResponse>($"tests/{test.Slug}/take/");
        int takeQId = take!.Questions[0].Id;

        var startResp = await _anonClient.PostAsync($"tests/{test.Slug}/attempts/",
            new StartAttemptRequest { AnonymousName = "PartialSelect" });
        var attempt = await _anonClient.DeserializeResponseAsync<AttemptResponse>(startResp);

        // Only select Python — Java is also required but missing
        await _anonClient.PutAsync($"tests/{test.Slug}/attempts/{attempt!.Id}/",
            new SaveDraftRequest
            {
                DraftAnswers = new Dictionary<string, List<int>>
                {
                    { takeQId.ToString(), new List<int> { pythonId } }
                }
            });
        await _anonClient.PostAsync($"tests/{test.Slug}/attempts/{attempt.Id}/submit/",
            new Dictionary<string, object>());

        var result = await GetResultAsync(test.Slug, "PartialSelect");
        result.Should().NotBeNull();
        result!.Score.Should().Be(0.0,
            "because multi_select scoring is all-or-nothing: missing one correct answer scores zero");
    }

    /// <summary>
    /// Verifies the all-or-nothing scoring rule: selecting all correct answers together
    /// with at least one wrong answer scores 0%.
    /// </summary>
    [Fact]
    public async Task MultiSelect_Scoring_CorrectPlusWrongAnswers_ScoresZero()
    {
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"MS_CorrectPlusWrong_{Guid.NewGuid().ToString("N")[..8]}");

        var question = await TestDataHelper.AddQuestionAsync(_apiClient, test.Slug,
            "Select all programming languages",
            questionType: "multi_select",
            answers: new List<CreateAnswerRequest>
            {
                new() { AnswerText = "Python", IsCorrect = true,  Order = 1 },
                new() { AnswerText = "HTML",   IsCorrect = false, Order = 2 },
                new() { AnswerText = "Java",   IsCorrect = true,  Order = 3 },
            });

        int pythonId = question.Answers.First(a => a.AnswerText == "Python").Id;
        int javaId   = question.Answers.First(a => a.AnswerText == "Java").Id;
        int htmlId   = question.Answers.First(a => a.AnswerText == "HTML").Id;

        var take = await _anonClient.GetAsync<TakeTestResponse>($"tests/{test.Slug}/take/");
        int takeQId = take!.Questions[0].Id;

        var startResp = await _anonClient.PostAsync($"tests/{test.Slug}/attempts/",
            new StartAttemptRequest { AnonymousName = "CorrectPlusWrong" });
        var attempt = await _anonClient.DeserializeResponseAsync<AttemptResponse>(startResp);

        // Select both correct answers but also include the wrong one
        await _anonClient.PutAsync($"tests/{test.Slug}/attempts/{attempt!.Id}/",
            new SaveDraftRequest
            {
                DraftAnswers = new Dictionary<string, List<int>>
                {
                    { takeQId.ToString(), new List<int> { pythonId, javaId, htmlId } }
                }
            });
        await _anonClient.PostAsync($"tests/{test.Slug}/attempts/{attempt.Id}/submit/",
            new Dictionary<string, object>());

        var result = await GetResultAsync(test.Slug, "CorrectPlusWrong");
        result.Should().NotBeNull();
        result!.Score.Should().Be(0.0,
            "because selecting a wrong answer in addition to correct ones disqualifies the attempt");
    }

    /// <summary>
    /// Verifies that the take endpoint returns all answers with is_correct = false
    /// for multi_select questions, preventing test takers from identifying correct options.
    /// </summary>
    [Fact]
    public async Task MultiSelect_TakeEndpoint_DoesNotExposeIsCorrect()
    {
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"MS_HideCorrect_{Guid.NewGuid().ToString("N")[..8]}");

        await TestDataHelper.AddQuestionAsync(_apiClient, test.Slug,
            "Which are programming languages?",
            questionType: "multi_select",
            answers: new List<CreateAnswerRequest>
            {
                new() { AnswerText = "Python", IsCorrect = true,  Order = 1 },
                new() { AnswerText = "HTML",   IsCorrect = false, Order = 2 },
                new() { AnswerText = "Java",   IsCorrect = true,  Order = 3 },
            });

        var take = await _anonClient.GetAsync<TakeTestResponse>($"tests/{test.Slug}/take/");

        take.Should().NotBeNull();
        take!.Questions.Should().HaveCount(1);
        take.Questions[0].Answers.Should().AllSatisfy(a =>
            a.IsCorrect.Should().BeFalse(
                "because the take endpoint must never reveal which answers are correct"));
    }

    // =========================================================================
    // EXACT-ANSWER  (free-text input)
    // =========================================================================

    /// <summary>
    /// Verifies that an exact_answer question is created successfully with a valid
    /// correct_answer value, and that the stored answer is returned to the author.
    /// </summary>
    [Fact]
    public async Task ExactAnswer_Creation_WithCorrectAnswer_Succeeds()
    {
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"EA_Create_{Guid.NewGuid().ToString("N")[..8]}");

        var response = await _apiClient.PostAsync($"tests/{test.Slug}/questions/",
            new CreateQuestionRequest
            {
                QuestionText = "What is the capital of France?",
                QuestionType = "exact_answer",
                CorrectAnswer = "Paris",
                Answers = new List<CreateAnswerRequest>()
            });

        var body = await _apiClient.GetResponseBodyAsync(response);
        response.StatusCode.Should().Be(HttpStatusCode.Created, $"Response: {body}");

        var question = await _apiClient.DeserializeResponseAsync<QuestionResponse>(response);
        question.Should().NotBeNull();
        question!.QuestionType.Should().Be("exact_answer");
        question.CorrectAnswer.Should().Be("Paris",
            "because the API should store and return the correct answer to the author");
        question.Answers.Should().BeEmpty("because exact_answer questions use a text field, not answer choices");
    }

    /// <summary>
    /// Verifies that creating an exact_answer question with an empty correct_answer
    /// is rejected with 400 Bad Request.
    /// </summary>
    [Fact]
    public async Task ExactAnswer_Creation_WithEmptyCorrectAnswer_RejectsRequest()
    {
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"EA_EmptyCA_{Guid.NewGuid().ToString("N")[..8]}");

        var response = await _apiClient.PostAsync($"tests/{test.Slug}/questions/",
            new CreateQuestionRequest
            {
                QuestionText = "Name the element with symbol Au",
                QuestionType = "exact_answer",
                CorrectAnswer = "",
                Answers = new List<CreateAnswerRequest>()
            });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "because an exact_answer question requires a non-empty correct answer");
    }

    /// <summary>
    /// Verifies that a correct_answer value exceeding the 30-character maximum is rejected
    /// with 400 Bad Request.
    /// </summary>
    [Fact]
    public async Task ExactAnswer_Creation_CorrectAnswerExceedsMaxLength_RejectsRequest()
    {
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"EA_TooLong_{Guid.NewGuid().ToString("N")[..8]}");

        var response = await _apiClient.PostAsync($"tests/{test.Slug}/questions/",
            new CreateQuestionRequest
            {
                QuestionText = "Describe in one word",
                QuestionType = "exact_answer",
                CorrectAnswer = new string('A', 31),  // 31 characters — one over the limit
                Answers = new List<CreateAnswerRequest>()
            });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "because the correct answer must be 30 characters or fewer");
    }

    /// <summary>
    /// Verifies that the take endpoint omits the correct_answer field for exact_answer
    /// questions, ensuring test takers cannot see the expected answer.
    /// </summary>
    [Fact]
    public async Task ExactAnswer_TakeEndpoint_DoesNotRevealCorrectAnswer()
    {
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"EA_HideCA_{Guid.NewGuid().ToString("N")[..8]}");

        await _apiClient.PostAsync($"tests/{test.Slug}/questions/",
            new CreateQuestionRequest
            {
                QuestionText = "What is the capital of France?",
                QuestionType = "exact_answer",
                CorrectAnswer = "Paris",
                Answers = new List<CreateAnswerRequest>()
            });

        var take = await _anonClient.GetAsync<TakeTestResponse>($"tests/{test.Slug}/take/");

        take.Should().NotBeNull();
        take!.Questions.Should().HaveCount(1);

        var q = take.Questions[0];
        q.QuestionType.Should().Be("exact_answer");
        q.CorrectAnswer.Should().BeNullOrEmpty(
            "because the take endpoint must not expose the correct answer to test takers");
    }

    /// <summary>
    /// Verifies that submitting the exact correct text for an exact_answer question
    /// scores 100%.
    /// </summary>
    [Fact]
    public async Task ExactAnswer_Scoring_ExactTextMatch_ScoresFullMarks()
    {
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"EA_Correct_{Guid.NewGuid().ToString("N")[..8]}");

        var question = await _apiClient.DeserializeResponseAsync<QuestionResponse>(
            await _apiClient.PostAsync($"tests/{test.Slug}/questions/",
                new CreateQuestionRequest
                {
                    QuestionText = "What is the capital of France?",
                    QuestionType = "exact_answer",
                    CorrectAnswer = "Paris",
                    Answers = new List<CreateAnswerRequest>()
                }));

        var startResp = await _anonClient.PostAsync($"tests/{test.Slug}/attempts/",
            new StartAttemptRequest { AnonymousName = "ExactMatch" });
        var attempt = await _anonClient.DeserializeResponseAsync<AttemptResponse>(startResp);

        // Submit exact matching text
        await _anonClient.PutAsync($"tests/{test.Slug}/attempts/{attempt!.Id}/",
            new { draft_answers = new Dictionary<string, object> { { question!.Id.ToString(), "Paris" } } });
        await _anonClient.PostAsync($"tests/{test.Slug}/attempts/{attempt.Id}/submit/",
            new { draft_answers = new Dictionary<string, object> { { question.Id.ToString(), "Paris" } } });

        var result = await GetResultAsync(test.Slug, "ExactMatch");
        result.Should().NotBeNull();
        result!.Score.Should().Be(100.0, "because the submitted text matches the correct answer exactly");
        result.CorrectAnswers.Should().Be(1);
    }

    /// <summary>
    /// Verifies that submitting a text answer that does not match the stored correct
    /// answer scores 0%.
    /// </summary>
    [Fact]
    public async Task ExactAnswer_Scoring_WrongText_ScoresZero()
    {
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"EA_Wrong_{Guid.NewGuid().ToString("N")[..8]}");

        var question = await _apiClient.DeserializeResponseAsync<QuestionResponse>(
            await _apiClient.PostAsync($"tests/{test.Slug}/questions/",
                new CreateQuestionRequest
                {
                    QuestionText = "What is the capital of France?",
                    QuestionType = "exact_answer",
                    CorrectAnswer = "Paris",
                    Answers = new List<CreateAnswerRequest>()
                }));

        var startResp = await _anonClient.PostAsync($"tests/{test.Slug}/attempts/",
            new StartAttemptRequest { AnonymousName = "WrongText" });
        var attempt = await _anonClient.DeserializeResponseAsync<AttemptResponse>(startResp);

        await _anonClient.PutAsync($"tests/{test.Slug}/attempts/{attempt!.Id}/",
            new { draft_answers = new Dictionary<string, object> { { question!.Id.ToString(), "London" } } });
        await _anonClient.PostAsync($"tests/{test.Slug}/attempts/{attempt.Id}/submit/",
            new { draft_answers = new Dictionary<string, object> { { question.Id.ToString(), "London" } } });

        var result = await GetResultAsync(test.Slug, "WrongText");
        result.Should().NotBeNull();
        result!.Score.Should().Be(0.0, "because \"London\" does not match the correct answer \"Paris\"");
        result.CorrectAnswers.Should().Be(0);
    }

    /// <summary>
    /// Documents the current case-insensitive matching behaviour: submitting "paris"
    /// scores 100% when the stored correct answer is "Paris". If the API ever enforces
    /// case-sensitive matching, this test will catch the regression.
    /// </summary>
    [Fact]
    public async Task ExactAnswer_Scoring_DifferentCase_ScoresCorrectly()
    {
        // The API performs case-insensitive matching regardless of the case_sensitive field.
        // This test documents the current behaviour so any future change to strict
        // case-sensitive scoring is caught immediately.
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"EA_Case_{Guid.NewGuid().ToString("N")[..8]}");

        var question = await _apiClient.DeserializeResponseAsync<QuestionResponse>(
            await _apiClient.PostAsync($"tests/{test.Slug}/questions/",
                new CreateQuestionRequest
                {
                    QuestionText = "What is the capital of France?",
                    QuestionType = "exact_answer",
                    CorrectAnswer = "Paris",
                    Answers = new List<CreateAnswerRequest>()
                }));

        var startResp = await _anonClient.PostAsync($"tests/{test.Slug}/attempts/",
            new StartAttemptRequest { AnonymousName = "CaseTester" });
        var attempt = await _anonClient.DeserializeResponseAsync<AttemptResponse>(startResp);

        // Submit lowercase version of the correct answer
        await _anonClient.PutAsync($"tests/{test.Slug}/attempts/{attempt!.Id}/",
            new { draft_answers = new Dictionary<string, object> { { question!.Id.ToString(), "paris" } } });
        await _anonClient.PostAsync($"tests/{test.Slug}/attempts/{attempt.Id}/submit/",
            new { draft_answers = new Dictionary<string, object> { { question.Id.ToString(), "paris" } } });

        var result = await GetResultAsync(test.Slug, "CaseTester");
        result.Should().NotBeNull();
        // Document current behavior: scoring is case-insensitive
        result!.Score.Should().Be(100.0,
            "because the API currently performs case-insensitive matching on exact_answer questions");
    }

    /// <summary>
    /// Verifies that submitting no text answer for an exact_answer question scores 0%.
    /// </summary>
    [Fact]
    public async Task ExactAnswer_Scoring_EmptySubmission_ScoresZero()
    {
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"EA_Empty_{Guid.NewGuid().ToString("N")[..8]}");

        var question = await _apiClient.DeserializeResponseAsync<QuestionResponse>(
            await _apiClient.PostAsync($"tests/{test.Slug}/questions/",
                new CreateQuestionRequest
                {
                    QuestionText = "What is the capital of France?",
                    QuestionType = "exact_answer",
                    CorrectAnswer = "Paris",
                    Answers = new List<CreateAnswerRequest>()
                }));

        var startResp = await _anonClient.PostAsync($"tests/{test.Slug}/attempts/",
            new StartAttemptRequest { AnonymousName = "EmptySubmit" });
        var attempt = await _anonClient.DeserializeResponseAsync<AttemptResponse>(startResp);

        // Submit without providing any text answer (leave blank)
        await _anonClient.PostAsync($"tests/{test.Slug}/attempts/{attempt!.Id}/submit/",
            new Dictionary<string, object>());

        var result = await GetResultAsync(test.Slug, "EmptySubmit");
        result.Should().NotBeNull();
        result!.Score.Should().Be(0.0, "because an empty submission should not match any correct answer");
    }

    public void Dispose()
    {
        _apiClient?.Dispose();
        _anonClient?.Dispose();
    }
}
