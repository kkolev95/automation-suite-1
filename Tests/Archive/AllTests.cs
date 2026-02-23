using System.Net;
using FluentAssertions;
using TestIT.ApiTests.Helpers;
using TestIT.ApiTests.Models;
using Xunit;

namespace TestIT.ApiTests.Tests;

public class AuthenticationTests : IDisposable
{
    private readonly ApiClient _apiClient;
    private readonly string _baseUrl;

    public AuthenticationTests()
    {
        _baseUrl = TestConfiguration.GetBaseUrl();
        _apiClient = new ApiClient(_baseUrl);
    }

    [Fact]
    public async Task Registration_WithValidCredentials_CreatesUserAccount()
    {
        // Arrange
        var uniqueEmail = $"testuser_{Guid.NewGuid().ToString("N").Substring(0, 8)}@example.com";
        var registerRequest = new RegisterRequest
        {
            Email = uniqueEmail,
            Password = "SecurePass123!",
            PasswordConfirm = "SecurePass123!",
            FirstName = "John",
            LastName = "Doe"
        };

        // Act
        var response = await _apiClient.PostAsync("auth/register/", registerRequest);
        var body = await _apiClient.GetResponseBodyAsync(response);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created, $"because registration should succeed. Response: {body}");

        var registerResponse = await _apiClient.DeserializeResponseAsync<RegisterResponse>(response);
        registerResponse.Should().NotBeNull();
        registerResponse!.Email.Should().Be(uniqueEmail);
        registerResponse.FirstName.Should().Be("John");
        registerResponse.LastName.Should().Be("Doe");
    }

    [Fact]
    public async Task Registration_WithMismatchedPasswords_RejectsRequest()
    {
        // Arrange
        var uniqueEmail = $"testuser_{Guid.NewGuid().ToString("N").Substring(0, 8)}@example.com";
        var registerRequest = new RegisterRequest
        {
            Email = uniqueEmail,
            Password = "SecurePass123!",
            PasswordConfirm = "DifferentPass123!",
            FirstName = "John",
            LastName = "Doe"
        };

        // Act
        var response = await _apiClient.PostAsync("auth/register/", registerRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, "because passwords don't match");
    }

    [Fact]
    public async Task Registration_WithWeakPassword_RejectsRequest()
    {
        // Arrange
        var uniqueEmail = $"testuser_{Guid.NewGuid().ToString("N").Substring(0, 8)}@example.com";
        var registerRequest = new RegisterRequest
        {
            Email = uniqueEmail,
            Password = "weak",
            PasswordConfirm = "weak",
            FirstName = "John",
            LastName = "Doe"
        };

        // Act
        var response = await _apiClient.PostAsync("auth/register/", registerRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, "because password is too weak");
    }

    [Fact]
    public async Task Registration_WithMissingRequiredFields_RejectsRequest()
    {
        // Arrange
        var registerRequest = new RegisterRequest
        {
            Email = "incomplete@example.com",
            Password = "SecurePass123!",
            PasswordConfirm = "SecurePass123!"
            // Missing FirstName and LastName
        };

        // Act
        var response = await _apiClient.PostAsync("auth/register/", registerRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, "because required fields are missing");
    }

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsAuthenticationTokens()
    {
        // Arrange
        // First register a user
        var uniqueEmail = $"testuser_{Guid.NewGuid().ToString("N").Substring(0, 8)}@example.com";
        var password = "SecurePass123!";
        var registerRequest = new RegisterRequest
        {
            Email = uniqueEmail,
            Password = password,
            PasswordConfirm = password,
            FirstName = "Test",
            LastName = "User"
        };
        await _apiClient.PostAsync("auth/register/", registerRequest);

        var loginRequest = new LoginRequest
        {
            Email = uniqueEmail,
            Password = password
        };

        // Act
        var response = await _apiClient.PostAsync("auth/login/", loginRequest);
        var body = await _apiClient.GetResponseBodyAsync(response);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK, $"because login should succeed with valid credentials. Response: {body}");

        var loginResponse = await _apiClient.DeserializeResponseAsync<LoginResponse>(response);
        loginResponse.Should().NotBeNull();
        loginResponse!.AccessToken.Should().NotBeNullOrEmpty("because access token should be provided");
        loginResponse.RefreshToken.Should().NotBeNullOrEmpty("because refresh token should be provided");
    }

    [Fact]
    public async Task Login_WithInvalidCredentials_DeniesAccess()
    {
        // Arrange
        var loginRequest = new LoginRequest
        {
            Email = "nonexistent@example.com",
            Password = "WrongPassword123!"
        };

        // Act
        var response = await _apiClient.PostAsync("auth/login/", loginRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "because credentials are invalid");
    }

    [Fact]
    public async Task Login_WithMissingPassword_RejectsRequest()
    {
        // Arrange
        var loginRequest = new LoginRequest
        {
            Email = "test@example.com"
            // Missing Password
        };

        // Act
        var response = await _apiClient.PostAsync("auth/login/", loginRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, "because password field is missing");
    }

    [Fact]
    public async Task UserProfile_WhenAuthenticated_ReturnsUserDetails()
    {
        // Arrange
        // First register and login a user
        var uniqueEmail = $"testuser_{Guid.NewGuid().ToString("N").Substring(0, 8)}@example.com";
        var password = "SecurePass123!";
        var registerRequest = new RegisterRequest
        {
            Email = uniqueEmail,
            Password = password,
            PasswordConfirm = password,
            FirstName = "Test",
            LastName = "User"
        };
        await _apiClient.PostAsync("auth/register/", registerRequest);

        var loginRequest = new LoginRequest
        {
            Email = uniqueEmail,
            Password = password
        };
        var loginResponse = await _apiClient.PostAsync<LoginRequest, LoginResponse>("auth/login/", loginRequest);

        _apiClient.SetAuthToken(loginResponse!.AccessToken);

        // Act
        var response = await _apiClient.GetAsync("auth/me/");
        var body = await _apiClient.GetResponseBodyAsync(response);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK, $"because valid token is provided. Response: {body}");

        var userResponse = await _apiClient.DeserializeResponseAsync<UserResponse>(response);
        userResponse.Should().NotBeNull();
        userResponse!.Email.Should().Be(uniqueEmail);
        userResponse.FirstName.Should().Be("Test");
        userResponse.LastName.Should().Be("User");
    }

    [Fact]
    public async Task UserProfile_WhenUnauthenticated_DeniesAccess()
    {
        // Arrange
        _apiClient.ClearAuthToken();

        // Act
        var response = await _apiClient.GetAsync("auth/me/");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "because no authentication token is provided");
    }

    [Fact]
    public async Task TokenRefresh_WithValidRefreshToken_IssuesNewAccessToken()
    {
        // Arrange
        // First register and login a user
        var uniqueEmail = $"testuser_{Guid.NewGuid().ToString("N").Substring(0, 8)}@example.com";
        var password = "SecurePass123!";
        var registerRequest = new RegisterRequest
        {
            Email = uniqueEmail,
            Password = password,
            PasswordConfirm = password,
            FirstName = "Test",
            LastName = "User"
        };
        await _apiClient.PostAsync("auth/register/", registerRequest);

        var loginRequest = new LoginRequest
        {
            Email = uniqueEmail,
            Password = password
        };
        var loginResponse = await _apiClient.PostAsync<LoginRequest, LoginResponse>("auth/login/", loginRequest);

        var refreshRequest = new RefreshTokenRequest
        {
            RefreshToken = loginResponse!.RefreshToken
        };

        // Act
        var response = await _apiClient.PostAsync("auth/refresh/", refreshRequest);
        var body = await _apiClient.GetResponseBodyAsync(response);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK, $"because valid refresh token is provided. Response: {body}");

        var refreshResponse = await _apiClient.DeserializeResponseAsync<RefreshTokenResponse>(response);
        refreshResponse.Should().NotBeNull();
        refreshResponse!.AccessToken.Should().NotBeNullOrEmpty("because new access token should be provided");
    }

    public void Dispose()
    {
        _apiClient?.Dispose();
    }
}


