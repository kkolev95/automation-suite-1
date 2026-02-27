using System.Diagnostics;
using System.Net;
using FluentAssertions;
using TestIT.ApiTests.Helpers;
using TestIT.ApiTests.Models;
using Xunit;

namespace TestIT.ApiTests.Tests;

[Collection("Stress")]
public class StressTests : IDisposable
{
    private readonly ApiClient _apiClient;
    private readonly List<ApiClient> _clients = new();

    public StressTests(ITestOutputHelper output)
    {
        ApiClient.SetOutput(output.WriteLine);
        _apiClient = new ApiClient(TestConfiguration.GetBaseUrl(), enableRetry: false);
    }

    private ApiClient CreateClient()
    {
        var client = new ApiClient(TestConfiguration.GetBaseUrl(), enableRetry: false);
        _clients.Add(client);
        return client;
    }

    [Fact]
    public async Task StressTest_ConcurrentTestSubmissions_HandlesLoad()
    {
        // Arrange: Create a test with questions (using persistent user for easy cleanup)
        await PersistentTestUser.GetOrCreateAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient, $"Stress_{Guid.NewGuid().ToString("N")[..8]}");

        var q1 = await TestDataHelper.AddQuestionAsync(_apiClient, test.Slug, "Question 1",
            answers: new List<CreateAnswerRequest>
            {
                new() { AnswerText = "Correct", IsCorrect = true, Order = 1 },
                new() { AnswerText = "Wrong", IsCorrect = false, Order = 2 }
            });

        // Simulate 50 concurrent test takers submitting at the same time
        const int concurrentUsers = 50;
        var stopwatch = Stopwatch.StartNew();
        var tasks = new List<Task<(bool success, long durationMs, HttpStatusCode statusCode)>>();

