using System.Net;
using System.Text.Json;
using FluentAssertions;
using TestIT.ApiTests.Helpers;
using TestIT.ApiTests.Models;
using Xunit;

namespace TestIT.ApiTests.Tests;

/// <summary>
/// Schema validation tests verify that API responses match expected contracts:
/// - Required fields are present with correct types
/// - Sensitive fields (passwords, internal IDs) are never exposed
/// - Response structure remains stable (backward compatibility)
/// </summary>
public class SchemaValidationTests : IDisposable
{
    private readonly ApiClient _apiClient;
    private readonly ApiClient _anonClient;

    public SchemaValidationTests(ITestOutputHelper output)
    {
        ApiClient.SetOutput(output.WriteLine);
        var baseUrl = TestConfiguration.GetBaseUrl();
        _apiClient = new ApiClient(baseUrl);
        _anonClient = new ApiClient(baseUrl);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Authentication Schemas
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    [Trait("Category", "Schema")]
    [Trait("Priority", "P1")]
    public async Task Schema_LoginResponse_HasRequiredFieldsOnly()
    {
        // Arrange
        var (email, password) = await TestDataHelper.RegisterUserAsync(_apiClient);

        // Act
        var response = await _apiClient.PostAsync("auth/login/",
            new LoginRequest { Email = email, Password = password });
        var json = await response.Content.ReadAsStringAsync();

        // Assert: response must deserialize successfully
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var loginResponse = await _apiClient.DeserializeResponseAsync<LoginResponse>(response);
        loginResponse.Should().NotBeNull();

        // Assert: required fields present with correct types
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.TryGetProperty("access", out var access).Should().BeTrue(
            "LoginResponse must include 'access' token");
        access.ValueKind.Should().Be(JsonValueKind.String);
        access.GetString().Should().NotBeNullOrEmpty();

        root.TryGetProperty("refresh", out var refresh).Should().BeTrue(
            "LoginResponse must include 'refresh' token");
        refresh.ValueKind.Should().Be(JsonValueKind.String);
        refresh.GetString().Should().NotBeNullOrEmpty();

        // Assert: sensitive fields must NOT be present
        root.TryGetProperty("password", out _).Should().BeFalse(
            "LoginResponse must never expose the password");
        root.TryGetProperty("password_hash", out _).Should().BeFalse(
            "LoginResponse must never expose password_hash");
    }

    [Fact]
    [Trait("Category", "Schema")]
    [Trait("Priority", "P1")]
    public async Task Schema_UserResponse_HasRequiredFieldsOnly()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);

        // Act
        var response = await _apiClient.GetAsync("auth/me/");
        var json = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Required fields
        root.TryGetProperty("id", out var id).Should().BeTrue();
        id.ValueKind.Should().Be(JsonValueKind.Number);
        id.GetInt32().Should().BePositive();

        root.TryGetProperty("email", out var emailProp).Should().BeTrue();
        emailProp.ValueKind.Should().Be(JsonValueKind.String);
        emailProp.GetString().Should().Contain("@");

        root.TryGetProperty("first_name", out var firstName).Should().BeTrue();
        firstName.ValueKind.Should().Be(JsonValueKind.String);

        root.TryGetProperty("last_name", out var lastName).Should().BeTrue();
        lastName.ValueKind.Should().Be(JsonValueKind.String);

