using TestIT.ApiTests.Models;

namespace TestIT.ApiTests.Helpers;

/// <summary>
/// Shared helpers to reduce boilerplate across test classes.
/// </summary>
public static class TestDataHelper
{
    /// <summary>
    /// Registers a fresh user, logs in, sets the token on the client, and returns the access token.
    /// Uses TestAccountManager for automatic cleanup tracking.
    /// </summary>
    public static async Task<string> RegisterAndLoginAsync(ApiClient client, bool autoCleanup = true)
    {
        var (email, password, token) = await TestAccountManager.CreateAndTrackAccountAsync(
            client,
            emailPrefix: "testuser",
            autoCleanup: autoCleanup);

        return token;
    }

    /// <summary>
    /// Registers a fresh user (does NOT log in or set a token). Returns the email and password.
    /// Uses TestAccountManager for automatic cleanup tracking.
    /// </summary>
    public static async Task<(string Email, string Password)> RegisterUserAsync(ApiClient client, bool autoCleanup = true)
    {
        // Create a temporary client for registration without affecting the passed client's auth state
        using var tempClient = new ApiClient(TestConfiguration.GetBaseUrl());
        var (email, password, _) = await TestAccountManager.CreateAndTrackAccountAsync(
            tempClient,
            emailPrefix: "testuser",
            autoCleanup: autoCleanup);

        return (email, password);
    }

    /// <summary>
    /// Creates a test via the authenticated client and returns the deserialized TestResponse.
    /// Client must already have a valid auth token set.
    /// </summary>
    public static async Task<TestResponse> CreateTestAsync(
        ApiClient client,
        string title,
        string? description = null,
        string visibility = "link_only",
        int? timeLimitMinutes = null,
        int maxAttempts = 1,
        bool showAnswersAfter = false,
        string? password = null)
    {
        var response = await client.PostAsync("tests/", new CreateTestRequest
        {
            Title = title,
            Description = description,
            Visibility = visibility,
            TimeLimitMinutes = timeLimitMinutes,
            MaxAttempts = maxAttempts,
            ShowAnswersAfter = showAnswersAfter,
            Password = password
        });

        return (await client.DeserializeResponseAsync<TestResponse>(response))!;
    }

    /// <summary>
    /// Adds a question to a test and returns the deserialized QuestionResponse.
    /// Client must already have a valid auth token set.
    /// </summary>
    public static async Task<QuestionResponse> AddQuestionAsync(
        ApiClient client,
        string testSlug,
        string questionText,
        string questionType = "multiple_choice",
        List<CreateAnswerRequest>? answers = null)
    {
        answers ??= new List<CreateAnswerRequest>
        {
            new() { AnswerText = "Option A", IsCorrect = true, Order = 1 },
            new() { AnswerText = "Option B", IsCorrect = false, Order = 2 },
            new() { AnswerText = "Option C", IsCorrect = false, Order = 3 }
        };

        var response = await client.PostAsync($"tests/{testSlug}/questions/", new CreateQuestionRequest
        {
            QuestionText = questionText,
            QuestionType = questionType,
            Answers = answers
        });

        return (await client.DeserializeResponseAsync<QuestionResponse>(response))!;
    }
}
