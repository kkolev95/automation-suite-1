using TestIT.ApiTests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace TestIT.ApiTests.Tests;

/// <summary>
/// Simple cleanup tests - run these to delete your stress test data
/// </summary>
[Collection("Cleanup")]
public class SimpleCleanupTests
{
    private readonly ITestOutputHelper _output;

    public SimpleCleanupTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Cleanup_UsingAdminAccount_DeleteVisibleTests()
    {
        var baseUrl = TestConfiguration.GetBaseUrl();
        using var cleanup = new SimpleCleanup(baseUrl);

        _output.WriteLine("═══════════════════════════════════════════════════════════");
        _output.WriteLine("  Simple Test Data Cleanup");
        _output.WriteLine("═══════════════════════════════════════════════════════════");
        _output.WriteLine("");

        // Login with the SAME persistent account that stress tests use
        _output.WriteLine($"Logging in with persistent test user: {PersistentTestUser.Email}");
        var loggedIn = await cleanup.LoginAsync(PersistentTestUser.Email, PersistentTestUser.Password);

        if (!loggedIn)
        {
            _output.WriteLine("✗ Failed to login. Trying to register new admin user...");

            // If admin doesn't exist, register a new user
            var registerClient = new ApiClient(baseUrl);
            var email = $"cleanup_{Guid.NewGuid().ToString("N")[..8]}@example.com";
            var password = "Cleanup123!";

            var registerRequest = new Models.RegisterRequest
            {
                Email = email,
                Password = password,
                PasswordConfirm = password,
                FirstName = "Cleanup",
                LastName = "User"
            };

            var registerResponse = await registerClient.PostAsync("auth/register/", registerRequest);
            if (registerResponse.StatusCode != System.Net.HttpStatusCode.Created)
            {
                _output.WriteLine("✗ Failed to register cleanup user");
                return;
            }

            loggedIn = await cleanup.LoginAsync(email, password);
            if (!loggedIn)
            {
                _output.WriteLine("✗ Failed to login with new user");
                return;
            }

            _output.WriteLine($"✓ Registered and logged in as: {email}");
        }
        else
        {
            _output.WriteLine("✓ Logged in successfully");
        }

        _output.WriteLine("");
        _output.WriteLine("Fetching visible tests...");
        var allTests = await cleanup.GetVisibleTestsAsync();
        _output.WriteLine($"Found {allTests.Count} visible tests");
        _output.WriteLine("");

        if (allTests.Count == 0)
        {
            _output.WriteLine("═══════════════════════════════════════════════════════════");
            _output.WriteLine("  No tests visible to this user");
            _output.WriteLine("═══════════════════════════════════════════════════════════");
            _output.WriteLine("");
            _output.WriteLine("This means:");
            _output.WriteLine("  • Tests were created by different users");
            _output.WriteLine("  • You need database access or admin privileges");
            _output.WriteLine("");
            _output.WriteLine("Options:");
            _output.WriteLine("  1. Ask server admin to run cleanup-db.sql");
            _output.WriteLine("  2. Use Django admin panel to delete tests");
            _output.WriteLine("  3. Get superuser API credentials");
            _output.WriteLine("");
            return;
        }

        // Show sample tests
        _output.WriteLine("Sample tests found:");
        foreach (var test in allTests.Take(10))
        {
            _output.WriteLine($"  • {test.Title} ({test.Slug})");
        }
        if (allTests.Count > 10)
        {
            _output.WriteLine($"  ... and {allTests.Count - 10} more");
        }
        _output.WriteLine("");

        _output.WriteLine("Deleting test data...");
        _output.WriteLine("");

        var deleted = await cleanup.DeleteAllTestDataAsync();

        _output.WriteLine("");
        _output.WriteLine("═══════════════════════════════════════════════════════════");
        _output.WriteLine($"  ✓ Cleanup Complete: Deleted {deleted} tests");
        _output.WriteLine("═══════════════════════════════════════════════════════════");
    }

    [Fact]
    public async Task Cleanup_BySlugList_DeleteSpecificTests()
    {
        var baseUrl = TestConfiguration.GetBaseUrl();
        using var cleanup = new SimpleCleanup(baseUrl);

        _output.WriteLine("═══════════════════════════════════════════════════════════");
        _output.WriteLine("  Delete Tests By Slug");
        _output.WriteLine("═══════════════════════════════════════════════════════════");
        _output.WriteLine("");

        // Login with the SAME persistent account that stress tests use
        _output.WriteLine($"Logging in with persistent test user: {PersistentTestUser.Email}");
        var loggedIn = await cleanup.LoginAsync(PersistentTestUser.Email, PersistentTestUser.Password);

        if (!loggedIn)
        {
            // Try registering
            var registerClient = new ApiClient(baseUrl);
            var email = $"cleanup_{Guid.NewGuid().ToString("N")[..8]}@example.com";
            var password = "Cleanup123!";

            var registerRequest = new Models.RegisterRequest
            {
                Email = email,
                Password = password,
                PasswordConfirm = password,
                FirstName = "Cleanup",
                LastName = "User"
            };

            await registerClient.PostAsync("auth/register/", registerRequest);
            loggedIn = await cleanup.LoginAsync(email, password);
        }

        if (!loggedIn)
        {
            _output.WriteLine("✗ Failed to login");
            return;
        }

        _output.WriteLine("✓ Logged in successfully");
        _output.WriteLine("");

        // Get all visible tests and show their slugs
        var tests = await cleanup.GetVisibleTestsAsync();
        _output.WriteLine($"Found {tests.Count} visible tests:");
        _output.WriteLine("");

        if (tests.Count == 0)
        {
            _output.WriteLine("No tests found. They may belong to other users.");
            return;
        }

        foreach (var test in tests)
        {
            _output.WriteLine($"  {test.Slug} - {test.Title}");
        }

        _output.WriteLine("");
        _output.WriteLine("Copy slugs you want to delete and add them to the slugsToDelete list in this test.");
        _output.WriteLine("");

        // Example: Delete specific tests (edit this list)
        var slugsToDelete = new List<string>
        {
            // Add your test slugs here, for example:
            // "stress-abc123-...",
            // "integritytest-def456-...",
        };

        if (slugsToDelete.Count == 0)
        {
            _output.WriteLine("No slugs specified. Edit this test to add slugs to delete.");
            return;
        }

        _output.WriteLine($"Deleting {slugsToDelete.Count} tests...");
        var (deleted, failed, failedSlugs) = await cleanup.DeleteTestsAsync(slugsToDelete);

        _output.WriteLine("");
        _output.WriteLine("═══════════════════════════════════════════════════════════");
        _output.WriteLine($"  Results: {deleted} deleted, {failed} failed");
        _output.WriteLine("═══════════════════════════════════════════════════════════");

        if (failedSlugs.Count > 0)
        {
            _output.WriteLine("");
            _output.WriteLine("Failed to delete:");
            foreach (var slug in failedSlugs)
            {
                _output.WriteLine($"  • {slug}");
            }
        }
    }
}