public class TestsManagementTests : IDisposable
{
    private readonly ApiClient _apiClient;
    private readonly string _baseUrl;
    private string? _accessToken;

    public TestsManagementTests()
    {
        _baseUrl = TestConfiguration.GetBaseUrl();
        _apiClient = new ApiClient(_baseUrl);
    }

    private async Task<string> AuthenticateAndGetToken()
    {
        if (!string.IsNullOrEmpty(_accessToken))
        {
            return _accessToken;
        }

        // Register and login a user
        var uniqueEmail = $"testuser_{Guid.NewGuid().ToString("N").Substring(0, 8)}@example.com";
        var password = "SecurePass123!";
        var registerRequest = new RegisterRequest
        {
            Email = uniqueEmail,
            Password = password,
            PasswordConfirm = password,
            FirstName = "Test",
            LastName = "User"
        };
        await _apiClient.PostAsync("auth/register/", registerRequest);

        var loginRequest = new LoginRequest
        {
            Email = uniqueEmail,
            Password = password
        };
        var loginResponse = await _apiClient.PostAsync<LoginRequest, LoginResponse>("auth/login/", loginRequest);

        _accessToken = loginResponse!.AccessToken;
        return _accessToken;
    }

    [Fact]
    public async Task TestCreation_WithValidData_CreatesTestSuccessfully()
    {
        // Arrange
        var token = await AuthenticateAndGetToken();
        _apiClient.SetAuthToken(token);

        var createTestRequest = new CreateTestRequest
        {
            Title = "JavaScript Basics",
            Description = "Test your JS knowledge",
            Visibility = "link_only",
            TimeLimitMinutes = 30,
            MaxAttempts = 3,
            ShowAnswersAfter = true
        };

        // Act
        var response = await _apiClient.PostAsync("tests/", createTestRequest);
        var body = await _apiClient.GetResponseBodyAsync(response);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created, $"because test creation should succeed. Response: {body}");

        var testResponse = await _apiClient.DeserializeResponseAsync<TestResponse>(response);
        testResponse.Should().NotBeNull();
        testResponse!.Title.Should().Be("JavaScript Basics");
        testResponse.Description.Should().Be("Test your JS knowledge");
        testResponse.Visibility.Should().Be("link_only");
        testResponse.TimeLimitMinutes.Should().Be(30);
        testResponse.MaxAttempts.Should().Be(3);
        testResponse.ShowAnswersAfter.Should().BeTrue();
        testResponse.Slug.Should().NotBeNullOrEmpty("because slug should be auto-generated");
    }

    [Fact]
    public async Task TestCreation_WhenUnauthenticated_DeniesAccess()
    {
        // Arrange
        _apiClient.ClearAuthToken();

        var createTestRequest = new CreateTestRequest
        {
            Title = "Unauthorized Test",
            Description = "This should fail"
        };

        // Act
        var response = await _apiClient.PostAsync("tests/", createTestRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "because authentication is required");
    }

    [Fact]
    public async Task TestCreation_WithMissingTitle_RejectsRequest()
    {
        // Arrange
        var token = await AuthenticateAndGetToken();
        _apiClient.SetAuthToken(token);

        var createTestRequest = new CreateTestRequest
        {
            Title = "", // Missing title
            Description = "Test with no title"
        };

        // Act
        var response = await _apiClient.PostAsync("tests/", createTestRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, "because title is required");
    }

    [Fact]
    public async Task TestListing_AsAuthor_ReturnsOwnTests()
    {
        // Arrange
        var token = await AuthenticateAndGetToken();
        _apiClient.SetAuthToken(token);

        // Create a test first
        var createTestRequest = new CreateTestRequest
        {
            Title = "My Test",
            Description = "A test I created"
        };
        await _apiClient.PostAsync("tests/", createTestRequest);

        // Act
        var response = await _apiClient.GetAsync("tests/");
        var body = await _apiClient.GetResponseBodyAsync(response);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK, $"because listing tests should succeed. Response: {body}");

        var tests = await _apiClient.DeserializeResponseAsync<List<TestResponse>>(response);
        tests.Should().NotBeNull();
        tests.Should().NotBeEmpty("because we just created a test");
        tests!.Should().Contain(t => t.Title == "My Test");
    }

    [Fact]
    public async Task TestDetails_WithValidSlug_ReturnsTestData()
    {
        // Arrange
        var token = await AuthenticateAndGetToken();
        _apiClient.SetAuthToken(token);

        // Create a test first
        var createTestRequest = new CreateTestRequest
        {
            Title = "Detailed Test",
            Description = "Test for details retrieval"
        };
        var createResponse = await _apiClient.PostAsync("tests/", createTestRequest);
        var createdTest = await _apiClient.DeserializeResponseAsync<TestResponse>(createResponse);

        // Act
        var response = await _apiClient.GetAsync($"tests/{createdTest!.Slug}/");
        var body = await _apiClient.GetResponseBodyAsync(response);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK, $"because test exists. Response: {body}");

        var testResponse = await _apiClient.DeserializeResponseAsync<TestResponse>(response);
        testResponse.Should().NotBeNull();
        testResponse!.Slug.Should().Be(createdTest.Slug);
        testResponse.Title.Should().Be("Detailed Test");
    }

    [Fact]
    public async Task TestDetails_WithNonExistentSlug_ReturnsNotFound()
    {
        // Arrange
        var token = await AuthenticateAndGetToken();
        _apiClient.SetAuthToken(token);

        // Act
        var response = await _apiClient.GetAsync("tests/non-existent-slug-12345/");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "because test with this slug doesn't exist");
    }

    [Fact]
    public async Task TestUpdate_WithValidData_UpdatesTestSuccessfully()
    {
        // Arrange
        var token = await AuthenticateAndGetToken();
        _apiClient.SetAuthToken(token);

        // Create a test first
        var createTestRequest = new CreateTestRequest
        {
            Title = "Original Title",
            Description = "Original description"
        };
        var createResponse = await _apiClient.PostAsync("tests/", createTestRequest);
        var createdTest = await _apiClient.DeserializeResponseAsync<TestResponse>(createResponse);

        var updateTestRequest = new CreateTestRequest
        {
            Title = "Updated Title",
            Description = "Updated description",
            Visibility = "public",
            MaxAttempts = 5
        };

        // Act
        var response = await _apiClient.PutAsync($"tests/{createdTest!.Slug}/", updateTestRequest);
        var body = await _apiClient.GetResponseBodyAsync(response);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK, $"because test update should succeed. Response: {body}");

        var updatedTest = await _apiClient.DeserializeResponseAsync<TestResponse>(response);
        updatedTest.Should().NotBeNull();
        updatedTest!.Title.Should().Be("Updated Title");
        updatedTest.Description.Should().Be("Updated description");
        updatedTest.Visibility.Should().Be("public");
        updatedTest.MaxAttempts.Should().Be(5);
    }

    [Fact]
    public async Task TestDeletion_WithValidSlug_RemovesTestCompletely()
    {
        // Arrange
        var token = await AuthenticateAndGetToken();
        _apiClient.SetAuthToken(token);

        // Create a test first
        var createTestRequest = new CreateTestRequest
        {
            Title = "Test To Delete",
            Description = "This test will be deleted"
        };
        var createResponse = await _apiClient.PostAsync("tests/", createTestRequest);
        var createdTest = await _apiClient.DeserializeResponseAsync<TestResponse>(createResponse);

        // Act
        var response = await _apiClient.DeleteAsync($"tests/{createdTest!.Slug}/");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent, "because test deletion should succeed");

        // Verify the test is gone
        var getResponse = await _apiClient.GetAsync($"tests/{createdTest.Slug}/");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound, "because test should be deleted");
    }

    [Fact]
    public async Task QuestionAddition_WithValidData_AddsQuestionToTest()
    {
        // Arrange
        var token = await AuthenticateAndGetToken();
        _apiClient.SetAuthToken(token);

        // Create a test first
        var createTestRequest = new CreateTestRequest
        {
            Title = "Math Test",
            Description = "Basic math questions"
        };
        var createResponse = await _apiClient.PostAsync("tests/", createTestRequest);
        var createdTest = await _apiClient.DeserializeResponseAsync<TestResponse>(createResponse);

        var createQuestionRequest = new CreateQuestionRequest
        {
            QuestionText = "What is 2 + 2?",
            QuestionType = "multiple_choice",
            Answers = new List<CreateAnswerRequest>
            {
                new() { AnswerText = "3", IsCorrect = false, Order = 1 },
                new() { AnswerText = "4", IsCorrect = true, Order = 2 },
                new() { AnswerText = "5", IsCorrect = false, Order = 3 }
            }
        };

        // Act
        var response = await _apiClient.PostAsync($"tests/{createdTest!.Slug}/questions/", createQuestionRequest);
        var body = await _apiClient.GetResponseBodyAsync(response);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created, $"because question creation should succeed. Response: {body}");

        var questionResponse = await _apiClient.DeserializeResponseAsync<QuestionResponse>(response);
        questionResponse.Should().NotBeNull();
        questionResponse!.QuestionText.Should().Be("What is 2 + 2?");
        questionResponse.QuestionType.Should().Be("multiple_choice");
        questionResponse.Answers.Should().HaveCount(3);
        questionResponse.Answers.Should().Contain(a => a.AnswerText == "4" && a.IsCorrect);
    }

    public void Dispose()
    {
        _apiClient?.Dispose();
    }
}


