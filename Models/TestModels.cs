using System.Text.Json.Serialization;

namespace TestIT.ApiTests.Models;

public class CreateTestRequest
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("visibility")]
    public string Visibility { get; set; } = "link_only";

    [JsonPropertyName("time_limit_minutes")]
    public int? TimeLimitMinutes { get; set; }

    [JsonPropertyName("max_attempts")]
    public int MaxAttempts { get; set; } = 1;

    [JsonPropertyName("show_answers_after")]
    public bool ShowAnswersAfter { get; set; }

    [JsonPropertyName("password")]
    public string? Password { get; set; }
}

public class TestResponse
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("slug")]
    public string Slug { get; set; } = string.Empty;

    [JsonPropertyName("visibility")]
    public string Visibility { get; set; } = string.Empty;

    [JsonPropertyName("time_limit_minutes")]
    public int? TimeLimitMinutes { get; set; }

    [JsonPropertyName("max_attempts")]
    public int MaxAttempts { get; set; }

    [JsonPropertyName("show_answers_after")]
    public bool ShowAnswersAfter { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("questions")]
    public List<QuestionResponse>? Questions { get; set; }
}

public class CreateQuestionRequest
{
    [JsonPropertyName("question_text")]
    public string QuestionText { get; set; } = string.Empty;

    [JsonPropertyName("question_type")]
    public string QuestionType { get; set; } = "multiple_choice";

    [JsonPropertyName("answers")]
    public List<CreateAnswerRequest> Answers { get; set; } = new();
}

public class CreateAnswerRequest
{
    [JsonPropertyName("answer_text")]
    public string AnswerText { get; set; } = string.Empty;

    [JsonPropertyName("is_correct")]
    public bool IsCorrect { get; set; }

    [JsonPropertyName("order")]
    public int Order { get; set; }
}

public class QuestionResponse
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("question_text")]
    public string QuestionText { get; set; } = string.Empty;

    [JsonPropertyName("question_type")]
    public string QuestionType { get; set; } = string.Empty;

    [JsonPropertyName("order")]
    public int Order { get; set; }

    [JsonPropertyName("answers")]
    public List<AnswerResponse> Answers { get; set; } = new();
}

public class AnswerResponse
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("answer_text")]
    public string AnswerText { get; set; } = string.Empty;

    [JsonPropertyName("is_correct")]
    public bool IsCorrect { get; set; }

    [JsonPropertyName("order")]
    public int Order { get; set; }
}
