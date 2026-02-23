using System.Text.Json.Serialization;

namespace TestIT.ApiTests.Models;

// --- Question Management ---

public class UpdateQuestionRequest
{
    [JsonPropertyName("question_text")]
    public string QuestionText { get; set; } = string.Empty;

    [JsonPropertyName("question_type")]
    public string QuestionType { get; set; } = "multiple_choice";

    [JsonPropertyName("answers")]
    public List<CreateAnswerRequest> Answers { get; set; } = new();
}

public class ReorderQuestionsRequest
{
    [JsonPropertyName("order")]
    public List<OrderItem> Order { get; set; } = new();
}

public class OrderItem
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("order")]
    public int Order { get; set; }
}

// --- Test Taking ---

public class VerifyPasswordRequest
{
    [JsonPropertyName("password")]
    public string Password { get; set; } = string.Empty;
}

public class VerifyPasswordResponse
{
    [JsonPropertyName("verified")]
    public bool Verified { get; set; }

    [JsonPropertyName("token")]
    public string Token { get; set; } = string.Empty;
}

public class StartAttemptRequest
{
    [JsonPropertyName("anonymous_name")]
    public string? AnonymousName { get; set; }
}

public class AttemptResponse
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("started_at")]
    public string StartedAt { get; set; } = string.Empty;
}

public class SaveDraftRequest
{
    [JsonPropertyName("draft_answers")]
    public Dictionary<string, List<int>> DraftAnswers { get; set; } = new();
}

// --- Take Test Response (no correct-answer flags) ---

public class TakeTestResponse
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("slug")]
    public string Slug { get; set; } = string.Empty;

    [JsonPropertyName("questions")]
    public List<TakeQuestionResponse> Questions { get; set; } = new();

    [JsonPropertyName("requires_password")]
    public bool RequiresPassword { get; set; }
}

public class TakeQuestionResponse
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("question_text")]
    public string QuestionText { get; set; } = string.Empty;

    [JsonPropertyName("question_type")]
    public string QuestionType { get; set; } = string.Empty;

    [JsonPropertyName("answers")]
    public List<TakeAnswerResponse> Answers { get; set; } = new();
}

public class TakeAnswerResponse
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("answer_text")]
    public string AnswerText { get; set; } = string.Empty;

    // Present in response but should always be false on the /take/ endpoint
    [JsonPropertyName("is_correct")]
    public bool IsCorrect { get; set; }
}

public class PasswordRequiredResponse
{
    [JsonPropertyName("requires_password")]
    public bool RequiresPassword { get; set; }
}

// --- Results ---

public class DetailedResultResponse
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("score")]
    public double? Score { get; set; }

    [JsonPropertyName("correct_answers")]
    public int? CorrectAnswers { get; set; }

    [JsonPropertyName("total_questions")]
    public int? TotalQuestions { get; set; }

    [JsonPropertyName("anonymous_name")]
    public string? AnonymousName { get; set; }

    [JsonPropertyName("submitted_at")]
    public string? SubmittedAt { get; set; }

    [JsonPropertyName("answer_detail")]
    public List<AnswerDetailResponse>? AnswerDetail { get; set; }
}

public class AnswerDetailResponse
{
    [JsonPropertyName("question_id")]
    public int QuestionId { get; set; }

    [JsonPropertyName("question_text")]
    public string QuestionText { get; set; } = string.Empty;

    [JsonPropertyName("is_correct")]
    public bool IsCorrect { get; set; }

    [JsonPropertyName("submitted_answer_ids")]
    public List<int> SubmittedAnswerIds { get; set; } = new();

    [JsonPropertyName("correct_answer_ids")]
    public List<int> CorrectAnswerIds { get; set; } = new();
}

public class ResultResponse
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("user_name")]
    public string? UserName { get; set; }

    [JsonPropertyName("user_email")]
    public string? UserEmail { get; set; }

    [JsonPropertyName("anonymous_name")]
    public string? AnonymousName { get; set; }

    [JsonPropertyName("score")]
    public double? Score { get; set; }

    [JsonPropertyName("correct_answers")]
    public int? CorrectAnswers { get; set; }

    [JsonPropertyName("total_questions")]
    public int? TotalQuestions { get; set; }

    [JsonPropertyName("attempt_number")]
    public int? AttemptNumber { get; set; }

    [JsonPropertyName("submitted_at")]
    public string? SubmittedAt { get; set; }
}

// --- Analytics ---

public class AnalyticsResponse
{
    [JsonPropertyName("total_attempts")]
    public int TotalAttempts { get; set; }

    [JsonPropertyName("average_score")]
    public double? AverageScore { get; set; }

    [JsonPropertyName("pass_rate")]
    public double? PassRate { get; set; }

    [JsonPropertyName("completion_rate")]
    public double? CompletionRate { get; set; }

    [JsonPropertyName("question_stats")]
    public List<QuestionStatsResponse> QuestionStats { get; set; } = new();
}

public class QuestionStatsResponse
{
    [JsonPropertyName("question_id")]
    public int QuestionId { get; set; }

    [JsonPropertyName("question_text")]
    public string QuestionText { get; set; } = string.Empty;

    [JsonPropertyName("total_answered")]
    public int TotalAnswered { get; set; }

    [JsonPropertyName("correct_count")]
    public int CorrectCount { get; set; }

    [JsonPropertyName("difficulty")]
    public double Difficulty { get; set; }

    [JsonPropertyName("answer_distribution")]
    public List<AnswerDistributionResponse> AnswerDistribution { get; set; } = new();
}

public class AnswerDistributionResponse
{
    [JsonPropertyName("answer_id")]
    public int AnswerId { get; set; }

    [JsonPropertyName("answer_text")]
    public string AnswerText { get; set; } = string.Empty;

    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("is_correct")]
    public bool IsCorrect { get; set; }
}