public class QuestionManagementTests : IDisposable
{
    private readonly ApiClient _apiClient;

    public QuestionManagementTests()
    {
        _apiClient = new ApiClient(TestConfiguration.GetBaseUrl());
    }

    private async Task SetupAuth()
    {
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
    }

    [Fact]
    public async Task QuestionUpdate_WithValidData_UpdatesQuestionSuccessfully()
    {
        await SetupAuth();

        var test = await TestDataHelper.CreateTestAsync(_apiClient, $"UpdateQ_{Guid.NewGuid().ToString("N")[..8]}");
        var question = await TestDataHelper.AddQuestionAsync(_apiClient, test.Slug, "Original question");

        var updateRequest = new UpdateQuestionRequest
        {
            QuestionText = "Updated question text",
            QuestionType = "multiple_choice",
            Answers = new List<CreateAnswerRequest>
            {
                new() { AnswerText = "New A", IsCorrect = false, Order = 1 },
                new() { AnswerText = "New B", IsCorrect = true, Order = 2 }
            }
        };

        var response = await _apiClient.PutAsync($"tests/{test.Slug}/questions/{question.Id}/", updateRequest);
        var body = await _apiClient.GetResponseBodyAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {body}");

        var updated = await _apiClient.DeserializeResponseAsync<QuestionResponse>(response);
        updated.Should().NotBeNull();
        updated!.QuestionText.Should().Be("Updated question text");
    }

