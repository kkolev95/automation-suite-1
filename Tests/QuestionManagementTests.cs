using System.Net;
using FluentAssertions;
using TestIT.ApiTests.Helpers;
using TestIT.ApiTests.Models;
using Xunit;

namespace TestIT.ApiTests.Tests;

public class QuestionManagementTests : IDisposable
{
    private readonly ApiClient _apiClient;

    public QuestionManagementTests(ITestOutputHelper output)
    {
        ApiClient.SetOutput(output.WriteLine);
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

        // Verify the reorder actually took effect by re-fetching the test
        var testDetails = await _apiClient.GetAsync<TestResponse>($"tests/{test.Slug}/");
        testDetails.Should().NotBeNull();
        var orderedIds = testDetails!.Questions!
            .OrderBy(q => q.Order)
            .Select(q => q.Id)
            .ToList();
        orderedIds.Should().Equal(
            new[] { q3.Id, q2.Id, q1.Id },
            "questions should appear in the new order: q3 first, q2 second, q1 last");
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
