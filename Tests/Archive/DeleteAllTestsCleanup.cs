using System.Net;
using TestIT.ApiTests.Helpers;
using TestIT.ApiTests.Models;
using Xunit;
using Xunit.Abstractions;

namespace TestIT.ApiTests.Tests;

/// <summary>
/// Cleanup that tries multiple user accounts to find and delete ALL test data
/// </summary>
[Collection("Cleanup")]
public class DeleteAllTestsCleanup
{
    private readonly ITestOutputHelper _output;

    public DeleteAllTestsCleanup(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task DeleteAllTests_TryMultipleAccounts()
    {
        var baseUrl = TestConfiguration.GetBaseUrl();
        int totalDeleted = 0;

        _output.WriteLine("═══════════════════════════════════════════════════════════");
        _output.WriteLine("  Delete ALL Test Data - Multi-Account Cleanup");
        _output.WriteLine("═══════════════════════════════════════════════════════════");
        _output.WriteLine("");

        // List of accounts to try (add more if needed)
        var accountsToTry = new List<(string Email, string Password)>
        {
            // Persistent test user (current tests)
            (PersistentTestUser.Email, PersistentTestUser.Password),

            // Admin account
            ("admin@test.com", "Admin123!"),

            // Try some common test user patterns (old tests)
            // Note: These are guesses - we don't know exact emails from old runs
        };

        // Try each account
        foreach (var (email, password) in accountsToTry)
        {
            _output.WriteLine($"Trying account: {email}");

            using var client = new ApiClient(baseUrl);

            // Try to login
            bool loggedIn = false;
            try
            {
                var loginResponse = await client.PostAsync<LoginRequest, LoginResponse>(
                    "auth/login/",
                    new LoginRequest { Email = email, Password = password });

                if (loginResponse?.AccessToken != null)
                {
                    client.SetAuthToken(loginResponse.AccessToken);
                    loggedIn = true;
                    _output.WriteLine($"  ✓ Logged in");
                }
            }
            catch
            {
                _output.WriteLine($"  ✗ Login failed (account doesn't exist or wrong password)");
                continue;
            }

            if (!loggedIn) continue;

            // Get all tests visible to this user
            try
            {
                var response = await client.GetAsync("tests/");
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    _output.WriteLine($"  ✗ Failed to get tests");
                    continue;
                }

                var tests = await client.DeserializeResponseAsync<List<TestResponse>>(response);
                if (tests == null || tests.Count == 0)
                {
                    _output.WriteLine($"  • No tests found for this user");
                    continue;
                }

                _output.WriteLine($"  • Found {tests.Count} tests");

                // Delete each test
                int deleted = 0;
                foreach (var test in tests)
                {
                    // Only delete test data (matching patterns)
                    if (IsTestData(test.Title))
                    {
                        try
                        {
                            var deleteResponse = await client.DeleteAsync($"tests/{test.Slug}/");
                            if (deleteResponse.StatusCode == HttpStatusCode.NoContent ||
                                deleteResponse.StatusCode == HttpStatusCode.OK)
                            {
                                deleted++;
                                totalDeleted++;
                                _output.WriteLine($"    ✓ Deleted: {test.Title}");
                            }
                            else
                            {
                                _output.WriteLine($"    ✗ Failed to delete: {test.Title} ({deleteResponse.StatusCode})");
                            }
                        }
                        catch (Exception ex)
                        {
                            _output.WriteLine($"    ✗ Error deleting {test.Title}: {ex.Message}");
                        }

                        await Task.Delay(50); // Avoid rate limiting
                    }
                }

                _output.WriteLine($"  • Deleted {deleted} tests from this account");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"  ✗ Error processing tests: {ex.Message}");
            }

            _output.WriteLine("");
        }

        _output.WriteLine("═══════════════════════════════════════════════════════════");
        _output.WriteLine($"  Total Deleted: {totalDeleted} tests");
        _output.WriteLine("═══════════════════════════════════════════════════════════");
        _output.WriteLine("");

        if (totalDeleted == 0)
        {
            _output.WriteLine("⚠ WARNING: No tests were deleted!");
            _output.WriteLine("");
            _output.WriteLine("This means:");
            _output.WriteLine("  • Tests belong to other user accounts we don't know");
            _output.WriteLine("  • You need database access or admin help");
            _output.WriteLine("");
            _output.WriteLine("Options:");
            _output.WriteLine("  1. Send cleanup-now.sql to server admin");
            _output.WriteLine("  2. Use Django admin panel");
            _output.WriteLine("  3. Manually delete via web UI");
        }
        else
        {
            _output.WriteLine($"✓ Successfully deleted {totalDeleted} tests!");
        }
    }

    private bool IsTestData(string title)
    {
        var patterns = new[]
        {
            "Stress_", "IntegrityTest_", "Scorable_", "UpdateQ_", "DeleteQ_",
            "Reorder_", "MultiSelect_", "SingleQ_", "PublicTake_", "PwProtected_",
            "XSSSec_", "UpdateSec_", "DeleteSec_", "AnswerSec_", "DoubleSubmit_",
            "AnalyticsEmpty_", "AnalyticsTest_", "My Test", "Original Title",
            "Test To Delete", "Detailed Test", "Math Test", "JavaScript Basics"
        };

        return patterns.Any(p => title.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }
}
