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

        // Assert: authenticated User B must get 403 Forbidden.
        // 404 would hide the resource and could mask a missing auth check — the test exists,
        // so the correct response for an unauthorised authenticated request is 403.
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            $"because User B should not access User A's private test. Response: {body}");
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

        // Assert: authenticated non-author must get 403 Forbidden, not 404.
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "because only the test author should access results — non-author must get 403 Forbidden");
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

        // Assert: authenticated non-owner must get 403 Forbidden, not 404.
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "because authenticated non-owner must receive 403 Forbidden when updating another user's test");
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

        // Assert: authenticated non-owner must get 403 Forbidden, not 404.
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "because authenticated non-owner must receive 403 Forbidden when deleting another user's test");
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

        // Assert: authenticated non-member must get 403 Forbidden.
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "because authenticated non-member must receive 403 Forbidden when accessing another company's data");
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

        // Assert: SQL injection payloads must be safely stored by the ORM's parameterised queries,
        // NOT rejected at the application layer. A 400 here would indicate the API is using
        // string sanitisation instead of parameterised queries — which is a security antipattern
        // that creates a false sense of security.
        // If this assertion fails (400 returned), the API is blocking SQL meta-characters rather
        // than parameterising them — that is a design flaw worth surfacing.
        response.StatusCode.Should().Be(HttpStatusCode.Created,
            "because SQL injection strings must be stored safely via parameterised queries, not rejected");
        var test = await _apiClient.DeserializeResponseAsync<TestResponse>(response);
        test!.Title.Should().Be(maliciousTitle,
            "because the SQL injection payload must be stored literally — parameterised queries must not modify it");
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

        // Assert: XSS payloads must be stored as-is. The API is a JSON REST backend —
        // HTML escaping is the responsibility of the frontend renderer, not the data store.
        // If 400 is returned, the API is incorrectly blocking HTML characters as security theatre,
        // which would prevent legitimate use of markup in question text (e.g., code snippets).
        response.StatusCode.Should().Be(HttpStatusCode.Created,
            "because the API must store XSS payloads as-is and rely on the frontend to escape on render");
        var question = await _apiClient.DeserializeResponseAsync<QuestionResponse>(response);
        question!.QuestionText.Should().Contain("script",
            "because the XSS payload must be stored literally without server-side modification");
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

        // Assert: 413 Request Entity Too Large (from the web server/reverse proxy) or
        // 400 Bad Request (from Django field length validation) — both are correct rejections.
        // If 201 is returned, the API accepts a 1MB description, which is a DoS vector.
        response.StatusCode.Should().BeOneOf(
            new[] { HttpStatusCode.RequestEntityTooLarge, HttpStatusCode.BadRequest },
            "because a 1MB payload must be rejected by the server or application layer to prevent DoS");
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
