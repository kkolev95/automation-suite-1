using TestIT.ApiTests.Models;

namespace TestIT.ApiTests.Helpers;

/// <summary>
/// Fluent builder for CreateTestRequest with sensible defaults.
/// </summary>
public class TestBuilder
{
    private string _title = $"Test_{Guid.NewGuid().ToString("N")[..8]}";
    private string? _description;
    private string _visibility = "link_only";
    private int? _timeLimitMinutes;
    private int _maxAttempts = 10;
    private bool _showAnswersAfter = true;

    public static TestBuilder Default() => new();

    public TestBuilder WithTitle(string title)
    {
        _title = title;
        return this;
    }

    public TestBuilder WithDescription(string description)
    {
        _description = description;
        return this;
    }

    public TestBuilder Public()
    {
        _visibility = "public";
        return this;
    }

    public TestBuilder Private()
    {
        _visibility = "private";
        return this;
    }

    public TestBuilder LinkOnly()
    {
        _visibility = "link_only";
        return this;
    }

    public TestBuilder PasswordProtected(string password)
    {
        _visibility = "password_protected";
        // Note: password would need to be set separately via the test flow
        return this;
    }

    public TestBuilder WithTimeLimit(int minutes)
    {
        _timeLimitMinutes = minutes;
        return this;
    }

    public TestBuilder WithMaxAttempts(int attempts)
    {
        _maxAttempts = attempts;
        return this;
    }

    public TestBuilder ShowAnswers()
    {
        _showAnswersAfter = true;
        return this;
    }

    public TestBuilder HideAnswers()
    {
        _showAnswersAfter = false;
        return this;
    }

    public CreateTestRequest Build()
    {
        return new CreateTestRequest
        {
            Title = _title,
            Description = _description,
            Visibility = _visibility,
            TimeLimitMinutes = _timeLimitMinutes,
            MaxAttempts = _maxAttempts,
            ShowAnswersAfter = _showAnswersAfter
        };
    }
}

/// <summary>
/// Fluent builder for CreateQuestionRequest with sensible defaults.
/// </summary>
public class QuestionBuilder
{
    private string _questionText = "Sample question";
    private string _questionType = "multiple_choice";
    private readonly List<CreateAnswerRequest> _answers = new();

    public static QuestionBuilder Default() => new();

    public QuestionBuilder WithText(string text)
    {
        _questionText = text;
        return this;
    }

    public QuestionBuilder MultipleChoice()
    {
        _questionType = "multiple_choice";
        return this;
    }

    public QuestionBuilder MultiSelect()
    {
        _questionType = "multi_select";
        return this;
    }

    public QuestionBuilder WithAnswer(string text, bool isCorrect, int order = 1)
    {
        _answers.Add(new CreateAnswerRequest
        {
            AnswerText = text,
            IsCorrect = isCorrect,
            Order = order
        });
        return this;
    }

    public QuestionBuilder WithCorrectAnswer(string text, int order = 1)
    {
        return WithAnswer(text, isCorrect: true, order);
    }

    public QuestionBuilder WithWrongAnswer(string text, int order = 2)
    {
        return WithAnswer(text, isCorrect: false, order);
    }

    /// <summary>
    /// Adds default answers (one correct, two wrong) for a basic multiple choice question.
    /// </summary>
    public QuestionBuilder WithDefaultAnswers()
    {
        _answers.Clear();
        _answers.Add(new CreateAnswerRequest { AnswerText = "Correct Answer", IsCorrect = true, Order = 1 });
        _answers.Add(new CreateAnswerRequest { AnswerText = "Wrong Answer 1", IsCorrect = false, Order = 2 });
        _answers.Add(new CreateAnswerRequest { AnswerText = "Wrong Answer 2", IsCorrect = false, Order = 3 });
        return this;
    }

    public CreateQuestionRequest Build()
    {
        // If no answers were added, provide defaults
        if (_answers.Count == 0)
        {
            WithDefaultAnswers();
        }

        return new CreateQuestionRequest
        {
            QuestionText = _questionText,
            QuestionType = _questionType,
            Answers = _answers
        };
    }
}

/// <summary>
/// Fluent builder for RegisterRequest with sensible defaults.
/// </summary>
public class UserBuilder
{
    private string _email = $"user_{Guid.NewGuid().ToString("N")[..8]}@example.com";
    private string _password = "SecurePass123!";
    private string _firstName = "Test";
    private string _lastName = "User";

    public static UserBuilder Default() => new();

    public UserBuilder WithEmail(string email)
    {
        _email = email;
        return this;
    }

    public UserBuilder WithPassword(string password)
    {
        _password = password;
        return this;
    }

    public UserBuilder WithName(string firstName, string lastName)
    {
        _firstName = firstName;
        _lastName = lastName;
        return this;
    }

    public RegisterRequest Build()
    {
        return new RegisterRequest
        {
            Email = _email,
            Password = _password,
            PasswordConfirm = _password,
            FirstName = _firstName,
            LastName = _lastName
        };
    }
}
