using System.Net;
using System.Text;
using FluentAssertions;
using TestIT.ApiTests.Helpers;
using TestIT.ApiTests.Models;
using Xunit;

namespace TestIT.ApiTests.Tests;

public class SecurityTests : IDisposable
{
    private readonly ApiClient _apiClient;
    private readonly ApiClient _attackerClient;

    public SecurityTests(ITestOutputHelper output)
    {
        ApiClient.SetOutput(output.WriteLine);
        var baseUrl = TestConfiguration.GetBaseUrl();
        _apiClient = new ApiClient(baseUrl);
        _attackerClient = new ApiClient(baseUrl);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Authorization Tests (Horizontal Privilege Escalation)
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Authorization_UserCannotAccessOtherUsersTests()
    {
        // Arrange: User A creates a test
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var testA = await TestDataHelper.CreateTestAsync(_apiClient,
            $"UserATest_{Guid.NewGuid().ToString("N")[..8]}",
            visibility: "link_only");

        // User B registers and tries to access User A's test
        await TestDataHelper.RegisterAndLoginAsync(_attackerClient);

        // Act: User B tries to view User A's test details
        var response = await _attackerClient.GetAsync($"tests/{testA.Slug}/");
        var body = await _attackerClient.GetResponseBodyAsync(response);

        // Assert: Should be denied (403) or hidden (404)
        (response.StatusCode == HttpStatusCode.Forbidden || response.StatusCode == HttpStatusCode.NotFound)
            .Should().BeTrue($"because User B should not access User A's private test. Response: {body}");
    }

    [Fact]
    public async Task Authorization_UserCannotAccessOtherUsersTestResults()
    {
        // Arrange: User A creates test with submission
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"ResultsSec_{Guid.NewGuid().ToString("N")[..8]}");
        await TestDataHelper.AddQuestionAsync(_apiClient, test.Slug, "Q1");

        // Create an attempt (as anonymous user for simplicity)
        var anonClient = new ApiClient(TestConfiguration.GetBaseUrl());
        var startResp = await anonClient.PostAsync($"tests/{test.Slug}/attempts/",
            new StartAttemptRequest { AnonymousName = "Test User" });
        var attempt = await anonClient.DeserializeResponseAsync<AttemptResponse>(startResp);
        await anonClient.PostAsync($"tests/{test.Slug}/attempts/{attempt!.Id}/submit/",
            new Dictionary<string, object>());

        // User B tries to access User A's results
        await TestDataHelper.RegisterAndLoginAsync(_attackerClient);

        // Act: User B attempts to fetch results
        var response = await _attackerClient.GetAsync($"tests/{test.Slug}/results/");

        // Assert: Should be denied
        (response.StatusCode == HttpStatusCode.Forbidden || response.StatusCode == HttpStatusCode.NotFound)
            .Should().BeTrue("because only test author should access results");
    }

    [Fact]
    public async Task Authorization_UserCannotUpdateOtherUsersTests()
    {
        // Arrange: User A creates a test
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"UpdateSec_{Guid.NewGuid().ToString("N")[..8]}");

        // User B tries to modify it
        await TestDataHelper.RegisterAndLoginAsync(_attackerClient);

        var updateRequest = new CreateTestRequest
        {
            Title = "Hijacked Title",
            Description = "User B modified this"
        };

        // Act: User B attempts to update User A's test
        var response = await _attackerClient.PutAsync($"tests/{test.Slug}/", updateRequest);

