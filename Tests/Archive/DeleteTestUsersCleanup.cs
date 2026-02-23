using System.Net;
using System.Text.Json;
using TestIT.ApiTests.Helpers;
using TestIT.ApiTests.Models;
using Xunit;
using Xunit.Abstractions;

namespace TestIT.ApiTests.Tests;

/// <summary>
/// Cleanup by deleting test user accounts using DELETE /api/auth/me/
/// When users are deleted, their tests cascade delete
/// </summary>
[Collection("Cleanup")]
public class DeleteTestUsersCleanup
{
    private readonly ITestOutputHelper _output;

    public DeleteTestUsersCleanup(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task DeleteTestUsers_CascadeDeleteTests()
    {
        var baseUrl = TestConfiguration.GetBaseUrl();
        var testUserEmails = new List<string>();
        int usersDeleted = 0;

        _output.WriteLine("═══════════════════════════════════════════════════════════");
        _output.WriteLine("  Delete Test Users (Cascade Deletes Tests)");
        _output.WriteLine("═══════════════════════════════════════════════════════════");
        _output.WriteLine("");

        // Strategy 1: Try to get list of users via admin
        _output.WriteLine("Step 1: Finding test users...");
        using (var adminClient = new ApiClient(baseUrl))
        {
            // Try admin login
            var adminCredentials = new[]
            {
                ("admin@test.com", "Admin123!"),
                ("admin@example.com", "admin123"),
            };

            bool hasAdminAccess = false;
            foreach (var (email, password) in adminCredentials)
            {
                try
                {
                    var loginResp = await adminClient.PostAsync<LoginRequest, LoginResponse>(
                        "auth/login/",
                        new LoginRequest { Email = email, Password = password });

                    if (loginResp?.AccessToken != null)
                    {
                        adminClient.SetAuthToken(loginResp.AccessToken);
                        hasAdminAccess = true;
                        _output.WriteLine($"  ✓ Admin access: {email}");
                        break;
                    }
                }
                catch { }
            }

            // Try to get users list
            if (hasAdminAccess)
            {
                try
                {
                    var usersResp = await adminClient.GetAsync("users/");
                    if (usersResp.StatusCode == HttpStatusCode.OK)
                    {
                        var content = await usersResp.Content.ReadAsStringAsync();
                        var users = JsonSerializer.Deserialize<List<UserInfo>>(content,
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                        if (users != null)
                        {
                            testUserEmails.AddRange(users
                                .Where(u => u.Email.StartsWith("testuser_") ||
                                           u.Email.StartsWith("stress_test_") ||
                                           u.Email.StartsWith("cleanup_admin_"))
                                .Select(u => u.Email));

                            _output.WriteLine($"  ✓ Found {testUserEmails.Count} test users via API");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _output.WriteLine($"  • Users list not available: {ex.Message}");
                }
            }
        }

        // Strategy 2: Add known test users
        if (!testUserEmails.Contains(PersistentTestUser.Email))
        {
            testUserEmails.Add(PersistentTestUser.Email);
            _output.WriteLine($"  • Added persistent test user: {PersistentTestUser.Email}");
        }

        // Strategy 3: Try common test user patterns (brute force)
        // These are users that might have been created in previous runs
        var commonPatterns = new[]
        {
            "testuser_12345678@example.com",
            "stress_testuser@example.com",
        };

        foreach (var email in commonPatterns)
        {
            if (!testUserEmails.Contains(email))
            {
                testUserEmails.Add(email);
            }
        }

        if (testUserEmails.Count == 0)
        {
            _output.WriteLine("");
            _output.WriteLine("⚠ No test users found to delete");
            _output.WriteLine("");
            _output.WriteLine("Options:");
            _output.WriteLine("  1. The test users may have already been deleted");
            _output.WriteLine("  2. Use database cleanup instead (cleanup-now.sql)");
            _output.WriteLine("  3. Check if users endpoint exists: GET /api/users/");
            return;
        }

        _output.WriteLine("");
        _output.WriteLine($"Found {testUserEmails.Count} test users to delete");
        _output.WriteLine("");
        _output.WriteLine("═══════════════════════════════════════════════════════════");
        _output.WriteLine("  Deleting Users (and their tests)");
        _output.WriteLine("═══════════════════════════════════════════════════════════");
        _output.WriteLine("");

        // For each test user: login and delete account
        foreach (var email in testUserEmails)
        {
            _output.WriteLine($"Processing: {email}");

            using var client = new ApiClient(baseUrl);

            // Determine password (all test users use same password)
            var password = "SecurePass123!";
            if (email == PersistentTestUser.Email)
                password = PersistentTestUser.Password;

            // Try to login
            try
            {
                var loginResp = await client.PostAsync<LoginRequest, LoginResponse>(
                    "auth/login/",
                    new LoginRequest { Email = email, Password = password });

                if (loginResp?.AccessToken == null)
                {
                    _output.WriteLine("  ✗ Could not login (user may not exist or wrong password)");
                    continue;
                }

                client.SetAuthToken(loginResp.AccessToken);
                _output.WriteLine("  ✓ Logged in");

                // Delete the user account using DELETE /api/auth/me/
                _output.WriteLine("  • Calling DELETE /api/auth/me/...");
                var deleteResp = await client.DeleteAsync("auth/me/");

                if (deleteResp.StatusCode == HttpStatusCode.NoContent ||
                    deleteResp.StatusCode == HttpStatusCode.OK ||
                    deleteResp.StatusCode == HttpStatusCode.Accepted)
                {
                    usersDeleted++;
                    _output.WriteLine("  ✓ User deleted (tests cascade deleted)");
                }
                else if (deleteResp.StatusCode == HttpStatusCode.NotFound)
                {
                    _output.WriteLine("  ✗ DELETE /api/auth/me/ endpoint not found!");
                    _output.WriteLine("     The backend doesn't support user deletion via API.");
                }
                else
                {
                    _output.WriteLine($"  ✗ Failed to delete ({deleteResp.StatusCode})");
                    var errorContent = await deleteResp.Content.ReadAsStringAsync();
                    if (!string.IsNullOrEmpty(errorContent))
                    {
                        _output.WriteLine($"     Error: {errorContent}");
                    }
                }
            }
            catch (Exception ex)
            {
                _output.WriteLine($"  ✗ Error: {ex.Message}");
            }

            _output.WriteLine("");
            await Task.Delay(200); // Small delay between deletions
        }

        _output.WriteLine("═══════════════════════════════════════════════════════════");
        _output.WriteLine($"  Summary: {usersDeleted} users deleted");
        _output.WriteLine("═══════════════════════════════════════════════════════════");
        _output.WriteLine("");

        if (usersDeleted > 0)
        {
            _output.WriteLine($"✓ Successfully deleted {usersDeleted} test users");
            _output.WriteLine("  All tests owned by those users were cascade deleted");
        }
        else if (testUserEmails.Count > 0)
        {
            _output.WriteLine("⚠ No users were deleted");
            _output.WriteLine("");
            _output.WriteLine("Possible reasons:");
            _output.WriteLine("  • DELETE /api/auth/me/ endpoint doesn't exist");
            _output.WriteLine("  • Users don't exist or wrong passwords");
            _output.WriteLine("  • Permission issues");
            _output.WriteLine("");
            _output.WriteLine("Verify endpoint exists:");
            _output.WriteLine("  curl -X DELETE https://exampractices.com/api/auth/me/ \\");
            _output.WriteLine("    -H \"Authorization: Bearer YOUR_TOKEN\"");
        }
    }

    private class UserInfo
    {
        public string Email { get; set; } = string.Empty;
        public int Id { get; set; }
    }
}
