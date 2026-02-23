using System.Net;
using TestIT.ApiTests.Models;

namespace TestIT.ApiTests.Helpers;

/// <summary>
/// Simple cleanup that deletes tests by slug pattern.
/// Works even when you can't see all tests via GET /tests/
/// </summary>
public class SimpleCleanup : IDisposable
{
    private readonly ApiClient _apiClient;

    public SimpleCleanup(string baseUrl)
    {
        _apiClient = new ApiClient(baseUrl);
    }

    /// <summary>
    /// Login with an existing user account
    /// </summary>
    public async Task<bool> LoginAsync(string email, string password)
    {
        try
        {
            var loginRequest = new LoginRequest
            {
                Email = email,
                Password = password
            };

            var response = await _apiClient.PostAsync<LoginRequest, LoginResponse>(
                "auth/login/", loginRequest);

            if (response?.AccessToken != null)
            {
                _apiClient.SetAuthToken(response.AccessToken);
                return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Get all visible tests
    /// </summary>
    public async Task<List<TestResponse>> GetVisibleTestsAsync()
    {
        try
        {
            var response = await _apiClient.GetAsync("tests/");
            if (response.StatusCode == HttpStatusCode.OK)
            {
                return await _apiClient.DeserializeResponseAsync<List<TestResponse>>(response)
                       ?? new List<TestResponse>();
            }
            return new List<TestResponse>();
        }
        catch
        {
            return new List<TestResponse>();
        }
    }

    /// <summary>
    /// Delete a single test by slug
    /// </summary>
    public async Task<bool> DeleteTestAsync(string slug)
    {
        try
        {
            var response = await _apiClient.DeleteAsync($"tests/{slug}/");
            return response.StatusCode == HttpStatusCode.NoContent ||
                   response.StatusCode == HttpStatusCode.OK;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Delete multiple tests by slug
    /// </summary>
    public async Task<(int Deleted, int Failed, List<string> FailedSlugs)> DeleteTestsAsync(List<string> slugs)
    {
        int deleted = 0;
        int failed = 0;
        var failedSlugs = new List<string>();

        foreach (var slug in slugs)
        {
            Console.WriteLine($"Attempting to delete: {slug}");
            var success = await DeleteTestAsync(slug);

            if (success)
            {
                deleted++;
                Console.WriteLine($"  ✓ Deleted");
            }
            else
            {
                failed++;
                failedSlugs.Add(slug);
                Console.WriteLine($"  ✗ Failed");
            }

            await Task.Delay(50); // Small delay to avoid rate limiting
        }

        return (deleted, failed, failedSlugs);
    }

    /// <summary>
    /// Delete all visible tests that match test patterns
    /// </summary>
    public async Task<int> DeleteAllTestDataAsync()
    {
        var tests = await GetVisibleTestsAsync();
        Console.WriteLine($"Found {tests.Count} visible tests");

        var testPatterns = new[]
        {
            "Stress_", "IntegrityTest_", "Scorable_", "UpdateQ_", "DeleteQ_",
            "Reorder_", "MultiSelect_", "SingleQ_", "PublicTake_", "PwProtected_",
            "XSSSec_", "UpdateSec_", "DeleteSec_", "AnswerSec_", "DoubleSubmit_",
            "AnalyticsEmpty_", "AnalyticsTest_", "My Test", "Original Title",
            "Test To Delete", "Detailed Test", "Math Test", "JavaScript Basics"
        };

        var testsToDelete = tests
            .Where(t => testPatterns.Any(pattern =>
                t.Title.StartsWith(pattern, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        Console.WriteLine($"Identified {testsToDelete.Count} tests to delete");

        int deleted = 0;
        foreach (var test in testsToDelete)
        {
            Console.WriteLine($"Deleting: {test.Title}");
            var success = await DeleteTestAsync(test.Slug);
            if (success)
            {
                deleted++;
                Console.WriteLine($"  ✓ Deleted");
            }
            else
            {
                Console.WriteLine($"  ✗ Failed");
            }

            await Task.Delay(50);
        }

        return deleted;
    }

    public void Dispose()
    {
        _apiClient?.Dispose();
    }
}
