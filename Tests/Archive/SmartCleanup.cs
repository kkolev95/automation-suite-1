using System.Net;
using System.Text.Json;
using TestIT.ApiTests.Helpers;
using TestIT.ApiTests.Models;
using Xunit;
using Xunit.Abstractions;

namespace TestIT.ApiTests.Tests;

/// <summary>
/// Smart cleanup that finds test users and deletes their tests
/// </summary>
[Collection("Cleanup")]
public class SmartCleanup
{
    private readonly ITestOutputHelper _output;

    public SmartCleanup(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task SmartCleanup_FindAndDeleteAllTestData()
    {
        var baseUrl = TestConfiguration.GetBaseUrl();
        int totalDeleted = 0;
        var testUserEmails = new List<string>();

        _output.WriteLine("═══════════════════════════════════════════════════════════");
        _output.WriteLine("  Smart Cleanup - Finding Test Users");
        _output.WriteLine("═══════════════════════════════════════════════════════════");
        _output.WriteLine("");

        // Strategy 1: Try to get list of users from API
        _output.WriteLine("Strategy 1: Trying to list users via API...");
        using var adminClient = new ApiClient(baseUrl);

        // Try to get admin access
        var adminEmails = new[] { "admin@test.com", "admin@example.com" };
        var adminPasswords = new[] { "Admin123!", "admin123", "password" };

        bool hasAdminAccess = false;
        foreach (var email in adminEmails)
        {
            foreach (var pass in adminPasswords)
            {
                try
                {
                    var loginResp = await adminClient.PostAsync<LoginRequest, LoginResponse>(
                        "auth/login/",
                        new LoginRequest { Email = email, Password = pass });

                    if (loginResp?.AccessToken != null)
                    {
                        adminClient.SetAuthToken(loginResp.AccessToken);
                        hasAdminAccess = true;
                        _output.WriteLine($"  ✓ Logged in as admin: {email}");
                        break;
                    }
                }
                catch { }
            }
            if (hasAdminAccess) break;
        }

        if (hasAdminAccess)
        {
            // Try to get users list
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
                                       u.Email.StartsWith("stress_") ||
                                       u.Email.StartsWith("cleanup_admin_"))
                            .Select(u => u.Email));

                        _output.WriteLine($"  ✓ Found {testUserEmails.Count} test users via API");
                    }
                }
            }
            catch (Exception ex)
            {
                _output.WriteLine($"  • Users endpoint not available: {ex.Message}");
            }
        }
        else
        {
            _output.WriteLine("  • No admin access, skipping user list");
        }

        // Strategy 2: Check test responses for owner information
        _output.WriteLine("");
        _output.WriteLine("Strategy 2: Checking tests for owner information...");

        if (hasAdminAccess)
        {
            try
            {
                var testsResp = await adminClient.GetAsync("tests/");
                if (testsResp.StatusCode == HttpStatusCode.OK)
                {
                    var content = await testsResp.Content.ReadAsStringAsync();
                    var doc = JsonDocument.Parse(content);

                    // Look for owner_email or author_email fields
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var test in doc.RootElement.EnumerateArray())
                        {
                            if (test.TryGetProperty("owner_email", out var ownerEmail))
                            {
                                var email = ownerEmail.GetString();
                                if (!string.IsNullOrEmpty(email) &&
                                    (email.StartsWith("testuser_") || email.StartsWith("stress_")))
                                {
                                    if (!testUserEmails.Contains(email))
                                        testUserEmails.Add(email);
                                }
                            }
                            else if (test.TryGetProperty("author_email", out var authorEmail))
                            {
                                var email = authorEmail.GetString();
                                if (!string.IsNullOrEmpty(email) &&
                                    (email.StartsWith("testuser_") || email.StartsWith("stress_")))
                                {
                                    if (!testUserEmails.Contains(email))
                                        testUserEmails.Add(email);
                                }
                            }
                        }
                    }

                    _output.WriteLine($"  • Found {testUserEmails.Count} unique test users from tests");
                }
            }
            catch (Exception ex)
            {
                _output.WriteLine($"  • Could not extract owner info: {ex.Message}");
            }
        }

        // Strategy 3: Try persistent test user
        _output.WriteLine("");
        _output.WriteLine("Strategy 3: Checking persistent test user...");
        if (!testUserEmails.Contains(PersistentTestUser.Email))
        {
            testUserEmails.Add(PersistentTestUser.Email);
            _output.WriteLine($"  • Added persistent user: {PersistentTestUser.Email}");
        }

        _output.WriteLine("");
        _output.WriteLine($"Total test users to try: {testUserEmails.Count}");
        _output.WriteLine("");
        _output.WriteLine("═══════════════════════════════════════════════════════════");
        _output.WriteLine("  Deleting Tests");
        _output.WriteLine("═══════════════════════════════════════════════════════════");
        _output.WriteLine("");

        // For each test user, login and delete their tests
        foreach (var email in testUserEmails)
        {
            _output.WriteLine($"Processing user: {email}");

            using var client = new ApiClient(baseUrl);

            // We know all test users have the same password
            var password = "SecurePass123!";
            if (email == PersistentTestUser.Email)
                password = PersistentTestUser.Password;

            try
            {
                var loginResp = await client.PostAsync<LoginRequest, LoginResponse>(
                    "auth/login/",
                    new LoginRequest { Email = email, Password = password });

                if (loginResp?.AccessToken == null)
                {
                    _output.WriteLine($"  ✗ Could not login");
                    continue;
                }

                client.SetAuthToken(loginResp.AccessToken);
                _output.WriteLine($"  ✓ Logged in");

                // Get this user's tests
                var testsResp = await client.GetAsync("tests/");
                if (testsResp.StatusCode != HttpStatusCode.OK)
                {
                    _output.WriteLine($"  ✗ Could not get tests");
                    continue;
                }

                var tests = await client.DeserializeResponseAsync<List<TestResponse>>(testsResp);
                if (tests == null || tests.Count == 0)
                {
                    _output.WriteLine($"  • No tests found");
                    continue;
                }

                _output.WriteLine($"  • Found {tests.Count} tests");

                // Delete each test
                int deleted = 0;
                foreach (var test in tests)
                {
                    try
                    {
                        var deleteResp = await client.DeleteAsync($"tests/{test.Slug}/");
                        if (deleteResp.StatusCode == HttpStatusCode.NoContent ||
                            deleteResp.StatusCode == HttpStatusCode.OK)
                        {
                            deleted++;
                            totalDeleted++;
                            _output.WriteLine($"    ✓ Deleted: {test.Title}");
                        }
                    }
                    catch { }

                    await Task.Delay(50);
                }

                _output.WriteLine($"  • Deleted {deleted} tests");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"  ✗ Error: {ex.Message}");
            }

            _output.WriteLine("");
        }

        _output.WriteLine("═══════════════════════════════════════════════════════════");
        _output.WriteLine($"  Total Deleted: {totalDeleted} tests");
        _output.WriteLine("═══════════════════════════════════════════════════════════");

        if (totalDeleted == 0)
        {
            _output.WriteLine("");
            _output.WriteLine("⚠ No tests deleted. The test users are not accessible via API.");
            _output.WriteLine("");
            _output.WriteLine("You need:");
            _output.WriteLine("  1. Database access (use cleanup-now.sql)");
            _output.WriteLine("  2. Django admin panel");
            _output.WriteLine("  3. Contact server administrator");
        }
        else
        {
            _output.WriteLine("");
            _output.WriteLine($"✓ Successfully deleted {totalDeleted} tests!");
        }
    }

    private class UserInfo
    {
        public string Email { get; set; } = string.Empty;
        public int Id { get; set; }
    }
}
