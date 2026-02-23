using System.Net;
using FluentAssertions;
using TestIT.ApiTests.Helpers;
using TestIT.ApiTests.Models;
using Xunit;

namespace TestIT.ApiTests.Tests;

public class TestsManagementTests : IDisposable
{
    private readonly ApiClient _apiClient;
    private string? _accessToken;

    public TestsManagementTests(ITestOutputHelper output)
    {
        ApiClient.SetOutput(output.WriteLine);
        _apiClient = new ApiClient(TestConfiguration.GetBaseUrl());
    }

    private async Task<string> AuthenticateAndGetToken()
    {
        if (!string.IsNullOrEmpty(_accessToken))
        {
            return _accessToken;
        }

        // Register, login, and track for cleanup
        var (_, _, token) = await TestAccountManager.CreateAndTrackAccountAsync(_apiClient, "testuser");
        _accessToken = token;
        return _accessToken;
    }

    [Fact]
    [Trait("Category", "Smoke")]
    [Trait("Priority", "P0")]
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
    [Trait("Category", "Functional")]
    [Trait("Priority", "P0")]
    public async Task TestCreation_WithTitleOnly_PopulatesDefaultFields()
    {
        // Arrange
        var token = await AuthenticateAndGetToken();
        _apiClient.SetAuthToken(token);

        var createTestRequest = new CreateTestRequest
        {
            Title = $"DefaultFields_{Guid.NewGuid().ToString("N")[..8]}"
            // All other fields intentionally omitted — testing server-side defaults
        };

        // Act
        var response = await _apiClient.PostAsync("tests/", createTestRequest);
        var body = await _apiClient.GetResponseBodyAsync(response);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created,
            $"because a test with only a title should be creatable. Response: {body}");

        var testResponse = await _apiClient.DeserializeResponseAsync<TestResponse>(response);
        testResponse.Should().NotBeNull();

        testResponse!.Slug.Should().NotBeNullOrWhiteSpace(
            "because a non-empty slug must be auto-generated from the title");

        var knownVisibilities = new[] { "public", "link_only", "private", "password_protected" };
        testResponse.Visibility.Should().BeOneOf(knownVisibilities,
            "because visibility must default to a known, valid value");

        testResponse.MaxAttempts.Should().BeGreaterThan(0,
            "because default max_attempts must be a positive integer");

        // ShowAnswersAfter is bool — deserialization succeeding already proves it's a valid boolean
        _ = testResponse.ShowAnswersAfter;
    }

    [Fact]
    [Trait("Category", "Functional")]
    [Trait("Priority", "P1")]
    public async Task TestCreation_DuplicateTitle_GeneratesUniqueSlug()
    {
        // Arrange
        var token = await AuthenticateAndGetToken();
        _apiClient.SetAuthToken(token);

        var duplicateTitle = $"DuplicateTitle_{Guid.NewGuid().ToString("N")[..8]}";

        // Act: create two tests with identical titles
        var firstResponse = await _apiClient.PostAsync("tests/", new CreateTestRequest
        {
            Title = duplicateTitle
        });
        var firstBody = await _apiClient.GetResponseBodyAsync(firstResponse);
        firstResponse.StatusCode.Should().Be(HttpStatusCode.Created,
            $"first test creation should succeed. Response: {firstBody}");

        var secondResponse = await _apiClient.PostAsync("tests/", new CreateTestRequest
        {
            Title = duplicateTitle
        });
        var secondBody = await _apiClient.GetResponseBodyAsync(secondResponse);
        secondResponse.StatusCode.Should().Be(HttpStatusCode.Created,
            $"second test with identical title should also succeed. Response: {secondBody}");

        var firstTest  = await _apiClient.DeserializeResponseAsync<TestResponse>(firstResponse);
        var secondTest = await _apiClient.DeserializeResponseAsync<TestResponse>(secondResponse);

        // Assert: both slugs must be present and different from each other
        firstTest!.Slug.Should().NotBeNullOrWhiteSpace();
        secondTest!.Slug.Should().NotBeNullOrWhiteSpace();
        secondTest.Slug.Should().NotBe(firstTest.Slug,
            "because duplicate titles must produce unique slugs to avoid URL collisions");
    }

    [Fact]
    [Trait("Category", "Security")]
    [Trait("Priority", "P0")]
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
    [Trait("Category", "Validation")]
    [Trait("Priority", "P1")]
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
    [Trait("Category", "Smoke")]
    [Trait("Priority", "P0")]
    public async Task TestListing_AsAuthor_ReturnsOwnTests()
    {
        // Arrange
        var token = await AuthenticateAndGetToken();
        _apiClient.SetAuthToken(token);

        // Create a test with a unique title so stale data from previous runs cannot produce a false pass
        var testTitle = $"ListTest_{Guid.NewGuid().ToString("N")[..8]}";
        var createTestRequest = new CreateTestRequest
        {
            Title = testTitle,
            Description = "A test I created"
        };
        var createResponse = await _apiClient.PostAsync("tests/", createTestRequest);
        var createdTest = await _apiClient.DeserializeResponseAsync<TestResponse>(createResponse);

        // Act
        var response = await _apiClient.GetAsync("tests/");
        var body = await _apiClient.GetResponseBodyAsync(response);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK, $"because listing tests should succeed. Response: {body}");

        var tests = await _apiClient.DeserializeResponseAsync<List<TestResponse>>(response);
        tests.Should().NotBeNull();
        tests.Should().NotBeEmpty("because we just created a test");
        tests!.Should().Contain(t => t.Slug == createdTest!.Slug,
            "because the test we just created must appear in the author's test listing");
    }

    [Fact]
    [Trait("Category", "Functional")]
    [Trait("Priority", "P0")]
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
    [Trait("Category", "Functional")]
    [Trait("Priority", "P1")]
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
    [Trait("Category", "Functional")]
    [Trait("Priority", "P0")]
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
    [Trait("Category", "Functional")]
    [Trait("Priority", "P0")]
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
    [Trait("Category", "Smoke")]
    [Trait("Priority", "P0")]
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
