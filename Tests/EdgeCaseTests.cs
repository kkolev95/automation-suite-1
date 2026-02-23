using System.Net;
using FluentAssertions;
using TestIT.ApiTests.Helpers;
using TestIT.ApiTests.Models;
using Xunit;
using Xunit.Abstractions;

namespace TestIT.ApiTests.Tests;

/// <summary>
/// Edge case tests - boundary values, unusual inputs, corner cases
/// Tests scenarios at the extreme operating parameters
/// </summary>
public class EdgeCaseTests : IDisposable
{
    private readonly ApiClient _apiClient;

    public EdgeCaseTests(ITestOutputHelper output)
    {
        ApiClient.SetOutput(output.WriteLine);
        _apiClient = new ApiClient(TestConfiguration.GetBaseUrl());
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // String Boundary Tests
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CreateTest_WhitespaceOnlyTitle_ShouldFail()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);

        var request = new CreateTestRequest
        {
            Title = "   ",  // Only whitespace
            Description = "Test description"
        };

        // Act
        var response = await _apiClient.PostAsync("tests/", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "whitespace-only title should not be allowed");
    }

    [Fact]
    public async Task CreateTest_VeryLongTitle_ShouldHandleGracefully()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);

        var request = new CreateTestRequest
        {
            Title = new string('A', 1000),  // 1000 character title
            Description = "Test description"
        };

        // Act
        var response = await _apiClient.PostAsync("tests/", request);

        // Assert
        // Either accepts it OR returns 400 with clear error message
        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            var content = await response.Content.ReadAsStringAsync();
            content.Should().NotBeNullOrEmpty("error message should be provided");
        }
        else
        {
            response.StatusCode.Should().Be(HttpStatusCode.Created);
        }
    }

    [Fact]
    public async Task CreateTest_SpecialCharactersInTitle_ShouldWork()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);

        var request = new CreateTestRequest
        {
            Title = "Test @#$%^&*()_+-=[]{}|;:',.<>?/~`",
            Description = "Special characters test"
        };

        // Act
        var response = await _apiClient.PostAsync("tests/", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created,
            "special characters in title should be allowed");
    }

    [Fact]
    public async Task CreateTest_UnicodeCharactersInTitle_ShouldWork()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);

        var request = new CreateTestRequest
        {
            Title = "Тест 测试 テスト 🎉🎊",  // Cyrillic, Chinese, Japanese, Emojis
            Description = "Unicode test"
        };

        // Act
        var response = await _apiClient.PostAsync("tests/", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created,
            "unicode characters should be supported");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Numeric Boundary Tests
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CreateTest_ZeroTimeLimit_ShouldBeAllowed()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);

        var request = new CreateTestRequest
        {
            Title = "EdgeCase_ZeroTimeLimit",
            TimeLimitMinutes = 0  // No time limit
        };

        // Act
        var response = await _apiClient.PostAsync("tests/", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created,
            "zero time limit should mean unlimited time");
    }

    [Fact]
    public async Task CreateTest_NegativeTimeLimit_ShouldFail()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);

        var request = new CreateTestRequest
        {
            Title = "EdgeCase_NegativeTimeLimit",
            TimeLimitMinutes = -1
        };

        // Act
        var response = await _apiClient.PostAsync("tests/", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "negative time limit should not be allowed");
    }

    [Fact]
    public async Task CreateTest_VeryLargeTimeLimit_ShouldHandleGracefully()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);

        var request = new CreateTestRequest
        {
            Title = "EdgeCase_LargeTimeLimit",
            TimeLimitMinutes = int.MaxValue  // 2+ billion minutes
        };

        // Act
        var response = await _apiClient.PostAsync("tests/", request);

        // Assert: must accept (any 2xx) or cleanly reject — never a server error
        response.StatusCode.Should().BeOneOf(
            new[] { HttpStatusCode.Created, HttpStatusCode.BadRequest },
            "extremely large time limit should be accepted or cleanly rejected, not cause a server error");
    }

    [Fact]
    public async Task CreateTest_ZeroMaxAttempts_ShouldFail()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);

        var request = new CreateTestRequest
        {
            Title = "EdgeCase_ZeroMaxAttempts",
            MaxAttempts = 0
        };

        // Act
        var response = await _apiClient.PostAsync("tests/", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "zero max attempts means test can't be taken");
    }

    [Fact]
    public async Task CreateTest_NegativeMaxAttempts_ShouldFail()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);

        var request = new CreateTestRequest
        {
            Title = "EdgeCase_NegativeMaxAttempts",
            MaxAttempts = -5
        };

        // Act
        var response = await _apiClient.PostAsync("tests/", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "negative max attempts should not be allowed");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Enum Validation Tests
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CreateTest_InvalidVisibilityValue_ShouldFail()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);

        // Use an anonymous object to bypass the C# model's default visibility value
        var request = new
        {
            title      = "EnumTest_Visibility",
            visibility = "secret"  // Not a valid visibility enum value
        };

        // Act
        var response = await _apiClient.PostAsync("tests/", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "because 'secret' is not a valid visibility value");
    }

    [Fact]
    public async Task CreateQuestion_InvalidQuestionType_ShouldFail()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(
            _apiClient,
            $"EnumTest_QType_{Guid.NewGuid().ToString("N")[..8]}");

        var request = new CreateQuestionRequest
        {
            QuestionText = "Question with invalid type?",
            QuestionType = "essay",  // Not a valid question_type enum value
            Answers = new List<CreateAnswerRequest>
            {
                new() { AnswerText = "Answer", IsCorrect = true, Order = 1 }
            }
        };

        // Act
        var response = await _apiClient.PostAsync($"tests/{test.Slug}/questions/", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "because 'essay' is not a valid question_type value");
    }

    [Fact]
    public async Task CompanyInvite_InvalidRoleValue_ShouldFail()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);

        var companyResp = await _apiClient.PostAsync("companies/",
            new CreateCompanyRequest { Name = $"EnumCorp_{Guid.NewGuid().ToString("N")[..8]}" });
        companyResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var company = await _apiClient.DeserializeResponseAsync<CompanyResponse>(companyResp);

        var (targetEmail, _) = await TestDataHelper.RegisterUserAsync(_apiClient);

        var request = new CreateInviteRequest
        {
            Email = targetEmail,
            Role  = "superuser"  // Not a valid role enum value
        };

        // Act
        var response = await _apiClient.PostAsync($"companies/{company!.Id}/invites/", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "because 'superuser' is not a valid role value");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Null and Missing Value Tests
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CreateTest_NullDescription_ShouldWork()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);

        var request = new CreateTestRequest
        {
            Title = "EdgeCase_NullDescription",
            Description = null  // Null description
        };

        // Act
        var response = await _apiClient.PostAsync("tests/", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created,
            "null description should be optional");
    }

    [Fact]
    public async Task CreateQuestion_EmptyQuestionText_ShouldFail()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient, $"EdgeCase_EmptyQuestion_{Guid.NewGuid().ToString("N")[..8]}");

        var request = new CreateQuestionRequest
        {
            QuestionText = "",  // Empty question
            QuestionType = "multiple_choice",
            Answers = new List<CreateAnswerRequest>
            {
                new() { AnswerText = "Answer", IsCorrect = true, Order = 1 }
            }
        };

        // Act
        var response = await _apiClient.PostAsync($"tests/{test.Slug}/questions/", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "empty question text should not be allowed");
    }

    [Fact]
    public async Task CreateQuestion_NoAnswers_ShouldFail()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient, $"EdgeCase_NoAnswers_{Guid.NewGuid().ToString("N")[..8]}");

        var request = new CreateQuestionRequest
        {
            QuestionText = "Question without answers?",
            QuestionType = "multiple_choice",
            Answers = new List<CreateAnswerRequest>()  // No answers
        };

        // Act
        var response = await _apiClient.PostAsync($"tests/{test.Slug}/questions/", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "questions must have at least one answer");
    }

    [Fact]
    public async Task CreateQuestion_NoCorrectAnswer_ShouldFail()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient, $"EdgeCase_NoCorrectAnswer_{Guid.NewGuid().ToString("N")[..8]}");

        var request = new CreateQuestionRequest
        {
            QuestionText = "Question with no correct answers?",
            QuestionType = "multiple_choice",
            Answers = new List<CreateAnswerRequest>
            {
                new() { AnswerText = "Wrong 1", IsCorrect = false, Order = 1 },
                new() { AnswerText = "Wrong 2", IsCorrect = false, Order = 2 }
            }
        };

        // Act
        var response = await _apiClient.PostAsync($"tests/{test.Slug}/questions/", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "questions must have at least one correct answer");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Slug and URL Edge Cases
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetTest_NonExistentSlug_Returns404()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);

        // Act
        var response = await _apiClient.GetAsync("tests/definitely-does-not-exist-12345/");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "non-existent slug should return 404");
    }

    [Fact]
    public async Task GetTest_InvalidSlugFormat_Returns404()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);

        // Act
        var response = await _apiClient.GetAsync("tests/invalid!!!slug###/");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "invalid slug format should return 404");
    }

    [Fact]
    public async Task GetTest_VeryLongSlug_ShouldHandleGracefully()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);

        var veryLongSlug = new string('a', 500);

        // Act
        var response = await _apiClient.GetAsync($"tests/{veryLongSlug}/");

        // Assert
        var isValid = response.StatusCode == HttpStatusCode.NotFound ||
                     response.StatusCode == HttpStatusCode.BadRequest;
        isValid.Should().BeTrue("very long slug should be handled gracefully");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Password Edge Cases
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CreateTest_EmptyPassword_ShouldWork()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);

        var request = new CreateTestRequest
        {
            Title = "EdgeCase_EmptyPassword",
            Password = ""  // Empty password means no password
        };

        // Act
        var response = await _apiClient.PostAsync("tests/", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created,
            "empty password should mean no password protection");
    }

    [Fact]
    public async Task CreateTest_VeryLongPassword_ShouldHandleGracefully()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);

        var request = new CreateTestRequest
        {
            Title = "EdgeCase_LongPassword",
            Password = new string('x', 500)  // 500 char password
        };

        // Act
        var response = await _apiClient.PostAsync("tests/", request);

        // Assert: must accept (any 2xx) or cleanly reject — never a server error
        response.StatusCode.Should().BeOneOf(
            new[] { HttpStatusCode.Created, HttpStatusCode.BadRequest },
            "extremely long test password should be accepted or cleanly rejected, not cause a server error");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Email Edge Cases
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Register_EmailWithPlus_ShouldWork()
    {
        // Arrange
        var email = $"test+tag{Guid.NewGuid().ToString("N")[..8]}@example.com";

        var request = new RegisterRequest
        {
            Email = email,  // Email with + sign
            Password = "Test123!",
            PasswordConfirm = "Test123!",
            FirstName = "Test",
            LastName = "User"
        };

        // Act
        var response = await _apiClient.PostAsync("auth/register/", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created,
            "email with + sign should be valid");
    }

    [Fact]
    public async Task Register_EmailWithSubdomain_ShouldWork()
    {
        // Arrange
        var email = $"test{Guid.NewGuid().ToString("N")[..8]}@mail.example.com";

        var request = new RegisterRequest
        {
            Email = email,
            Password = "Test123!",
            PasswordConfirm = "Test123!",
            FirstName = "Test",
            LastName = "User"
        };

        // Act
        var response = await _apiClient.PostAsync("auth/register/", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created,
            "email with subdomain should be valid");
    }

    [Fact]
    public async Task Register_EmailWithDots_ShouldWork()
    {
        // Arrange
        var email = $"test.user.name{Guid.NewGuid().ToString("N")[..8]}@example.com";

        var request = new RegisterRequest
        {
            Email = email,  // Email with dots
            Password = "Test123!",
            PasswordConfirm = "Test123!",
            FirstName = "Test",
            LastName = "User"
        };

        // Act
        var response = await _apiClient.PostAsync("auth/register/", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created,
            "email with dots should be valid");
    }

    [Fact]
    public async Task Register_VeryLongEmail_ShouldHandleGracefully()
    {
        // Arrange
        var longLocalPart = new string('a', 200);
        var email = $"{longLocalPart}@example.com";

        var request = new RegisterRequest
        {
            Email = email,
            Password = "Test123!",
            PasswordConfirm = "Test123!",
            FirstName = "Test",
            LastName = "User"
        };

        // Act
        var response = await _apiClient.PostAsync("auth/register/", request);

        // Assert
        // Should either accept or reject with clear limit
        var isValid = response.StatusCode == HttpStatusCode.Created ||
                     response.StatusCode == HttpStatusCode.BadRequest;
        isValid.Should().BeTrue("should handle gracefully");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Attempt Edge Cases
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SaveAnswers_EmptyAnswersList_ShouldWork()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient, $"EdgeCase_EmptyAnswers_{Guid.NewGuid().ToString("N")[..8]}");
        await TestDataHelper.AddQuestionAsync(_apiClient, test.Slug, "Question?");

        using var testerClient = new ApiClient(TestConfiguration.GetBaseUrl());
        var attemptResp = await testerClient.PostAsync<object, AttemptResponse>(
            $"tests/{test.Slug}/attempts/",
            new { anonymous_name = "Tester" });
        var attempt = attemptResp!;

        // Act - save empty answers using correct endpoint
        var response = await testerClient.PutAsync($"tests/{test.Slug}/attempts/{attempt.Id}/",
            new { draft_answers = new Dictionary<string, List<int>>() });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "saving empty answers (skipping all) should be allowed");
    }

    [Fact]
    public async Task SubmitAttempt_NoAnswersSaved_ShouldScoreZero()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient, $"EdgeCase_NoAnswersSubmit_{Guid.NewGuid().ToString("N")[..8]}");
        await TestDataHelper.AddQuestionAsync(_apiClient, test.Slug, "Question?",
            answers: new List<CreateAnswerRequest>
            {
                new() { AnswerText = "Correct", IsCorrect = true, Order = 1 },
                new() { AnswerText = "Wrong", IsCorrect = false, Order = 2 }
            });

        using var testerClient = new ApiClient(TestConfiguration.GetBaseUrl());
        var attemptResp = await testerClient.PostAsync<object, AttemptResponse>(
            $"tests/{test.Slug}/attempts/",
            new { anonymous_name = "Tester" });
        var attempt = attemptResp!;

        // Act - submit without saving any answers
        var submitResponse = await testerClient.PostAsync($"tests/{test.Slug}/attempts/{attempt.Id}/submit/", new { });

        // Assert
        submitResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var results = await _apiClient.GetAsync<List<ResultResponse>>($"tests/{test.Slug}/results/");
        results.Should().NotBeNull();
        results!.First().Score.Should().Be(0.0,
            "submitting without answers should score 0%");
    }

    public void Dispose()
    {
        _apiClient?.Dispose();
    }
}