    [Fact]
    public async Task QuestionUpdate_WithNonExistentId_ReturnsNotFound()
    {
        await SetupAuth();

        var test = await TestDataHelper.CreateTestAsync(_apiClient, $"UpdateQ404_{Guid.NewGuid().ToString("N")[..8]}");

        var updateRequest = new UpdateQuestionRequest
        {
            QuestionText = "Ghost question",
            QuestionType = "multiple_choice",
            Answers = new List<CreateAnswerRequest>
            {
                new() { AnswerText = "A", IsCorrect = true, Order = 1 }
            }
        };

        var response = await _apiClient.PutAsync($"tests/{test.Slug}/questions/99999/", updateRequest);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "because question ID does not exist");
    }

    [Fact]
    public async Task QuestionDeletion_WithValidId_RemovesQuestion()
    {
        await SetupAuth();

        var test = await TestDataHelper.CreateTestAsync(_apiClient, $"DeleteQ_{Guid.NewGuid().ToString("N")[..8]}");
        var question = await TestDataHelper.AddQuestionAsync(_apiClient, test.Slug, "Question to delete");

        var response = await _apiClient.DeleteAsync($"tests/{test.Slug}/questions/{question.Id}/");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, "because question deletion should succeed");
    }

    [Fact]
    public async Task QuestionDeletion_VerifyRemoval_QuestionNoLongerInTest()
    {
        await SetupAuth();

        var test = await TestDataHelper.CreateTestAsync(_apiClient, $"DeleteQVerify_{Guid.NewGuid().ToString("N")[..8]}");
        var question = await TestDataHelper.AddQuestionAsync(_apiClient, test.Slug, "Deletable question");

        await _apiClient.DeleteAsync($"tests/{test.Slug}/questions/{question.Id}/");

        var detailsResponse = await _apiClient.GetAsync($"tests/{test.Slug}/");
        var testDetails = await _apiClient.DeserializeResponseAsync<TestResponse>(detailsResponse);

        testDetails.Should().NotBeNull();
        if (testDetails!.Questions != null)
        {
            testDetails.Questions.Should().NotContain(q => q.Id == question.Id);
        }
    }

    [Fact]
    public async Task QuestionReorder_WithValidOrder_ReordersSuccessfully()
    {
        await SetupAuth();

        var test = await TestDataHelper.CreateTestAsync(_apiClient, $"Reorder_{Guid.NewGuid().ToString("N")[..8]}");
        var q1 = await TestDataHelper.AddQuestionAsync(_apiClient, test.Slug, "First question");
        var q2 = await TestDataHelper.AddQuestionAsync(_apiClient, test.Slug, "Second question");
        var q3 = await TestDataHelper.AddQuestionAsync(_apiClient, test.Slug, "Third question");

        var reorderRequest = new ReorderQuestionsRequest
        {
            Order = new List<OrderItem>
            {
                new() { Id = q3.Id, Order = 0 },
                new() { Id = q2.Id, Order = 1 },
                new() { Id = q1.Id, Order = 2 }
            }
        };

        var response = await _apiClient.PostAsync($"tests/{test.Slug}/questions/reorder/", reorderRequest);
        var body = await _apiClient.GetResponseBodyAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {body}");
    }

    [Fact]
    public async Task QuestionCreation_MultiSelectType_CreatesWithMultipleCorrectAnswers()
    {
        await SetupAuth();

        var test = await TestDataHelper.CreateTestAsync(_apiClient, $"MultiSelect_{Guid.NewGuid().ToString("N")[..8]}");

        var questionRequest = new CreateQuestionRequest
        {
            QuestionText = "Which are programming languages?",
            QuestionType = "multi_select",
            Answers = new List<CreateAnswerRequest>
            {
                new() { AnswerText = "Python", IsCorrect = true, Order = 1 },
                new() { AnswerText = "HTML", IsCorrect = false, Order = 2 },
                new() { AnswerText = "Java", IsCorrect = true, Order = 3 },
                new() { AnswerText = "CSS", IsCorrect = false, Order = 4 }
            }
        };

        var response = await _apiClient.PostAsync($"tests/{test.Slug}/questions/", questionRequest);
        var body = await _apiClient.GetResponseBodyAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.Created, $"Response: {body}");

        var question = await _apiClient.DeserializeResponseAsync<QuestionResponse>(response);
        question.Should().NotBeNull();
        question!.QuestionType.Should().Be("multi_select");
        question.Answers.Should().HaveCount(4);
        question.Answers.Count(a => a.IsCorrect).Should().Be(2);
    }

    public void Dispose()
    {
        _apiClient?.Dispose();
    }
}


public class TestTakingTests : IDisposable
{
    private readonly ApiClient _apiClient;  // author / authenticated
    private readonly ApiClient _anonClient; // anonymous / unauthenticated

    public TestTakingTests()
    {
        var baseUrl = TestConfiguration.GetBaseUrl();
        _apiClient = new ApiClient(baseUrl);
        _anonClient = new ApiClient(baseUrl);
    }

    /// <summary>
    /// Creates an authenticated test with 2 MC questions.
    /// Returns enough IDs to submit both correct and incorrect answers.
    /// </summary>
    private async Task<(TestResponse test, int correctId1, int wrongId1, int correctId2, int wrongId2)> CreateScorableTestAsync()
    {
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);

        var test = await TestDataHelper.CreateTestAsync(
            _apiClient,
            $"Scorable_{Guid.NewGuid().ToString("N")[..8]}");

        var q1 = await TestDataHelper.AddQuestionAsync(_apiClient, test.Slug, "What is 1+1?",
            answers: new List<CreateAnswerRequest>
            {
                new() { AnswerText = "2", IsCorrect = true, Order = 1 },
                new() { AnswerText = "3", IsCorrect = false, Order = 2 }
            });

        var q2 = await TestDataHelper.AddQuestionAsync(_apiClient, test.Slug, "Capital of France?",
            answers: new List<CreateAnswerRequest>
            {
                new() { AnswerText = "London", IsCorrect = false, Order = 1 },
                new() { AnswerText = "Paris", IsCorrect = true, Order = 2 }
            });

