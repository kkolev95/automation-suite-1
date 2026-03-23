using System.Net;
using FluentAssertions;
using TestIT.ApiTests.Helpers;
using TestIT.ApiTests.Models;
using Xunit;
using Xunit.Abstractions;

namespace TestIT.ApiTests.Tests;

/// <summary>
/// Verifies question ordering guarantees:
///   - Each question receives an auto-assigned, positive integer order value
///   - Questions are returned in ascending order (order field, not insertion time)
///   - Questions added sequentially receive strictly increasing order values
///   - The /take/ endpoint returns questions in the same order as the author sees them
///   - The order field is present on both the author detail view and the take view
/// </summary>
public class QuestionOrderingTests : IDisposable
{
    private readonly ApiClient _apiClient;
    private readonly ApiClient _anonClient;

    public QuestionOrderingTests(ITestOutputHelper output)
    {
        ApiClient.SetOutput(output.WriteLine);
        var baseUrl = TestConfiguration.GetBaseUrl();
        _apiClient  = new ApiClient(baseUrl);
        _anonClient = new ApiClient(baseUrl);
    }

    [Fact]
    [Trait("Category", "Functional")]
    [Trait("Priority", "P1")]
    public async Task Questions_AutoAssignedOrder_IsPositiveInteger()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"OrderPos_{Guid.NewGuid().ToString("N")[..8]}");

        // Act: add 3 questions
        for (int i = 1; i <= 3; i++)
            await TestDataHelper.AddQuestionAsync(_apiClient, test.Slug, $"Question {i}");

        var detail = await _apiClient.GetAsync<TestResponse>($"tests/{test.Slug}/");

        // Assert: every question has a positive integer order
        detail!.Questions.Should().HaveCount(3, "three questions were added");
        detail.Questions!.Should().AllSatisfy(q =>
            q.Order.Should().BeGreaterThan(0,
                $"question '{q.QuestionText}' must have a positive order value, got {q.Order}"),
            "every question must have a positive order value");
    }

    [Fact]
    [Trait("Category", "Functional")]
    [Trait("Priority", "P1")]
    public async Task Questions_AddedSequentially_HaveStrictlyIncreasingOrder()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"OrderSeq_{Guid.NewGuid().ToString("N")[..8]}");

        // Act: add 4 questions in sequence
        for (int i = 1; i <= 4; i++)
            await TestDataHelper.AddQuestionAsync(_apiClient, test.Slug, $"Seq Question {i}");

        var detail = await _apiClient.GetAsync<TestResponse>($"tests/{test.Slug}/");
        var orders = detail!.Questions!.Select(q => q.Order).ToList();

        // Assert: each order must be strictly greater than the previous
        for (int i = 1; i < orders.Count; i++)
        {
            orders[i].Should().BeGreaterThan(orders[i - 1],
                $"question at index {i} (order={orders[i]}) must have a higher order than " +
                $"the preceding question (order={orders[i - 1]})");
        }
    }

    [Fact]
    [Trait("Category", "Functional")]
    [Trait("Priority", "P1")]
    public async Task Questions_ReturnedInAscendingOrderField_FromDetailEndpoint()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"OrderAsc_{Guid.NewGuid().ToString("N")[..8]}");

        var q1 = await TestDataHelper.AddQuestionAsync(_apiClient, test.Slug, "First question");
        var q2 = await TestDataHelper.AddQuestionAsync(_apiClient, test.Slug, "Second question");
        var q3 = await TestDataHelper.AddQuestionAsync(_apiClient, test.Slug, "Third question");

        // Act
        var detail = await _apiClient.GetAsync<TestResponse>($"tests/{test.Slug}/");
        var returnedOrders = detail!.Questions!.Select(q => q.Order).ToList();

        // Assert: the list must already be sorted ascending by order
        var sortedOrders = returnedOrders.OrderBy(o => o).ToList();
        returnedOrders.Should().Equal(sortedOrders,
            "the questions array must be returned sorted ascending by the order field");
    }

    [Fact]
    [Trait("Category", "Functional")]
    [Trait("Priority", "P1")]
    public async Task Questions_TakeEndpoint_ReturnsSameSequenceAsDetailEndpoint()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"OrderTake_{Guid.NewGuid().ToString("N")[..8]}",
            visibility: "public");

        await TestDataHelper.AddQuestionAsync(_apiClient, test.Slug, "Take Q1");
        await TestDataHelper.AddQuestionAsync(_apiClient, test.Slug, "Take Q2");
        await TestDataHelper.AddQuestionAsync(_apiClient, test.Slug, "Take Q3");

        // Act: compare question ID sequence from author detail vs anon /take/
        var detail = await _apiClient.GetAsync<TestResponse>($"tests/{test.Slug}/");
        var authorIds = detail!.Questions!.Select(q => q.Id).ToList();

        var take = await _anonClient.GetAsync<TakeTestResponse>($"tests/{test.Slug}/take/");
        var takeIds = take!.Questions.Select(q => q.Id).ToList();

        // Assert: both endpoints must list questions in the same sequence
        takeIds.Should().Equal(authorIds,
            "the /take/ endpoint must return questions in the same sequence as the author's detail view");
    }

    [Fact]
    [Trait("Category", "Functional")]
    [Trait("Priority", "P2")]
    public async Task Questions_OrderField_IsReturnedOnCreateResponse()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"OrderCreate_{Guid.NewGuid().ToString("N")[..8]}");

        // Act: create 2 questions and check the order field in the POST response
        var q1 = await TestDataHelper.AddQuestionAsync(_apiClient, test.Slug, "First");
        var q2 = await TestDataHelper.AddQuestionAsync(_apiClient, test.Slug, "Second");

        // Assert: the create response itself must include the assigned order
        q1.Order.Should().BeGreaterThan(0,
            "the POST /questions/ response must include a positive order value");
        q2.Order.Should().BeGreaterThan(q1.Order,
            "the second question's order must be greater than the first's as returned by create");
    }

    public void Dispose()
    {
        _apiClient?.Dispose();
        _anonClient?.Dispose();
    }
}