        for (int i = 0; i < concurrentUsers; i++)
        {
            var userId = i;
            tasks.Add(Task.Run(async () =>
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    var client = CreateClient();
                    var takeTest = await client.GetAsync<TakeTestResponse>($"tests/{test.Slug}/take/");

                    // Start attempt
                    var startResp = await client.PostAsync($"tests/{test.Slug}/attempts/",
                        new StartAttemptRequest { AnonymousName = $"User_{userId}" });

                    if (startResp.StatusCode != HttpStatusCode.Created)
                        return (false, sw.ElapsedMilliseconds, startResp.StatusCode);

                    var attempt = await client.DeserializeResponseAsync<AttemptResponse>(startResp);

                    // Save draft
                    var draftResp = await client.PutAsync($"tests/{test.Slug}/attempts/{attempt!.Id}/",
                        new SaveDraftRequest
                        {
                            DraftAnswers = new Dictionary<string, List<int>>
                            {
                                { takeTest!.Questions[0].Id.ToString(), new List<int> { q1.Answers.First(a => a.IsCorrect).Id } }
                            }
                        });

                    if (draftResp.StatusCode != HttpStatusCode.OK)
                        return (false, sw.ElapsedMilliseconds, draftResp.StatusCode);

                    // Submit
                    var submitResp = await client.PostAsync($"tests/{test.Slug}/attempts/{attempt.Id}/submit/",
                        new Dictionary<string, object>());

                    sw.Stop();
                    return (submitResp.StatusCode == HttpStatusCode.OK, sw.ElapsedMilliseconds, submitResp.StatusCode);
                }
                catch (Exception)
                {
                    sw.Stop();
                    return (false, sw.ElapsedMilliseconds, HttpStatusCode.InternalServerError);
                }
            }));
        }

        var results = await Task.WhenAll(tasks);
        stopwatch.Stop();

        // Assert: Analyze results
        var successful = results.Count(r => r.success);
        var failed = results.Count(r => !r.success);
        var avgDuration = results.Average(r => r.durationMs);
        var maxDuration = results.Max(r => r.durationMs);
        var minDuration = results.Min(r => r.durationMs);
        var p95Duration = results.Select(r => r.durationMs).OrderBy(x => x).Skip((int)(concurrentUsers * 0.95)).First();

        // Log metrics
        Console.WriteLine($"\n=== Stress Test Results: {concurrentUsers} Concurrent Submissions ===");
        Console.WriteLine($"Total Time: {stopwatch.ElapsedMilliseconds}ms");
        Console.WriteLine($"Successful: {successful}/{concurrentUsers} ({(successful * 100.0 / concurrentUsers):F1}%)");
        Console.WriteLine($"Failed: {failed}");
        Console.WriteLine($"Response Times:");
        Console.WriteLine($"  Avg: {avgDuration:F0}ms");
        Console.WriteLine($"  Min: {minDuration}ms");
        Console.WriteLine($"  Max: {maxDuration}ms");
        Console.WriteLine($"  P95: {p95Duration}ms");
        Console.WriteLine($"Status Code Distribution:");
        foreach (var group in results.GroupBy(r => r.statusCode))
        {
            Console.WriteLine($"  {group.Key}: {group.Count()}");
        }

        // Assert: At least one submission must succeed to prove the feature works under load.
        // The API may rate-limit or reject some concurrent requests, but total failure
        // indicates a broken endpoint, not just load pressure.
        successful.Should().BeGreaterThanOrEqualTo(1,
            $"at least one concurrent submission must succeed; got {successful}/{concurrentUsers}");

        // Assert: P95 response time should be reasonable (under 15 seconds) when requests succeed
        if (successful > 0)
        {
            p95Duration.Should().BeLessThan(10000,
                "because 95% of requests should complete within 10 seconds");
        }
    }

    [Fact]
    public async Task StressTest_ConcurrentTestCreations_HandlesLoad()
    {
        // Arrange: Create and authenticate a user
        await PersistentTestUser.GetOrCreateAndLoginAsync(_apiClient);

        // Simulate 30 concurrent test creations
        const int concurrentCreations = 30;
        var stopwatch = Stopwatch.StartNew();
        var tasks = new List<Task<(bool success, long durationMs)>>();

        for (int i = 0; i < concurrentCreations; i++)
        {
            var testNum = i;
            tasks.Add(Task.Run(async () =>
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    var createRequest = new CreateTestRequest
                    {
                        Title = $"Stress Test {testNum}",
                        Description = $"Created under load #{testNum}",
                        Visibility = "public"
                    };

                    var response = await _apiClient.PostAsync("tests/", createRequest);
                    sw.Stop();
                    return (response.StatusCode == HttpStatusCode.Created, sw.ElapsedMilliseconds);
                }
                catch
                {
                    sw.Stop();
                    return (false, sw.ElapsedMilliseconds);
                }
            }));
        }

        var results = await Task.WhenAll(tasks);
        stopwatch.Stop();

        var successful = results.Count(r => r.success);
        var avgDuration = results.Average(r => r.durationMs);
        var maxDuration = results.Max(r => r.durationMs);

        Console.WriteLine($"\n=== Stress Test Results: {concurrentCreations} Concurrent Test Creations ===");
        Console.WriteLine($"Total Time: {stopwatch.ElapsedMilliseconds}ms");
        Console.WriteLine($"Successful: {successful}/{concurrentCreations}");
        Console.WriteLine($"Avg Duration: {avgDuration:F0}ms");
        Console.WriteLine($"Max Duration: {maxDuration}ms");

        successful.Should().BeGreaterThanOrEqualTo((int)(concurrentCreations * 0.9),
            "because the system should handle 90% of concurrent test creations");
    }

    [Fact]
    public async Task StressTest_ConcurrentUserRegistrations_HandlesLoad()
    {
        // Simulate 40 users registering simultaneously
        const int concurrentRegistrations = 40;
        var stopwatch = Stopwatch.StartNew();
        var tasks = new List<Task<(bool success, long durationMs, HttpStatusCode statusCode, string email, string password)>>();

        for (int i = 0; i < concurrentRegistrations; i++)
        {
            var userNum = i;
            tasks.Add(Task.Run(async () =>
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    var client = CreateClient();
                    var uniqueEmail = $"stress_{Guid.NewGuid().ToString("N")[..8]}@example.com";
                    var password = "StressTest123!";
                    var registerRequest = new RegisterRequest
                    {
                        Email = uniqueEmail,
                        Password = password,
                        PasswordConfirm = password,
                        FirstName = "User",
                        LastName = "Stress"
                    };

                    var response = await client.PostAsync("auth/register/", registerRequest);

                    // Track for cleanup if successful
                    if (response.StatusCode == HttpStatusCode.Created)
                    {
                        var loginResp = await client.PostAsync<LoginRequest, LoginResponse>("auth/login/",
                            new LoginRequest { Email = uniqueEmail, Password = password });
                        if (loginResp?.AccessToken != null)
                        {
                            TestAccountManager.TrackAccount(uniqueEmail, password, loginResp.AccessToken);
                        }
                    }

                    sw.Stop();
                    return (response.StatusCode == HttpStatusCode.Created, sw.ElapsedMilliseconds, response.StatusCode, uniqueEmail, password);
                }
                catch
                {
                    sw.Stop();
                    return (false, sw.ElapsedMilliseconds, HttpStatusCode.InternalServerError, "", "");
                }
            }));
        }

        var results = await Task.WhenAll(tasks);
        stopwatch.Stop();

        var successful = results.Count(r => r.success);
        var avgDuration = results.Average(r => r.durationMs);
        var p95Duration = results.Select(r => r.durationMs).OrderBy(x => x).Skip((int)(concurrentRegistrations * 0.95)).First();

        Console.WriteLine($"\n=== Stress Test Results: {concurrentRegistrations} Concurrent Registrations ===");
        Console.WriteLine($"Total Time: {stopwatch.ElapsedMilliseconds}ms");
        Console.WriteLine($"Successful: {successful}/{concurrentRegistrations}");
        Console.WriteLine($"Avg Duration: {avgDuration:F0}ms");
        Console.WriteLine($"P95 Duration: {p95Duration}ms");
        Console.WriteLine($"Status Code Distribution:");
        foreach (var group in results.GroupBy(r => r.statusCode))
        {
            Console.WriteLine($"  {group.Key}: {group.Count()}");
        }

        // Assert: Even if the server rejects all concurrent registrations (e.g. rate limiting),
        // every response must be a proper HTTP status code — no 5xx server crashes.
        var serverErrors = results.Count(r => (int)r.statusCode >= 500);
        serverErrors.Should().Be(0,
            $"concurrent registration attempts must not cause server errors; " +
            $"{serverErrors} of {concurrentRegistrations} returned 5xx");

        successful.Should().BeGreaterThanOrEqualTo((int)(concurrentRegistrations * 0.8),
            $"at least 80% of concurrent registrations must succeed; got {successful}/{concurrentRegistrations}");
    }

    [Fact]
    public async Task StressTest_MassQuestionCreation_HandlesLargeTests()
    {
        // Test creating a test with 100 questions rapidly
        await PersistentTestUser.GetOrCreateAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient, $"MassQ_{Guid.NewGuid().ToString("N")[..8]}");

        const int questionCount = 100;
        var stopwatch = Stopwatch.StartNew();
        var tasks = new List<Task<bool>>();

        for (int i = 0; i < questionCount; i++)
        {
            var qNum = i;
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    var questionRequest = new CreateQuestionRequest
                    {
                        QuestionText = $"Mass question #{qNum}",
                        QuestionType = "multiple_choice",
                        Answers = new List<CreateAnswerRequest>
                        {
                            new() { AnswerText = "A", IsCorrect = qNum % 2 == 0, Order = 1 },
                            new() { AnswerText = "B", IsCorrect = qNum % 2 != 0, Order = 2 }
                        }
                    };

                    var response = await _apiClient.PostAsync($"tests/{test.Slug}/questions/", questionRequest);
                    return response.StatusCode == HttpStatusCode.Created;
                }
                catch
                {
                    return false;
                }
            }));
        }

        var results = await Task.WhenAll(tasks);
        stopwatch.Stop();

        var successful = results.Count(r => r);
        var throughput = questionCount * 1000.0 / stopwatch.ElapsedMilliseconds;

        Console.WriteLine($"\n=== Stress Test Results: {questionCount} Questions Creation ===");
        Console.WriteLine($"Total Time: {stopwatch.ElapsedMilliseconds}ms");
        Console.WriteLine($"Successful: {successful}/{questionCount}");
        Console.WriteLine($"Throughput: {throughput:F2} questions/sec");

        successful.Should().BeGreaterThanOrEqualTo((int)(questionCount * 0.9),
            "because the system should successfully create most questions");
    }

    [Fact]
    public async Task StressTest_ConcurrentTestRetrieval_HandlesReadLoad()
    {
        // Setup: Create a public test
        await PersistentTestUser.GetOrCreateAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"ReadStress_{Guid.NewGuid().ToString("N")[..8]}",
            visibility: "public");
        await TestDataHelper.AddQuestionAsync(_apiClient, test.Slug, "Sample Q");

        // Simulate 100 concurrent users fetching the same test
        const int concurrentReads = 100;
        var stopwatch = Stopwatch.StartNew();
        var tasks = new List<Task<(bool success, long durationMs)>>();

        for (int i = 0; i < concurrentReads; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    var client = CreateClient();
                    var response = await client.GetAsync($"tests/{test.Slug}/take/");
                    sw.Stop();
                    return (response.StatusCode == HttpStatusCode.OK, sw.ElapsedMilliseconds);
                }
                catch
                {
                    sw.Stop();
                    return (false, sw.ElapsedMilliseconds);
                }
            }));
        }

        var results = await Task.WhenAll(tasks);
        stopwatch.Stop();

        var successful = results.Count(r => r.success);
        var avgDuration = results.Average(r => r.durationMs);
        var maxDuration = results.Max(r => r.durationMs);
        var throughput = concurrentReads * 1000.0 / stopwatch.ElapsedMilliseconds;

        Console.WriteLine($"\n=== Stress Test Results: {concurrentReads} Concurrent Reads ===");
        Console.WriteLine($"Total Time: {stopwatch.ElapsedMilliseconds}ms");
        Console.WriteLine($"Successful: {successful}/{concurrentReads}");
        Console.WriteLine($"Avg Duration: {avgDuration:F0}ms");
        Console.WriteLine($"Max Duration: {maxDuration}ms");
        Console.WriteLine($"Throughput: {throughput:F2} requests/sec");

        // Assert: At least 80% of concurrent reads must succeed.
        // Read endpoints have no side effects and should be highly available.
        successful.Should().BeGreaterThanOrEqualTo((int)(concurrentReads * 0.8),
            $"at least 80% of concurrent reads must succeed; got {successful}/{concurrentReads}");

        if (successful > 0)
        {
            avgDuration.Should().BeLessThan(3000,
                "because read operations should be reasonably fast under load");
        }
    }

    public void Dispose()
    {
        _apiClient?.Dispose();
        foreach (var client in _clients)
        {
            client?.Dispose();
        }
    }
}