        return (test,
            q1.Answers.First(a => a.IsCorrect).Id,
            q1.Answers.First(a => !a.IsCorrect).Id,
            q2.Answers.First(a => a.IsCorrect).Id,
            q2.Answers.First(a => !a.IsCorrect).Id);
    }

    [Fact]
    public async Task TestAccess_PublicTest_AllowsAnonymousAccess()
    {
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);

        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"PublicTake_{Guid.NewGuid().ToString("N")[..8]}", visibility: "public");
        await TestDataHelper.AddQuestionAsync(_apiClient, test.Slug, "Sample question");

        // Anonymous client fetches the test
        var response = await _anonClient.GetAsync($"tests/{test.Slug}/take/");
        var body = await _anonClient.GetResponseBodyAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {body}");

        var takeTest = await _anonClient.DeserializeResponseAsync<TakeTestResponse>(response);
        takeTest.Should().NotBeNull();
        takeTest!.Title.Should().StartWith("PublicTake_");
        takeTest.Questions.Should().HaveCount(1);
    }

    [Fact]
    public async Task TestAccess_PasswordProtected_BlocksWithoutPassword()
    {
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);

        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"PwProtected_{Guid.NewGuid().ToString("N")[..8]}",
            visibility: "password_protected",
            password: "secret123");

        var response = await _anonClient.GetAsync($"tests/{test.Slug}/take/");
        var body = await _anonClient.GetResponseBodyAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, $"Response: {body}");

        var pwResponse = await _anonClient.DeserializeResponseAsync<PasswordRequiredResponse>(response);
        pwResponse.Should().NotBeNull();
        pwResponse!.RequiresPassword.Should().BeTrue();
    }

    [Fact]
    public async Task PasswordVerification_WithCorrectPassword_GrantsAccess()
    {
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);

        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"VerifyPW_{Guid.NewGuid().ToString("N")[..8]}",
            visibility: "password_protected",
            password: "secret123");

        var response = await _anonClient.PostAsync($"tests/{test.Slug}/verify-password/",
            new VerifyPasswordRequest { Password = "secret123" });
        var body = await _anonClient.GetResponseBodyAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {body}");

        var verified = await _anonClient.DeserializeResponseAsync<VerifyPasswordResponse>(response);
        verified.Should().NotBeNull();
        verified!.Verified.Should().BeTrue();
    }

    [Fact]
    public async Task PasswordVerification_WithWrongPassword_DeniesAccess()
    {
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);

        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"WrongPW_{Guid.NewGuid().ToString("N")[..8]}",
            visibility: "password_protected",
            password: "secret123");

        var response = await _anonClient.PostAsync($"tests/{test.Slug}/verify-password/",
            new VerifyPasswordRequest { Password = "wrongpassword" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "because wrong password should be rejected");
    }

    [Fact]
    public async Task AttemptStart_AsAnonymousUser_CreatesAttemptSuccessfully()
    {
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);

        var test = await TestDataHelper.CreateTestAsync(_apiClient, $"AnonStart_{Guid.NewGuid().ToString("N")[..8]}");
        await TestDataHelper.AddQuestionAsync(_apiClient, test.Slug, "Q1");

        var response = await _anonClient.PostAsync($"tests/{test.Slug}/attempts/",
            new StartAttemptRequest { AnonymousName = "Anonymous Tester" });
        var body = await _anonClient.GetResponseBodyAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.Created, $"Response: {body}");

        var attempt = await _anonClient.DeserializeResponseAsync<AttemptResponse>(response);
        attempt.Should().NotBeNull();
        attempt!.Id.Should().BePositive();
    }

    [Fact]
    public async Task DraftSave_WithValidAnswers_SavesDraftSuccessfully()
    {
        var (test, correctId1, _, _, _) = await CreateScorableTestAsync();

        // Get question IDs from the take endpoint
        var takeTest = await _anonClient.GetAsync<TakeTestResponse>($"tests/{test.Slug}/take/");
        takeTest.Should().NotBeNull();

        // Start attempt
        var startResponse = await _anonClient.PostAsync($"tests/{test.Slug}/attempts/",
            new StartAttemptRequest { AnonymousName = "Draft Tester" });
        var attempt = await _anonClient.DeserializeResponseAsync<AttemptResponse>(startResponse);

        // Save draft with one answer
        var draftRequest = new SaveDraftRequest
        {
            DraftAnswers = new Dictionary<string, List<int>>
            {
                { takeTest!.Questions[0].Id.ToString(), new List<int> { correctId1 } }
            }
        };

        var response = await _anonClient.PutAsync($"tests/{test.Slug}/attempts/{attempt!.Id}/", draftRequest);
        var body = await _anonClient.GetResponseBodyAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {body}");
    }

    [Fact]
    public async Task AttemptSubmission_AllCorrectAnswers_ScoresFullMarks()
    {
        var (test, correctId1, _, correctId2, _) = await CreateScorableTestAsync();

        var takeTest = await _anonClient.GetAsync<TakeTestResponse>($"tests/{test.Slug}/take/");

        // Start attempt
        var startResponse = await _anonClient.PostAsync($"tests/{test.Slug}/attempts/",
            new StartAttemptRequest { AnonymousName = "Perfect Scorer" });
        var attempt = await _anonClient.DeserializeResponseAsync<AttemptResponse>(startResponse);

        // Save draft with correct answers first (scoring is based on saved draft)
        var draftRequest = new SaveDraftRequest
        {
            DraftAnswers = new Dictionary<string, List<int>>
            {
                { takeTest!.Questions[0].Id.ToString(), new List<int> { correctId1 } },
                { takeTest.Questions[1].Id.ToString(), new List<int> { correctId2 } }
            }
        };
        await _anonClient.PutAsync($"tests/{test.Slug}/attempts/{attempt!.Id}/", draftRequest);

        // Submit the attempt
        var submitResponse = await _anonClient.PostAsync($"tests/{test.Slug}/attempts/{attempt.Id}/submit/",
            new Dictionary<string, object>());
        submitResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Score is in the results endpoint (only accessible by author)
        var resultsResponse = await _apiClient.GetAsync($"tests/{test.Slug}/results/");
        var body = await _apiClient.GetResponseBodyAsync(resultsResponse);
        resultsResponse.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {body}");

        var results = await _apiClient.DeserializeResponseAsync<List<ResultResponse>>(resultsResponse);
        results.Should().NotBeNull();

        var myResult = results!.First(r => r.AnonymousName == "Perfect Scorer");
        double.Parse(myResult.Score!).Should().Be(100.0);
        myResult.CorrectAnswers.Should().Be(2);
        myResult.TotalQuestions.Should().Be(2);
    }

    [Fact]
    public async Task AttemptSubmission_AllWrongAnswers_ScoresZeroMarks()
    {
        var (test, _, wrongId1, _, wrongId2) = await CreateScorableTestAsync();

        var takeTest = await _anonClient.GetAsync<TakeTestResponse>($"tests/{test.Slug}/take/");

        var startResponse = await _anonClient.PostAsync($"tests/{test.Slug}/attempts/",
            new StartAttemptRequest { AnonymousName = "Zero Scorer" });
        var attempt = await _anonClient.DeserializeResponseAsync<AttemptResponse>(startResponse);

        // Save draft with wrong answers
        var draftRequest = new SaveDraftRequest
        {
            DraftAnswers = new Dictionary<string, List<int>>
            {
                { takeTest!.Questions[0].Id.ToString(), new List<int> { wrongId1 } },
                { takeTest.Questions[1].Id.ToString(), new List<int> { wrongId2 } }
            }
        };
        await _anonClient.PutAsync($"tests/{test.Slug}/attempts/{attempt!.Id}/", draftRequest);

        // Submit
        var submitResponse = await _anonClient.PostAsync($"tests/{test.Slug}/attempts/{attempt.Id}/submit/",
            new Dictionary<string, object>());
        submitResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Check score via results
        var resultsResponse = await _apiClient.GetAsync($"tests/{test.Slug}/results/");
        var results = await _apiClient.DeserializeResponseAsync<List<ResultResponse>>(resultsResponse);

        var myResult = results!.First(r => r.AnonymousName == "Zero Scorer");
        double.Parse(myResult.Score!).Should().Be(0.0);
        myResult.CorrectAnswers.Should().Be(0);
    }

    [Fact]
    public async Task AttemptSubmission_AlreadySubmitted_PreventsDoubleSubmission()
    {
        var (test, correctId1, _, _, _) = await CreateScorableTestAsync();

        var takeTest = await _anonClient.GetAsync<TakeTestResponse>($"tests/{test.Slug}/take/");

        var startResponse = await _anonClient.PostAsync($"tests/{test.Slug}/attempts/",
            new StartAttemptRequest { AnonymousName = "Double Submitter" });
        var attempt = await _anonClient.DeserializeResponseAsync<AttemptResponse>(startResponse);

        // Save draft and submit once
        await _anonClient.PutAsync($"tests/{test.Slug}/attempts/{attempt!.Id}/", new SaveDraftRequest
        {
            DraftAnswers = new Dictionary<string, List<int>>
            {
                { takeTest!.Questions[0].Id.ToString(), new List<int> { correctId1 } }
            }
        });
        await _anonClient.PostAsync($"tests/{test.Slug}/attempts/{attempt.Id}/submit/",
            new Dictionary<string, object>());

        // Second submit should fail
        var response = await _anonClient.PostAsync($"tests/{test.Slug}/attempts/{attempt.Id}/submit/",
            new Dictionary<string, object>());

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "because attempt has already been submitted");
    }

    [Fact]
    public async Task ResultsRetrieval_AsAuthor_ReturnsAllSubmissions()
    {
        var (test, correctId1, _, correctId2, _) = await CreateScorableTestAsync();

        var takeTest = await _anonClient.GetAsync<TakeTestResponse>($"tests/{test.Slug}/take/");

        // Start, save draft, and submit an attempt
        var startResponse = await _anonClient.PostAsync($"tests/{test.Slug}/attempts/",
            new StartAttemptRequest { AnonymousName = "Results Tester" });
        var attempt = await _anonClient.DeserializeResponseAsync<AttemptResponse>(startResponse);

        await _anonClient.PutAsync($"tests/{test.Slug}/attempts/{attempt!.Id}/", new SaveDraftRequest
        {
            DraftAnswers = new Dictionary<string, List<int>>
            {
                { takeTest!.Questions[0].Id.ToString(), new List<int> { correctId1 } },
                { takeTest.Questions[1].Id.ToString(), new List<int> { correctId2 } }
            }
        });
        await _anonClient.PostAsync($"tests/{test.Slug}/attempts/{attempt.Id}/submit/",
            new Dictionary<string, object>());

        // Author retrieves results
        var response = await _apiClient.GetAsync($"tests/{test.Slug}/results/");
        var body = await _apiClient.GetResponseBodyAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {body}");

        var results = await _apiClient.DeserializeResponseAsync<List<ResultResponse>>(response);
        results.Should().NotBeNull();
        results.Should().NotBeEmpty();
        results!.Should().Contain(r => r.AnonymousName == "Results Tester");
    }

    public void Dispose()
    {
        _apiClient?.Dispose();
        _anonClient?.Dispose();
    }
}


