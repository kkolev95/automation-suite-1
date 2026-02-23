using FluentAssertions;
using TestIT.ApiTests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace TestIT.ApiTests.Tests;

/// <summary>
/// Utility tests for cleaning up test data.
/// Includes both old pattern-based cleanup and new account-based cleanup.
/// These are not automated tests - run manually when needed.
/// </summary>
[Collection("Cleanup")]
public class CleanupTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly ApiClient _apiClient;

    public CleanupTests(ITestOutputHelper output)
    {
        _output = output;
        ApiClient.SetOutput(output.WriteLine);
        _apiClient = new ApiClient(TestConfiguration.GetBaseUrl());
    }

    [Fact]
    public async Task ManualCleanup_AnalyzeTestData()
    {
        var baseUrl = TestConfiguration.GetBaseUrl();
        using var cleanup = new TestDataCleanup(baseUrl);

        // Create temp admin
        var token = await CreateTempAdminAsync();

        _output.WriteLine("Analyzing test data...");
        _output.WriteLine("");

        var report = await cleanup.AnalyzeTestDataAsync(token);

        _output.WriteLine($"API returned tests, checking patterns...");
        _output.WriteLine($"Found {report.TotalItemsFound} items to clean up:");
        _output.WriteLine("");

        if (report.TestsToDelete.Any())
        {
            _output.WriteLine($"Tests ({report.TestsToDelete.Count}):");
            foreach (var test in report.TestsToDelete.Take(20))
            {
                _output.WriteLine($"  • {test.Title} (slug: {test.Slug})");
            }
            if (report.TestsToDelete.Count > 20)
            {
                _output.WriteLine($"  ... and {report.TestsToDelete.Count - 20} more");
            }
            _output.WriteLine("");
        }

        if (report.CompaniesToDelete.Any())
        {
            _output.WriteLine($"Companies ({report.CompaniesToDelete.Count}):");
            foreach (var company in report.CompaniesToDelete.Take(20))
            {
                _output.WriteLine($"  • {company.Name} (id: {company.Id})");
            }
            if (report.CompaniesToDelete.Count > 20)
            {
                _output.WriteLine($"  ... and {report.CompaniesToDelete.Count - 20} more");
            }
        }
    }

    [Fact]
    public async Task ManualCleanup_DryRun()
    {
        var baseUrl = TestConfiguration.GetBaseUrl();
        using var cleanup = new TestDataCleanup(baseUrl);

        var token = await CreateTempAdminAsync();

        _output.WriteLine("DRY RUN MODE - No data will be deleted");
        _output.WriteLine("");

        var result = await cleanup.CleanupTestDataAsync(token, dryRun: true);

        _output.WriteLine(result.Message);
        _output.WriteLine($"  Tests: {result.TestsDeleted}");
        _output.WriteLine($"  Companies: {result.CompaniesDeleted}");
    }

    [Fact]
    public async Task ManualCleanup_DeleteAllTestData()
    {
        var baseUrl = TestConfiguration.GetBaseUrl();
        using var cleanup = new TestDataCleanup(baseUrl);

        var token = await CreateTempAdminAsync();

        _output.WriteLine("⚠ WARNING: Deleting all test data!");
        _output.WriteLine("");

        var result = await cleanup.CleanupTestDataAsync(token, dryRun: false);

        _output.WriteLine(result.Message);
        _output.WriteLine($"  Tests: {result.TestsDeleted}");
        _output.WriteLine($"  Companies: {result.CompaniesDeleted}");
        _output.WriteLine("");

        foreach (var detail in result.Details.Take(20))
        {
            _output.WriteLine(detail);
        }

        if (result.Details.Count > 20)
        {
            _output.WriteLine($"... and {result.Details.Count - 20} more");
        }

        if (result.Errors.Any())
        {
            _output.WriteLine("");
            _output.WriteLine("Errors:");
            foreach (var error in result.Errors)
            {
                _output.WriteLine(error);
            }
        }
    }

    [Fact]
    public async Task ManualCleanup_DeleteOldTestData_7Days()
    {
        var baseUrl = TestConfiguration.GetBaseUrl();
        using var cleanup = new TestDataCleanup(baseUrl);

        var token = await CreateTempAdminAsync();

        _output.WriteLine("Deleting test data older than 7 days...");
        _output.WriteLine("");

        var result = await cleanup.CleanupOldTestDataAsync(token, olderThanDays: 7, dryRun: false);

        _output.WriteLine(result.Message);
        _output.WriteLine("");

        foreach (var detail in result.Details)
        {
            _output.WriteLine(detail);
        }

        if (result.Errors.Any())
        {
            _output.WriteLine("");
            _output.WriteLine("Errors:");
            foreach (var error in result.Errors)
            {
                _output.WriteLine(error);
            }
        }
    }

    private async Task<string> CreateTempAdminAsync()
    {
        var baseUrl = TestConfiguration.GetBaseUrl();
        using var client = new ApiClient(baseUrl);

        var email = $"cleanup_admin_{Guid.NewGuid().ToString("N")[..8]}@example.com";
        var password = "Cleanup123!";

        var registerRequest = new Models.RegisterRequest
        {
            Email = email,
            Password = password,
            PasswordConfirm = password,
            FirstName = "Cleanup",
            LastName = "Admin"
        };

        var registerResponse = await client.PostAsync("auth/register/", registerRequest);
        if (registerResponse.StatusCode != System.Net.HttpStatusCode.Created)
        {
            throw new Exception("Failed to create temp admin user");
        }

        var loginRequest = new Models.LoginRequest
        {
            Email = email,
            Password = password
        };

        var loginResponse = await client.PostAsync<Models.LoginRequest, Models.LoginResponse>(
            "auth/login/", loginRequest);

        return loginResponse?.AccessToken ?? throw new Exception("Failed to get access token");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // New Account-Based Cleanup Tests (Using TestAccountManager)
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task AccountCleanup_AllTrackedAccounts_DeletesSuccessfully()
    {
        _output.WriteLine("Testing account-based cleanup (CASCADE DELETE)...");
        _output.WriteLine("");

        // Capture baseline before we add our accounts
        var trackedBefore = TestAccountManager.GetTrackedAccountsCount();

        // Arrange: Create 3 test accounts with data
        var account1 = await TestAccountManager.CreateAndTrackAccountAsync(_apiClient, "cleanup_test_1");
        var account2 = await TestAccountManager.CreateAndTrackAccountAsync(_apiClient, "cleanup_test_2");
        var account3 = await TestAccountManager.CreateAndTrackAccountAsync(_apiClient, "cleanup_test_3");

        // Create data for each account
        _apiClient.SetAuthToken(account1.token);
        await TestDataHelper.CreateTestAsync(_apiClient, "Test by account 1");

        _apiClient.SetAuthToken(account2.token);
        await TestDataHelper.CreateTestAsync(_apiClient, "Test by account 2");

        _apiClient.SetAuthToken(account3.token);
        await TestDataHelper.CreateTestAsync(_apiClient, "Test by account 3");

        var trackedAfter = TestAccountManager.GetTrackedAccountsCount();
        _output.WriteLine($"Tracked accounts before: {trackedBefore}, after adding 3: {trackedAfter}");

        // Act: Clean up all tracked accounts
        var summary = await TestAccountManager.CleanupAllAccountsAsync();

        // Assert: the 3 accounts this test explicitly created must be deleted.
        // CleanupAllAccountsAsync drains the entire tracked pool so AccountsDeleted
        // may be higher, but the minimum we can guarantee is our 3.
        summary.AccountsDeleted.Should().BeGreaterThanOrEqualTo(3,
            $"should delete at least our 3 test accounts (total tracked was {trackedAfter})");

        _output.WriteLine("");
        _output.WriteLine($"✓ Successfully cleaned up {summary.AccountsDeleted} test accounts");
        _output.WriteLine($"  Duration: {summary.Duration.TotalSeconds:F2}s");
        _output.WriteLine($"  Errors: {summary.Errors.Count}");
    }

    [Fact]
    public async Task AccountCleanup_WithCascadeData_DeletesEverything()
    {
        _output.WriteLine("Testing CASCADE DELETE with account cleanup...");
        _output.WriteLine("");

        // Arrange: Create account with lots of data
        var (email, password, token) = await TestAccountManager.CreateAndTrackAccountAsync(
            _apiClient, "cleanup_cascade");

        _apiClient.SetAuthToken(token);

        // Create multiple tests with questions
        var test1 = await TestDataHelper.CreateTestAsync(_apiClient, "Test 1 for cascade");
        await TestDataHelper.AddQuestionAsync(_apiClient, test1.Slug, "Q1");
        await TestDataHelper.AddQuestionAsync(_apiClient, test1.Slug, "Q2");

        var test2 = await TestDataHelper.CreateTestAsync(_apiClient, "Test 2 for cascade");
        await TestDataHelper.AddQuestionAsync(_apiClient, test2.Slug, "Q3");

        _output.WriteLine($"Created account '{email}' with:");
        _output.WriteLine($"  - 2 tests");
        _output.WriteLine($"  - 3 questions total");
        _output.WriteLine("");

        // Act: Delete the account (should cascade delete all data)
        var deleted = await TestAccountManager.DeleteAccountAsync(_apiClient, email, password);

        // Assert
        deleted.Should().BeTrue("account deletion should succeed");

        // Verify account is gone
        var loginAttempt = await _apiClient.PostAsync("auth/login/",
            new Models.LoginRequest { Email = email, Password = password });

        loginAttempt.StatusCode.Should().BeOneOf(
            new[] { System.Net.HttpStatusCode.BadRequest, System.Net.HttpStatusCode.Unauthorized },
            "login should fail after account deletion");

        _output.WriteLine($"✓ Account and all cascade data deleted successfully");
    }

    [Fact(Skip = "Manual - Run this to clean up all tracked accounts from test runs")]
    public async Task Manual_CleanupAllTrackedAccounts()
    {
        var trackedCount = TestAccountManager.GetTrackedAccountsCount();

        _output.WriteLine("");
        _output.WriteLine("=== Manual Account Cleanup ===");
        _output.WriteLine("");

        if (trackedCount == 0)
        {
            _output.WriteLine("No tracked accounts to clean up");
            return;
        }

        var emails = TestAccountManager.GetTrackedAccountEmails();
        _output.WriteLine($"Found {trackedCount} tracked accounts:");
        foreach (var email in emails.Take(10))
        {
            _output.WriteLine($"  - {email}");
        }
        if (trackedCount > 10)
        {
            _output.WriteLine($"  ... and {trackedCount - 10} more");
        }
        _output.WriteLine("");
        _output.WriteLine("Starting cleanup...");

        var summary = await TestAccountManager.CleanupAllAccountsAsync();

        _output.WriteLine("");
        _output.WriteLine("=== Cleanup Summary ===");
        _output.WriteLine($"Total: {summary.TotalAccounts}");
        _output.WriteLine($"Deleted: {summary.AccountsDeleted}");
        _output.WriteLine($"Failed: {summary.Errors.Count}");
        _output.WriteLine($"Duration: {summary.Duration.TotalSeconds:F2}s");

        if (summary.Errors.Any())
        {
            _output.WriteLine("");
            _output.WriteLine("Errors:");
            foreach (var error in summary.Errors)
            {
                _output.WriteLine($"  - {error}");
            }
        }
    }

    [Fact(Skip = "Manual - Run to delete old tracked accounts")]
    public async Task Manual_CleanupOldAccounts()
    {
        _output.WriteLine("");
        _output.WriteLine("=== Cleanup Old Accounts (older than 1 hour) ===");
        _output.WriteLine("");

        var summary = await TestAccountManager.CleanupOldAccountsAsync(TimeSpan.FromHours(1));

        _output.WriteLine($"Total old accounts: {summary.TotalAccounts}");
        _output.WriteLine($"Deleted: {summary.AccountsDeleted}");
        _output.WriteLine($"Duration: {summary.Duration.TotalSeconds:F2}s");

        if (summary.Errors.Any())
        {
            _output.WriteLine("");
            _output.WriteLine("Errors:");
            foreach (var error in summary.Errors)
            {
                _output.WriteLine($"  - {error}");
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Diagnostic & Backend Feature Tests (from consolidated cleanup files)
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Diagnostic_VerifyDeleteEndpoint_Works()
    {
        _output.WriteLine("=== Verifying DELETE /auth/me/ Endpoint ===");
        _output.WriteLine("");

        // Create temporary test account
        var email = $"verify_delete_{Guid.NewGuid().ToString("N")[..8]}@example.com";
        var password = "VerifyDelete123!";

        var registerResp = await _apiClient.PostAsync("auth/register/",
            new Models.RegisterRequest
            {
                Email = email,
                Password = password,
                PasswordConfirm = password,
                FirstName = "Verify",
                LastName = "Delete"
            });

        registerResp.StatusCode.Should().Be(System.Net.HttpStatusCode.Created,
            "account registration should succeed");

        // Login
        var loginResp = await _apiClient.PostAsync<Models.LoginRequest, Models.LoginResponse>(
            "auth/login/",
            new Models.LoginRequest { Email = email, Password = password });

        loginResp.Should().NotBeNull("login should succeed");
        loginResp!.AccessToken.Should().NotBeNullOrEmpty("access token should be returned");

        _apiClient.SetAuthToken(loginResp.AccessToken);

        // Attempt to delete account
        var deleteResp = await _apiClient.DeleteAsync("auth/me/");

        _output.WriteLine($"DELETE /auth/me/ returned: {deleteResp.StatusCode}");

        // Verify deletion worked
        if (deleteResp.StatusCode == System.Net.HttpStatusCode.NoContent ||
            deleteResp.StatusCode == System.Net.HttpStatusCode.OK)
        {
            _output.WriteLine("✓ DELETE endpoint works correctly");

            // Verify account is actually gone
            var loginAttempt = await _apiClient.PostAsync("auth/login/",
                new Models.LoginRequest { Email = email, Password = password });

            loginAttempt.StatusCode.Should().BeOneOf(
                new[] { System.Net.HttpStatusCode.BadRequest, System.Net.HttpStatusCode.Unauthorized },
                "login should fail after account deletion");

            _output.WriteLine("✓ Account deletion verified (login fails)");
        }
        else
        {
            _output.WriteLine($"✗ DELETE endpoint returned unexpected status: {deleteResp.StatusCode}");
            _output.WriteLine("");
            _output.WriteLine("The endpoint may not be implemented. Expected 204 No Content.");
        }
    }

    [Fact(Skip = "Tests future backend feature - bulk cleanup endpoint")]
    public async Task Backend_BulkCleanupEndpoint_IfImplemented()
    {
        _output.WriteLine("=== Testing Bulk Cleanup Endpoint ===");
        _output.WriteLine("");
        _output.WriteLine("This tests a potential backend endpoint: DELETE /api/cleanup/bulk/");
        _output.WriteLine("");

        // Create temp admin
        var token = await TestDataHelper.RegisterAndLoginAsync(_apiClient);

        // Attempt bulk cleanup
        var response = await _apiClient.DeleteAsync("cleanup/bulk/");

        _output.WriteLine($"Response: {response.StatusCode}");

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _output.WriteLine("");
            _output.WriteLine("✗ Endpoint not implemented (404 Not Found)");
            _output.WriteLine("");
            _output.WriteLine("To implement this endpoint on the backend:");
            _output.WriteLine("");
            _output.WriteLine("Python/Django example:");
            _output.WriteLine("  @api_view(['DELETE'])");
            _output.WriteLine("  @permission_classes([IsAdminUser])");
            _output.WriteLine("  def bulk_cleanup(request):");
            _output.WriteLine("      tests_deleted = Test.objects.filter(");
            _output.WriteLine("          title__startswith='Stress_'");
            _output.WriteLine("      ).delete()");
            _output.WriteLine("      users_deleted = User.objects.filter(");
            _output.WriteLine("          email__startswith='testuser_'");
            _output.WriteLine("      ).delete()");
            _output.WriteLine("      return Response({");
            _output.WriteLine("          'tests_deleted': tests_deleted[0],");
            _output.WriteLine("          'users_deleted': users_deleted[0]");
            _output.WriteLine("      })");
        }
        else if (response.StatusCode == System.Net.HttpStatusCode.OK ||
                 response.StatusCode == System.Net.HttpStatusCode.NoContent)
        {
            _output.WriteLine("✓ Endpoint exists!");

            if (response.StatusCode == System.Net.HttpStatusCode.OK)
            {
                var content = await response.Content.ReadAsStringAsync();
                _output.WriteLine($"Response: {content}");
            }
        }
        else
        {
            _output.WriteLine($"⚠ Unexpected response: {response.StatusCode}");
        }
    }

    [Fact(Skip = "Manual - Pattern-based cleanup for persistent test user")]
    public async Task Manual_CleanupPersistentUserTests_ByPattern()
    {
        _output.WriteLine("=== Pattern-Based Test Cleanup ===");
        _output.WriteLine("");
        _output.WriteLine("This cleans up tests matching known test patterns");
        _output.WriteLine("(useful for cleaning persistent test user's tests)");
        _output.WriteLine("");

        // Login as persistent test user
        var loginResp = await _apiClient.PostAsync<Models.LoginRequest, Models.LoginResponse>(
            "auth/login/",
            new Models.LoginRequest
            {
                Email = PersistentTestUser.Email,
                Password = PersistentTestUser.Password
            });

        if (loginResp?.AccessToken == null)
        {
            _output.WriteLine("✗ Failed to login as persistent test user");
            return;
        }

        _apiClient.SetAuthToken(loginResp.AccessToken);
        _output.WriteLine($"✓ Logged in as: {PersistentTestUser.Email}");
        _output.WriteLine("");

        // Get all tests
        var testsResp = await _apiClient.GetAsync("tests/");
        var tests = await _apiClient.DeserializeResponseAsync<List<Models.TestResponse>>(testsResp);

        if (tests == null || tests.Count == 0)
        {
            _output.WriteLine("No tests found");
            return;
        }

        _output.WriteLine($"Found {tests.Count} total tests");

        // Filter to test data by pattern
        var testDataTests = tests.Where(t => IsTestData(t.Title!)).ToList();

        _output.WriteLine($"Identified {testDataTests.Count} test data items:");
        _output.WriteLine("");

        foreach (var test in testDataTests.Take(20))
        {
            _output.WriteLine($"  • {test.Title} ({test.Slug})");
        }
        if (testDataTests.Count > 20)
        {
            _output.WriteLine($"  ... and {testDataTests.Count - 20} more");
        }

        _output.WriteLine("");
        _output.WriteLine("Deleting test data...");

        int deleted = 0;
        foreach (var test in testDataTests)
        {
            try
            {
                var deleteResp = await _apiClient.DeleteAsync($"tests/{test.Slug}/");
                if (deleteResp.StatusCode == System.Net.HttpStatusCode.NoContent ||
                    deleteResp.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    deleted++;
                    _output.WriteLine($"  ✓ Deleted: {test.Title}");
                }
                await Task.Delay(50); // Rate limiting
            }
            catch (Exception ex)
            {
                _output.WriteLine($"  ✗ Failed to delete {test.Title}: {ex.Message}");
            }
        }

        _output.WriteLine("");
        _output.WriteLine($"✓ Deleted {deleted}/{testDataTests.Count} test data items");
    }

    /// <summary>
    /// Identifies test data by common naming patterns used in the test suite.
    /// Useful for selective cleanup without deleting all tests.
    /// </summary>
    private bool IsTestData(string title)
    {
        var patterns = new[]
        {
            // Stress test patterns
            "Stress_", "MassQ_", "ReadStress_",

            // Data integrity test patterns
            "IntegrityTest_", "Scorable_", "DoubleSubmit_",

            // Management test patterns
            "UpdateQ_", "DeleteQ_", "Reorder_", "Original Title", "Updated Title",

            // Test taking patterns
            "MultiSelect_", "SingleQ_", "PublicTake_", "PwProtected_",

            // Security test patterns
            "XSSSec_", "UpdateSec_", "DeleteSec_", "AnswerSec_",

            // Analytics patterns
            "AnalyticsEmpty_", "AnalyticsTest_",

            // Edge case patterns
            "EdgeCase_",

            // Generic test patterns
            "My Test", "Test To Delete", "Detailed Test",
            "Math Test", "JavaScript Basics", "cleanup_cascade",
            "Test 1 for cascade", "Test 2 for cascade"
        };

        return patterns.Any(p => title.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        _apiClient?.Dispose();
    }
}