        // Sensitive fields must NOT be present
        root.TryGetProperty("password", out _).Should().BeFalse();
        root.TryGetProperty("password_hash", out _).Should().BeFalse();
        root.TryGetProperty("hashed_password", out _).Should().BeFalse();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Test Management Schemas
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    [Trait("Category", "Schema")]
    [Trait("Priority", "P1")]
    public async Task Schema_TestResponse_HasRequiredFields()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"Schema_{Guid.NewGuid().ToString("N")[..8]}",
            description: "Schema validation test",
            showAnswersAfter: true);

        // Act
        var response = await _apiClient.GetAsync($"tests/{test.Slug}/");
        var json = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Required fields with correct types
        root.TryGetProperty("id", out var id).Should().BeTrue();
        id.ValueKind.Should().Be(JsonValueKind.Number);

        root.TryGetProperty("title", out var title).Should().BeTrue();
        title.ValueKind.Should().Be(JsonValueKind.String);

        root.TryGetProperty("slug", out var slug).Should().BeTrue();
        slug.ValueKind.Should().Be(JsonValueKind.String);
        slug.GetString().Should().NotBeNullOrWhiteSpace();

        root.TryGetProperty("visibility", out var visibility).Should().BeTrue();
        visibility.ValueKind.Should().Be(JsonValueKind.String);
        var knownVisibilities = new[] { "public", "link_only", "private", "password_protected" };
        knownVisibilities.Should().Contain(visibility.GetString());

        root.TryGetProperty("max_attempts", out var maxAttempts).Should().BeTrue();
        maxAttempts.ValueKind.Should().Be(JsonValueKind.Number);

        root.TryGetProperty("show_answers_after", out var showAnswers).Should().BeTrue();
        (showAnswers.ValueKind == JsonValueKind.True || showAnswers.ValueKind == JsonValueKind.False)
            .Should().BeTrue("show_answers_after must be a boolean");

        root.TryGetProperty("created_at", out var createdAt).Should().BeTrue();
        createdAt.ValueKind.Should().Be(JsonValueKind.String,
            "created_at should be an ISO8601 timestamp string");

        // Optional nullable field
        if (root.TryGetProperty("description", out var desc))
        {
            (desc.ValueKind == JsonValueKind.String || desc.ValueKind == JsonValueKind.Null)
                .Should().BeTrue();
        }
    }

    [Fact]
    [Trait("Category", "Schema")]
    [Trait("Priority", "P1")]
    public async Task Schema_QuestionResponse_HasRequiredFields()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"QSchema_{Guid.NewGuid().ToString("N")[..8]}");
        await TestDataHelper.AddQuestionAsync(_apiClient, test.Slug, "Schema question");

        // Act: fetch test with questions included
        var response = await _apiClient.GetAsync($"tests/{test.Slug}/");
        var json = await response.Content.ReadAsStringAsync();

        // Assert
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.TryGetProperty("questions", out var questions).Should().BeTrue();
        questions.ValueKind.Should().Be(JsonValueKind.Array);
        questions.GetArrayLength().Should().BeGreaterThan(0);

        var question = questions.EnumerateArray().First();
        question.TryGetProperty("id", out var qId).Should().BeTrue();
        qId.ValueKind.Should().Be(JsonValueKind.Number);

        question.TryGetProperty("question_text", out var qText).Should().BeTrue();
        qText.ValueKind.Should().Be(JsonValueKind.String);

        question.TryGetProperty("question_type", out var qType).Should().BeTrue();
        qType.ValueKind.Should().Be(JsonValueKind.String);

        question.TryGetProperty("order", out var order).Should().BeTrue();
        order.ValueKind.Should().Be(JsonValueKind.Number);

        question.TryGetProperty("answers", out var answers).Should().BeTrue();
        answers.ValueKind.Should().Be(JsonValueKind.Array);

        // Validate answer structure
        var answer = answers.EnumerateArray().First();
        answer.TryGetProperty("id", out var aId).Should().BeTrue();
        aId.ValueKind.Should().Be(JsonValueKind.Number);

        answer.TryGetProperty("answer_text", out var aText).Should().BeTrue();
        aText.ValueKind.Should().Be(JsonValueKind.String);

        answer.TryGetProperty("is_correct", out var isCorrect).Should().BeTrue();
        (isCorrect.ValueKind == JsonValueKind.True || isCorrect.ValueKind == JsonValueKind.False)
            .Should().BeTrue();

        answer.TryGetProperty("order", out var aOrder).Should().BeTrue();
        aOrder.ValueKind.Should().Be(JsonValueKind.Number);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Test Taking Schemas
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    [Trait("Category", "Schema")]
    [Trait("Priority", "P1")]
    public async Task Schema_TakeTestResponse_DoesNotLeakCorrectAnswers()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"TakeSchema_{Guid.NewGuid().ToString("N")[..8]}",
            visibility: "public",
            showAnswersAfter: false);
        await TestDataHelper.AddQuestionAsync(_apiClient, test.Slug, "Take question");

        // Act
        var response = await _anonClient.GetAsync($"tests/{test.Slug}/take/");
        var json = await response.Content.ReadAsStringAsync();

        // Assert
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.TryGetProperty("id", out _).Should().BeTrue();
        root.TryGetProperty("title", out _).Should().BeTrue();
        root.TryGetProperty("questions", out var questions).Should().BeTrue();

        var question = questions.EnumerateArray().First();
        question.TryGetProperty("answers", out var answers).Should().BeTrue();

        // Critical: is_correct must be false or omitted on /take/ endpoint BEFORE submission
        var answer = answers.EnumerateArray().First();
        if (answer.TryGetProperty("is_correct", out var isCorrect))
        {
            isCorrect.GetBoolean().Should().BeFalse(
                "the /take/ endpoint must not reveal is_correct=true before submission");
        }
    }

    [Fact]
    [Trait("Category", "Schema")]
    [Trait("Priority", "P1")]
    public async Task Schema_AttemptResponse_HasRequiredFields()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"AttemptSchema_{Guid.NewGuid().ToString("N")[..8]}");

        // Act
        var response = await _anonClient.PostAsync($"tests/{test.Slug}/attempts/",
            new StartAttemptRequest { AnonymousName = "Schema Tester" });
        var json = await response.Content.ReadAsStringAsync();

        // Assert
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.TryGetProperty("id", out var id).Should().BeTrue();
        id.ValueKind.Should().Be(JsonValueKind.Number);
        id.GetInt32().Should().BePositive();

        root.TryGetProperty("started_at", out var startedAt).Should().BeTrue();
        startedAt.ValueKind.Should().Be(JsonValueKind.String,
            "started_at should be an ISO8601 timestamp string");
    }

    [Fact]
    [Trait("Category", "Schema")]
    [Trait("Priority", "P1")]
    public async Task Schema_ResultResponse_HasRequiredFields()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"ResultSchema_{Guid.NewGuid().ToString("N")[..8]}");
        await TestDataHelper.AddQuestionAsync(_apiClient, test.Slug, "Result question");

        var startResp = await _anonClient.PostAsync($"tests/{test.Slug}/attempts/",
            new StartAttemptRequest { AnonymousName = "Result Tester" });
        var attempt = await _anonClient.DeserializeResponseAsync<AttemptResponse>(startResp);
        await _anonClient.PostAsync($"tests/{test.Slug}/attempts/{attempt!.Id}/submit/",
            new Dictionary<string, object>());

        // Act: fetch results as author
        var response = await _apiClient.GetAsync($"tests/{test.Slug}/results/");
        var json = await response.Content.ReadAsStringAsync();

        // Assert
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.ValueKind.Should().Be(JsonValueKind.Array,
            "GET /results/ should return an array");

        var result = root.EnumerateArray().First();
        result.TryGetProperty("id", out var id).Should().BeTrue();
        id.ValueKind.Should().Be(JsonValueKind.Number);

        result.TryGetProperty("anonymous_name", out var name).Should().BeTrue();
        name.ValueKind.Should().Be(JsonValueKind.String);

        result.TryGetProperty("score", out var score).Should().BeTrue();
        score.ValueKind.Should().Be(JsonValueKind.Number);

        result.TryGetProperty("submitted_at", out var submittedAt).Should().BeTrue();
        submittedAt.ValueKind.Should().Be(JsonValueKind.String,
            "submitted_at should be an ISO8601 timestamp");

        result.TryGetProperty("correct_answers", out var correct).Should().BeTrue();
        correct.ValueKind.Should().Be(JsonValueKind.Number);

        result.TryGetProperty("total_questions", out var total).Should().BeTrue();
        total.ValueKind.Should().Be(JsonValueKind.Number);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Analytics Schema
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    [Trait("Category", "Schema")]
    [Trait("Priority", "P1")]
    public async Task Schema_AnalyticsResponse_HasRequiredFields()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"AnalyticsSchema_{Guid.NewGuid().ToString("N")[..8]}");
        await TestDataHelper.AddQuestionAsync(_apiClient, test.Slug, "Analytics Q");

        var startResp = await _anonClient.PostAsync($"tests/{test.Slug}/attempts/",
            new StartAttemptRequest { AnonymousName = "Analytics Tester" });
        var attempt = await _anonClient.DeserializeResponseAsync<AttemptResponse>(startResp);
        await _anonClient.PostAsync($"tests/{test.Slug}/attempts/{attempt!.Id}/submit/",
            new Dictionary<string, object>());

        // Act
        var response = await _apiClient.GetAsync($"analytics/tests/{test.Slug}/");
        var json = await response.Content.ReadAsStringAsync();

        // Assert
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.TryGetProperty("total_attempts", out var totalAttempts).Should().BeTrue();
        totalAttempts.ValueKind.Should().Be(JsonValueKind.Number);

        root.TryGetProperty("average_score", out var avgScore).Should().BeTrue();
        (avgScore.ValueKind == JsonValueKind.Number || avgScore.ValueKind == JsonValueKind.Null)
            .Should().BeTrue("average_score can be null when no valid scores exist");

        root.TryGetProperty("question_stats", out var qStats).Should().BeTrue();
        qStats.ValueKind.Should().Be(JsonValueKind.Array);

        if (qStats.GetArrayLength() > 0)
        {
            var stat = qStats.EnumerateArray().First();
            stat.TryGetProperty("question_id", out var qId).Should().BeTrue();
            qId.ValueKind.Should().Be(JsonValueKind.Number);

            stat.TryGetProperty("question_text", out var qText).Should().BeTrue();
            qText.ValueKind.Should().Be(JsonValueKind.String);

            stat.TryGetProperty("answer_distribution", out var dist).Should().BeTrue();
            dist.ValueKind.Should().Be(JsonValueKind.Array);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Company Schemas
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    [Trait("Category", "Schema")]
    [Trait("Priority", "P1")]
    public async Task Schema_CompanyResponse_HasRequiredFields()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);

        // Act
        var response = await _apiClient.PostAsync("companies/",
            new CreateCompanyRequest { Name = $"SchemaCompany_{Guid.NewGuid().ToString("N")[..8]}" });
        var json = await response.Content.ReadAsStringAsync();

        // Assert
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.TryGetProperty("id", out var id).Should().BeTrue();
        id.ValueKind.Should().Be(JsonValueKind.Number);

        root.TryGetProperty("name", out var name).Should().BeTrue();
        name.ValueKind.Should().Be(JsonValueKind.String);

        root.TryGetProperty("created_at", out var createdAt).Should().BeTrue();
        createdAt.ValueKind.Should().Be(JsonValueKind.String);
    }

    public void Dispose()
    {
        _apiClient?.Dispose();
        _anonClient?.Dispose();
    }
}