public class CompanyTests : IDisposable
{
    private readonly ApiClient _apiClient;

    public CompanyTests()
    {
        _apiClient = new ApiClient(TestConfiguration.GetBaseUrl());
    }

    private async Task SetupAuth()
    {
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
    }

    /// <summary>
    /// Creates a company and returns the full response object including ID.
    /// API now returns complete object in create response (fixed in v1.8).
    /// </summary>
    private async Task<CompanyResponse> CreateCompanyAsync(string name)
    {
        var response = await _apiClient.PostAsync("companies/", new CreateCompanyRequest { Name = name });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await _apiClient.DeserializeResponseAsync<CompanyResponse>(response))!;
    }

    [Fact]
    public async Task CompanyCreation_WithValidData_CreatesCompanySuccessfully()
    {
        await SetupAuth();

        var companyName = $"TestCorp_{Guid.NewGuid().ToString("N")[..8]}";
        var response = await _apiClient.PostAsync("companies/",
            new CreateCompanyRequest { Name = companyName });
        var body = await _apiClient.GetResponseBodyAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.Created, $"Response: {body}");

        // Create response only contains the name; verify full record via list
        var companies = await _apiClient.GetAsync<List<CompanyResponse>>("companies/");
        companies.Should().NotBeNull();
        companies.Should().Contain(c => c.Name == companyName);
        companies!.First(c => c.Name == companyName).Id.Should().BePositive();
    }

    [Fact]
    public async Task CompanyCreation_WhenUnauthenticated_DeniesAccess()
    {
        _apiClient.ClearAuthToken();

        var response = await _apiClient.PostAsync("companies/",
            new CreateCompanyRequest { Name = "Unauthorized Corp" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "because authentication is required");
    }

    [Fact]
    public async Task CompanyListing_AfterCreation_IncludesNewCompany()
    {
        await SetupAuth();

        var companyName = $"ListCorp_{Guid.NewGuid().ToString("N")[..8]}";
        await _apiClient.PostAsync("companies/", new CreateCompanyRequest { Name = companyName });

        var response = await _apiClient.GetAsync("companies/");
        var body = await _apiClient.GetResponseBodyAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {body}");

        var companies = await _apiClient.DeserializeResponseAsync<List<CompanyResponse>>(response);
        companies.Should().NotBeNull();
        companies.Should().Contain(c => c.Name == companyName);
    }

    [Fact]
    public async Task CompanyDetails_AsAdmin_ReturnsCompanyData()
    {
        await SetupAuth();

        var company = await CreateCompanyAsync($"GetCorp_{Guid.NewGuid().ToString("N")[..8]}");

        var response = await _apiClient.GetAsync($"companies/{company.Id}/");
        var body = await _apiClient.GetResponseBodyAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {body}");

        var retrieved = await _apiClient.DeserializeResponseAsync<CompanyResponse>(response);
        retrieved.Should().NotBeNull();
        retrieved!.Id.Should().Be(company.Id);
    }

    [Fact]
    public async Task CompanyUpdate_WithValidData_UpdatesCompanySuccessfully()
    {
        await SetupAuth();

        var company = await CreateCompanyAsync($"OldName_{Guid.NewGuid().ToString("N")[..8]}");

        var response = await _apiClient.PutAsync($"companies/{company.Id}/",
            new UpdateCompanyRequest { Name = "NewCompanyName" });
        var body = await _apiClient.GetResponseBodyAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {body}");

        var updated = await _apiClient.DeserializeResponseAsync<CompanyResponse>(response);
        updated.Should().NotBeNull();
        updated!.Name.Should().Be("NewCompanyName");
    }

    [Fact]
    public async Task CompanyDeletion_AsAdmin_RemovesCompanyCompletely()
    {
        await SetupAuth();

        var company = await CreateCompanyAsync($"DeleteCorp_{Guid.NewGuid().ToString("N")[..8]}");

        var response = await _apiClient.DeleteAsync($"companies/{company.Id}/");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, "because company deletion should succeed");

        // Verify deleted
        var getResponse = await _apiClient.GetAsync($"companies/{company.Id}/");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound, "because company should be deleted");
    }

    [Fact]
    public async Task MemberListing_NewCompany_CreatorIsOnlyAdmin()
    {
        await SetupAuth();

        var company = await CreateCompanyAsync($"MemberCorp_{Guid.NewGuid().ToString("N")[..8]}");

        var response = await _apiClient.GetAsync($"companies/{company.Id}/members/");
        var body = await _apiClient.GetResponseBodyAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {body}");

        var members = await _apiClient.DeserializeResponseAsync<List<MemberResponse>>(response);
        members.Should().NotBeNull();
        members.Should().HaveCount(1, "because only the creator should be a member initially");
        members![0].Role.Should().Be("admin", "because the creator becomes admin automatically");
    }

    [Fact]
    public async Task CompanyTestListing_AsAdmin_ReturnsCompanyTests()
    {
        await SetupAuth();

        var company = await CreateCompanyAsync($"TestListCorp_{Guid.NewGuid().ToString("N")[..8]}");

        var response = await _apiClient.GetAsync($"tests/company/{company.Id}/");
        var body = await _apiClient.GetResponseBodyAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {body}");
    }

    [Fact]
    public async Task CompanyTestCreation_WithValidData_CreatesTestInCompany()
    {
        await SetupAuth();

        var company = await CreateCompanyAsync($"CompTestCorp_{Guid.NewGuid().ToString("N")[..8]}");

        var response = await _apiClient.PostAsync($"tests/company/{company.Id}/", new CreateCompanyTestRequest
        {
            Title = "Company Quiz",
            Description = "Internal company test"
        });
        var body = await _apiClient.GetResponseBodyAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.Created, $"Response: {body}");

        var test = await _apiClient.DeserializeResponseAsync<TestResponse>(response);
        test.Should().NotBeNull();
        test!.Title.Should().Be("Company Quiz");
    }

    public void Dispose()
    {
        _apiClient?.Dispose();
    }
}