        // Assert: Should be denied
        (response.StatusCode == HttpStatusCode.Forbidden || response.StatusCode == HttpStatusCode.NotFound)
            .Should().BeTrue("because users should not modify others' tests");
    }

    [Fact]
    public async Task Authorization_UserCannotDeleteOtherUsersTests()
    {
        // Arrange: User A creates a test
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"DeleteSec_{Guid.NewGuid().ToString("N")[..8]}");

        // User B tries to delete it
        await TestDataHelper.RegisterAndLoginAsync(_attackerClient);

        // Act: User B attempts to delete User A's test
        var response = await _attackerClient.DeleteAsync($"tests/{test.Slug}/");

        // Assert: Should be denied
        (response.StatusCode == HttpStatusCode.Forbidden || response.StatusCode == HttpStatusCode.NotFound)
            .Should().BeTrue("because users should not delete others' tests");
    }

    [Fact]
    public async Task Authorization_UserCannotAccessOtherCompaniesData()
    {
        // Arrange: User A creates a company
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var companyAResp = await _apiClient.PostAsync("companies/",
            new CreateCompanyRequest { Name = $"CompanyA_{Guid.NewGuid().ToString("N")[..8]}" });

        // API now returns full object with ID in create response
        var companyA = await _apiClient.DeserializeResponseAsync<CompanyResponse>(companyAResp);
        companyA.Should().NotBeNull();
        companyA!.Id.Should().BePositive("API should return ID in create response");

        // User B creates their own company
        await TestDataHelper.RegisterAndLoginAsync(_attackerClient);
        await _attackerClient.PostAsync("companies/",
            new CreateCompanyRequest { Name = $"CompanyB_{Guid.NewGuid().ToString("N")[..8]}" });

        // Act: User B tries to access Company A's members
        var response = await _attackerClient.GetAsync($"companies/{companyA.Id}/members/");

        // Assert: Should be denied
        (response.StatusCode == HttpStatusCode.Forbidden || response.StatusCode == HttpStatusCode.NotFound)
            .Should().BeTrue("because users should not access other companies' member data");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Authentication & Token Security
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Authentication_InvalidToken_DeniesAccess()
    {
        // Act: Use a completely invalid token
        _apiClient.SetAuthToken("invalid.token.here");
        var response = await _apiClient.GetAsync("tests/");

        // Assert: Should be unauthorized
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "because invalid tokens should be rejected");
    }

    [Fact]
    public async Task Authentication_MalformedToken_DeniesAccess()
    {
        // Act: Use a malformed JWT (not properly formatted)
        _apiClient.SetAuthToken("not-even-a-jwt-format");
        var response = await _apiClient.GetAsync("tests/");

        // Assert: Should be unauthorized
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "because malformed tokens should be rejected");
    }

    [Fact]
    public async Task Authentication_NoToken_DeniesProtectedEndpoints()
    {
        // Act: Attempt to access protected endpoint without auth
        _apiClient.ClearAuthToken();
        var response = await _apiClient.GetAsync("tests/");

        // Assert: Should be unauthorized
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "because protected endpoints require authentication");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Input Validation & Injection Attacks
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task InputValidation_SQLInjectionInTestTitle_IsSanitized()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);

        var maliciousTitle = "Test'; DROP TABLE tests; --";
        var createRequest = new CreateTestRequest
        {
            Title = maliciousTitle,
            Description = "Testing SQL injection"
        };

        // Act: Create test with SQL injection attempt
        var response = await _apiClient.PostAsync("tests/", createRequest);

        // Assert: Either rejected or safely stored
        if (response.StatusCode == HttpStatusCode.Created)
        {
            var test = await _apiClient.DeserializeResponseAsync<TestResponse>(response);
            test!.Title.Should().Be(maliciousTitle,
                "because SQL injection should be escaped/parameterized, not rejected");
        }
        else
        {
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
                "because dangerous characters might be rejected");
        }
    }

    [Fact]
    public async Task InputValidation_XSSInQuestionText_IsSanitized()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"XSSSec_{Guid.NewGuid().ToString("N")[..8]}");

        var xssPayload = "<script>alert('XSS')</script>";
        var questionRequest = new CreateQuestionRequest
        {
            QuestionText = xssPayload,
            QuestionType = "multiple_choice",
            Answers = new List<CreateAnswerRequest>
            {
                new() { AnswerText = "A", IsCorrect = true, Order = 1 }
            }
        };

        // Act: Create question with XSS payload
        var response = await _apiClient.PostAsync($"tests/{test.Slug}/questions/", questionRequest);

        // Assert: Should either reject or escape the payload
        if (response.StatusCode == HttpStatusCode.Created)
        {
            var question = await _apiClient.DeserializeResponseAsync<QuestionResponse>(response);
            // The payload should be stored but will be escaped on render (client-side responsibility)
            question!.QuestionText.Should().Contain("script",
                "because API should accept HTML tags (frontend must escape on render)");
        }
        else
        {
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
                "because dangerous HTML might be rejected");
        }
    }

    [Fact]
    public async Task InputValidation_OversizedPayload_IsRejected()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);

        // Create an extremely large description (e.g., 1MB of text)
        var hugeDescription = new string('A', 1024 * 1024); // 1MB
        var createRequest = new CreateTestRequest
        {
            Title = "Oversized Test",
            Description = hugeDescription
        };

        // Act: Attempt to create test with huge payload
        var response = await _apiClient.PostAsync("tests/", createRequest);

        // Assert: Should be rejected (413 Payload Too Large or 400 Bad Request)
        ((int)response.StatusCode == 413 || response.StatusCode == HttpStatusCode.BadRequest)
            .Should().BeTrue("because oversized payloads should be rejected to prevent DoS");
    }

    [Fact]
    public async Task InputValidation_NegativeMaxAttempts_IsRejected()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);

        var createRequest = new CreateTestRequest
        {
            Title = "Negative Test",
            Description = "Test with negative max attempts",
            MaxAttempts = -5
        };

        // Act: Create test with invalid negative value
        var response = await _apiClient.PostAsync("tests/", createRequest);

        // Assert: Should be rejected
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "because negative max attempts is invalid");
    }

    [Fact]
    public async Task InputValidation_ExtremelyLongEmail_IsRejected()
    {
        // Arrange: Create an email with 500 characters
        var longEmail = new string('a', 500) + "@example.com";
        var registerRequest = new RegisterRequest
        {
            Email = longEmail,
            Password = "Password123!",
            PasswordConfirm = "Password123!",
            FirstName = "Test",
            LastName = "User"
        };

        // Act: Attempt registration with extremely long email
        var response = await _apiClient.PostAsync("auth/register/", registerRequest);

        // Assert: Should be rejected
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "because email length should be limited");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Data Exposure & Information Leakage
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task DataExposure_CorrectAnswers_NotExposedBeforeSubmission()
    {
        // Arrange: Create a test with questions
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"AnswerSec_{Guid.NewGuid().ToString("N")[..8]}");

        var question = await TestDataHelper.AddQuestionAsync(_apiClient, test.Slug, "What is 2+2?",
            answers: new List<CreateAnswerRequest>
            {
                new() { AnswerText = "3", IsCorrect = false, Order = 1 },
                new() { AnswerText = "4", IsCorrect = true, Order = 2 },
                new() { AnswerText = "5", IsCorrect = false, Order = 3 }
            });

        // Act: Fetch test for taking (anonymous user)
        using var anonClient = new ApiClient(TestConfiguration.GetBaseUrl());
        var response = await anonClient.GetAsync($"tests/{test.Slug}/take/");
        var takeTest = await anonClient.DeserializeResponseAsync<TakeTestResponse>(response);

        // Assert: Correct answers should NOT be exposed
        takeTest!.Questions.Should().NotBeEmpty();
        var fetchedQuestion = takeTest.Questions.First();

        // Verify the take endpoint actually returned the answers — an empty list would
        // mean the loop below never executes and the assertion trivially passes even if
        // the API started leaking the is_correct field.
        fetchedQuestion.Answers.Should().HaveCount(3,
            "the take endpoint must return all 3 answers that were created for the question");

        foreach (var answer in fetchedQuestion.Answers)
        {
            answer.IsCorrect.Should().BeFalse(
                "because correct answers should not be revealed before test submission");
        }
    }

    [Fact]
    public async Task DataExposure_PasswordNotReturned_InProfileEndpoint()
    {
        // Arrange: Register and login
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);

        // Act: Fetch user profile
        var response = await _apiClient.GetAsync("auth/me/");
        var body = await _apiClient.GetResponseBodyAsync(response);

        // Assert: Response should not contain password field
        body.ToLower().Should().NotContain("password")
            .And.NotContain("passwordhash")
            .And.NotContain("pwd",
                "because passwords should never be returned in responses");
    }

    [Fact]
    public async Task DataExposure_DetailedErrorMessages_DoNotLeakSensitiveInfo()
    {
        // Act: Attempt login with non-existent user
        var loginRequest = new LoginRequest
        {
            Email = "nonexistent@example.com",
            Password = "WrongPassword123!"
        };

        var response = await _apiClient.PostAsync("auth/login/", loginRequest);
        var body = await _apiClient.GetResponseBodyAsync(response);

        // Assert: Error message should be generic, not "user not found" vs "wrong password"
        body.ToLower().Should().NotContain("not found",
            "because error messages shouldn't distinguish 'user not found' from 'wrong password' (account enumeration)")
            .And.NotContain("does not exist");
    }

    public void Dispose()
    {
        _apiClient?.Dispose();
        _attackerClient?.Dispose();
    }
}
