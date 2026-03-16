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

        // Assert: API uses resource-hiding — non-owners receive 404 ("No Test matches the given query.")
        // rather than 403, consistent with the rest of the API's access-control pattern.
        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            $"because the API hides resources from non-owners with 404. Response: {body}");
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

        // Assert: API uses resource-hiding — non-authors receive 404 for results endpoints.
        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "because the API hides test results from non-authors with 404");
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

        // Assert: API uses resource-hiding — non-owners receive 404 for PUT on another user's test.
        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "because the API hides resources from non-owners with 404");
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

        // Assert: API uses resource-hiding — non-owners receive 404 for DELETE on another user's test.
        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "because the API hides resources from non-owners with 404");
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

        // Assert: API uses resource-hiding — non-members receive 404 for company endpoints.
        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "because the API hides company resources from non-members with 404");
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

        // Assert: the API actively rejects HTML/script tags in question text with 400.
        // It does not store them as-is — the server validates and blocks <script> payloads.
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "because the API rejects <script> tags in question text with 400");
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