public class InviteTests : IDisposable
{
    private readonly ApiClient _adminClient;
    private readonly ApiClient _inviteeClient;

    public InviteTests()
    {
        var baseUrl = TestConfiguration.GetBaseUrl();
        _adminClient = new ApiClient(baseUrl);
        _inviteeClient = new ApiClient(baseUrl);
    }

    /// <summary>
    /// Sets up: admin user with a company, and a registered (but not logged-in) invitee.
    /// API now returns full object including ID in create response (fixed in v1.8).
    /// </summary>
    private async Task<(CompanyResponse company, string inviteeEmail, string inviteePassword)> SetupCompanyAndInviteeAsync()
    {
        await TestDataHelper.RegisterAndLoginAsync(_adminClient);

        var companyName = $"InviteCorp_{Guid.NewGuid().ToString("N")[..8]}";
        var response = await _adminClient.PostAsync("companies/", new CreateCompanyRequest { Name = companyName });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var company = (await _adminClient.DeserializeResponseAsync<CompanyResponse>(response))!;

        var (inviteeEmail, inviteePassword) = await TestDataHelper.RegisterUserAsync(_inviteeClient);

        return (company, inviteeEmail, inviteePassword);
    }

    [Fact]
    public async Task InviteCreation_WithValidEmail_SendsInviteSuccessfully()
    {
        var (company, inviteeEmail, _) = await SetupCompanyAndInviteeAsync();

        var response = await _adminClient.PostAsync($"companies/{company.Id}/invites/", new CreateInviteRequest
        {
            Email = inviteeEmail,
            Role = "student"
        });
        var body = await _adminClient.GetResponseBodyAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.Created, $"Response: {body}");

        var invite = await _adminClient.DeserializeResponseAsync<InviteResponse>(response);
        invite.Should().NotBeNull();
        invite!.Email.Should().Be(inviteeEmail);
        invite.Role.Should().Be("student");
        invite.Token.Should().NotBeNullOrEmpty("because the invite token is returned directly (no email sending)");
    }

    [Fact]
    public async Task InviteCreation_DuplicatePendingInvite_RejectsRequest()
    {
        var (company, inviteeEmail, _) = await SetupCompanyAndInviteeAsync();

        // First invite succeeds
        await _adminClient.PostAsync($"companies/{company.Id}/invites/", new CreateInviteRequest
        {
            Email = inviteeEmail,
            Role = "student"
        });

        // Duplicate invite for the same email
        var response = await _adminClient.PostAsync($"companies/{company.Id}/invites/", new CreateInviteRequest
        {
            Email = inviteeEmail,
            Role = "instructor"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "because a pending invite already exists for this email");
    }

    [Fact]
    public async Task InviteListing_WithPendingInvites_ReturnsPendingInvites()
    {
        var (company, inviteeEmail, _) = await SetupCompanyAndInviteeAsync();

        await _adminClient.PostAsync($"companies/{company.Id}/invites/", new CreateInviteRequest
        {
            Email = inviteeEmail,
            Role = "student"
        });

        var response = await _adminClient.GetAsync($"companies/{company.Id}/invites/");
        var body = await _adminClient.GetResponseBodyAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {body}");

        var invites = await _adminClient.DeserializeResponseAsync<List<InviteResponse>>(response);
        invites.Should().NotBeNull();
        invites.Should().Contain(i => i.Email == inviteeEmail);
    }

    [Fact]
    public async Task InviteAcceptance_WithValidToken_AddsUserToCompany()
    {
        var (company, inviteeEmail, inviteePassword) = await SetupCompanyAndInviteeAsync();

        // Admin creates invite
        var createResponse = await _adminClient.PostAsync($"companies/{company.Id}/invites/", new CreateInviteRequest
        {
            Email = inviteeEmail,
            Role = "student"
        });
        var invite = await _adminClient.DeserializeResponseAsync<InviteResponse>(createResponse);

        // Invitee logs in
        var loginResponse = await _inviteeClient.PostAsync<LoginRequest, LoginResponse>("auth/login/", new LoginRequest
        {
            Email = inviteeEmail,
            Password = inviteePassword
        });
        _inviteeClient.SetAuthToken(loginResponse!.AccessToken);

        // Invitee accepts the invite
        var response = await _inviteeClient.PostAsync($"invites/{invite!.Token}/accept/",
            new Dictionary<string, object>());
        var body = await _inviteeClient.GetResponseBodyAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {body}");

        // Verify invitee is now a member
        var membersResponse = await _adminClient.GetAsync($"companies/{company.Id}/members/");
        var members = await _adminClient.DeserializeResponseAsync<List<MemberResponse>>(membersResponse);
        members.Should().NotBeNull();
        members.Should().HaveCount(2, "because invitee accepted and should now be a member");
        members.Should().Contain(m => m.Email == inviteeEmail);
    }

    public void Dispose()
    {
        _adminClient?.Dispose();
        _inviteeClient?.Dispose();
    }
}


public class FolderTests : IDisposable
{
    private readonly ApiClient _apiClient;

    public FolderTests()
    {
        _apiClient = new ApiClient(TestConfiguration.GetBaseUrl());
    }

    /// <summary>
    /// Registers a user, logs in, creates a company, and returns the company with its id.
    /// API now returns full object including ID in create response (fixed in v1.8).
    /// </summary>
    private async Task<CompanyResponse> SetupCompanyAsync()
    {
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);

        var name = $"FolderCorp_{Guid.NewGuid().ToString("N")[..8]}";
        var response = await _apiClient.PostAsync("companies/", new CreateCompanyRequest { Name = name });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await _apiClient.DeserializeResponseAsync<CompanyResponse>(response))!;
    }

    /// <summary>
    /// Creates a folder and retrieves its full record (including id) from the list endpoint.
    /// Folder create response doesn't include id.
    /// </summary>
    private async Task<FolderResponse> CreateFolderAsync(int companyId, string name, int? parent = null)
    {
        await _apiClient.PostAsync($"companies/{companyId}/folders/",
            new CreateFolderRequest { Name = name, Parent = parent });
        var folders = await _apiClient.GetAsync<List<FolderResponse>>($"companies/{companyId}/folders/");
        return folders!.First(f => f.Name == name);
    }

    [Fact]
    public async Task FolderCreation_TopLevel_CreatesFolderSuccessfully()
    {
        var company = await SetupCompanyAsync();

        var response = await _apiClient.PostAsync($"companies/{company.Id}/folders/",
            new CreateFolderRequest { Name = "Math Folder" });
        var body = await _apiClient.GetResponseBodyAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.Created, $"Response: {body}");

        // Create response doesn't include id; verify via list
        var folders = await _apiClient.GetAsync<List<FolderResponse>>($"companies/{company.Id}/folders/");
        folders.Should().NotBeNull();
        folders.Should().Contain(f => f.Name == "Math Folder");
        folders!.First(f => f.Name == "Math Folder").Parent.Should().BeNull();
    }

    [Fact]
    public async Task FolderListing_AfterCreation_IncludesNewFolder()
    {
        var company = await SetupCompanyAsync();

        var folderName = $"ListFolder_{Guid.NewGuid().ToString("N")[..8]}";
        await _apiClient.PostAsync($"companies/{company.Id}/folders/",
            new CreateFolderRequest { Name = folderName });

        var response = await _apiClient.GetAsync($"companies/{company.Id}/folders/");
        var body = await _apiClient.GetResponseBodyAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {body}");

        var folders = await _apiClient.DeserializeResponseAsync<List<FolderResponse>>(response);
        folders.Should().NotBeNull();
        folders.Should().Contain(f => f.Name == folderName);
    }

    [Fact]
    public async Task FolderUpdate_WithNewName_RenamesFolderSuccessfully()
    {
        var company = await SetupCompanyAsync();
        var folder = await CreateFolderAsync(company.Id, "OldFolderName");

        var response = await _apiClient.PutAsync($"companies/{company.Id}/folders/{folder.Id}/",
            new UpdateFolderRequest { Name = "NewFolderName" });
        var body = await _apiClient.GetResponseBodyAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {body}");

        var updated = await _apiClient.DeserializeResponseAsync<FolderResponse>(response);
        updated.Should().NotBeNull();
        updated!.Name.Should().Be("NewFolderName");
    }

    [Fact]
    public async Task FolderDeletion_WithValidId_RemovesFolder()
    {
        var company = await SetupCompanyAsync();
        var folder = await CreateFolderAsync(company.Id, "DeleteMe");

        var response = await _apiClient.DeleteAsync($"companies/{company.Id}/folders/{folder.Id}/");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, "because folder deletion should succeed");
    }

    [Fact]
    public async Task FolderCreation_WithParent_CreatesNestedFolderStructure()
    {
        var company = await SetupCompanyAsync();

        // Create parent folder and get its id
        var parent = await CreateFolderAsync(company.Id, "Parent Folder");

        // Create child folder with parent id
        var response = await _apiClient.PostAsync($"companies/{company.Id}/folders/", new CreateFolderRequest
        {
            Name = "Child Folder",
            Parent = parent.Id
        });
        var body = await _apiClient.GetResponseBodyAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.Created, $"Response: {body}");

        // Verify via list
        var folders = await _apiClient.GetAsync<List<FolderResponse>>($"companies/{company.Id}/folders/");
        var child = folders!.First(f => f.Name == "Child Folder");
        child.Parent.Should().Be(parent.Id);
    }

    public void Dispose()
    {
        _apiClient?.Dispose();
    }
}


public class AnalyticsTests : IDisposable
{
    private readonly ApiClient _apiClient;  // test author
    private readonly ApiClient _anonClient; // for submitting attempts
    private readonly ApiClient _otherClient; // a different authenticated user

    public AnalyticsTests()
    {
        var baseUrl = TestConfiguration.GetBaseUrl();
        _apiClient = new ApiClient(baseUrl);
        _anonClient = new ApiClient(baseUrl);
        _otherClient = new ApiClient(baseUrl);
    }

    [Fact]
    public async Task Analytics_NoSubmissions_ReturnsZeroStats()
    {
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);

        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"AnalyticsEmpty_{Guid.NewGuid().ToString("N")[..8]}");
        await TestDataHelper.AddQuestionAsync(_apiClient, test.Slug, "Lonely question");

        var response = await _apiClient.GetAsync($"analytics/tests/{test.Slug}/");
        var body = await _apiClient.GetResponseBodyAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {body}");

        var analytics = await _apiClient.DeserializeResponseAsync<AnalyticsResponse>(response);
        analytics.Should().NotBeNull();
        analytics!.TotalAttempts.Should().Be(0);
    }

    [Fact]
    public async Task Analytics_AfterSubmission_ReflectsAttemptData()
    {
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);

        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"AnalyticsStats_{Guid.NewGuid().ToString("N")[..8]}");

        var question = await TestDataHelper.AddQuestionAsync(_apiClient, test.Slug, "Stats question",
            answers: new List<CreateAnswerRequest>
            {
                new() { AnswerText = "Right", IsCorrect = true, Order = 1 },
                new() { AnswerText = "Wrong", IsCorrect = false, Order = 2 }
            });

        int correctId = question.Answers.First(a => a.IsCorrect).Id;

        // Get question IDs from the public take endpoint
        var takeTest = await _anonClient.GetAsync<TakeTestResponse>($"tests/{test.Slug}/take/");

        // Start attempt, save draft, then submit (scoring uses saved draft)
        var startResponse = await _anonClient.PostAsync($"tests/{test.Slug}/attempts/",
            new StartAttemptRequest { AnonymousName = "Stats Tester" });
        var attempt = await _anonClient.DeserializeResponseAsync<AttemptResponse>(startResponse);

        await _anonClient.PutAsync($"tests/{test.Slug}/attempts/{attempt!.Id}/", new SaveDraftRequest
        {
            DraftAnswers = new Dictionary<string, List<int>>
            {
                { takeTest!.Questions[0].Id.ToString(), new List<int> { correctId } }
            }
        });
        await _anonClient.PostAsync($"tests/{test.Slug}/attempts/{attempt.Id}/submit/",
            new Dictionary<string, object>());

        // Fetch analytics as author
        var response = await _apiClient.GetAsync($"analytics/tests/{test.Slug}/");
        var body = await _apiClient.GetResponseBodyAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {body}");

        var analytics = await _apiClient.DeserializeResponseAsync<AnalyticsResponse>(response);
        analytics.Should().NotBeNull();
        analytics!.TotalAttempts.Should().Be(1);
        analytics.QuestionStats.Should().HaveCount(1);
    }

    [Fact]
    public async Task Analytics_WhenUnauthenticated_DeniesAccess()
    {
        // Create a test first so the slug is real
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"AnalyticsNoAuth_{Guid.NewGuid().ToString("N")[..8]}");

        // Clear auth and try to access
        _apiClient.ClearAuthToken();

        var response = await _apiClient.GetAsync($"analytics/tests/{test.Slug}/");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "because authentication is required");
    }

    [Fact]
    public async Task Analytics_AsNonAuthor_DeniesAccessToOtherUsersTest()
    {
        // Create test as author
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"AnalyticsAuth_{Guid.NewGuid().ToString("N")[..8]}");

        // Different user tries to access analytics
        await TestDataHelper.RegisterAndLoginAsync(_otherClient);

        var response = await _otherClient.GetAsync($"analytics/tests/{test.Slug}/");

        // API may return 403 (forbidden) or 404 (hide existence) — both are correct
        response.StatusCode.Should().BeOneOf(
            new[] { HttpStatusCode.Forbidden, HttpStatusCode.NotFound },
            "because only the test author can view analytics");
    }

    public void Dispose()
    {
        _apiClient?.Dispose();
        _anonClient?.Dispose();
        _otherClient?.Dispose();
    }
}

