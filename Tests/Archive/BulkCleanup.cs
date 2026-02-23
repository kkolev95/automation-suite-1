using System.Net;
using System.Text.Json;
using TestIT.ApiTests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace TestIT.ApiTests.Tests;

/// <summary>
/// Bulk cleanup using the new DELETE /api/tests/cleanup/ endpoint
/// </summary>
[Collection("Cleanup")]
public class BulkCleanup
{
    private readonly ITestOutputHelper _output;

    public BulkCleanup(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task BulkCleanup_DeleteAllTestData()
    {
        var baseUrl = TestConfiguration.GetBaseUrl();
        using var client = new ApiClient(baseUrl);

        _output.WriteLine("═══════════════════════════════════════════════════════════");
        _output.WriteLine("  Bulk Cleanup - One API Call");
        _output.WriteLine("═══════════════════════════════════════════════════════════");
        _output.WriteLine("");

        // Login with persistent test user (or any user with permissions)
        _output.WriteLine($"Logging in as: {PersistentTestUser.Email}");
        var token = await PersistentTestUser.GetOrCreateAndLoginAsync(client);
        _output.WriteLine("✓ Logged in");
        _output.WriteLine("");

        // Call bulk cleanup endpoint
        _output.WriteLine("Calling DELETE /api/tests/cleanup/...");
        var response = await client.DeleteAsync("tests/cleanup/");

        if (response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.NoContent)
        {
            _output.WriteLine("✓ Cleanup endpoint called successfully");
            _output.WriteLine("");

            // Try to parse response
            try
            {
                var content = await response.Content.ReadAsStringAsync();
                if (!string.IsNullOrEmpty(content))
                {
                    var result = JsonSerializer.Deserialize<CleanupResponse>(content,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (result != null)
                    {
                        _output.WriteLine("Results:");
                        _output.WriteLine($"  • Tests deleted: {result.TestsDeleted}");
                        _output.WriteLine($"  • Users deleted: {result.UsersDeleted}");
                        _output.WriteLine($"  • Message: {result.Message}");
                        _output.WriteLine("");
                        _output.WriteLine("═══════════════════════════════════════════════════════════");
                        _output.WriteLine($"  ✅ SUCCESS: Deleted {result.TestsDeleted} tests!");
                        _output.WriteLine("═══════════════════════════════════════════════════════════");
                        return;
                    }
                }
            }
            catch
            {
                // Response might be empty (204 No Content)
            }

            _output.WriteLine("═══════════════════════════════════════════════════════════");
            _output.WriteLine("  ✅ Cleanup completed successfully!");
            _output.WriteLine("═══════════════════════════════════════════════════════════");
        }
        else if (response.StatusCode == HttpStatusCode.NotFound)
        {
            _output.WriteLine("");
            _output.WriteLine("═══════════════════════════════════════════════════════════");
            _output.WriteLine("  ❌ ERROR: Cleanup endpoint not found");
            _output.WriteLine("═══════════════════════════════════════════════════════════");
            _output.WriteLine("");
            _output.WriteLine("The DELETE /api/tests/cleanup/ endpoint doesn't exist yet.");
            _output.WriteLine("");
            _output.WriteLine("To fix:");
            _output.WriteLine("  1. Add the cleanup endpoint to your Django backend");
            _output.WriteLine("  2. See: BACKEND_CLEANUP_ENDPOINT.md for code");
            _output.WriteLine("  3. Restart Django server");
            _output.WriteLine("  4. Run this test again");
        }
        else
        {
            _output.WriteLine("");
            _output.WriteLine("═══════════════════════════════════════════════════════════");
            _output.WriteLine($"  ❌ ERROR: Cleanup failed ({response.StatusCode})");
            _output.WriteLine("═══════════════════════════════════════════════════════════");
            _output.WriteLine("");

            var errorContent = await response.Content.ReadAsStringAsync();
            if (!string.IsNullOrEmpty(errorContent))
            {
                _output.WriteLine($"Response: {errorContent}");
            }
        }
    }

    private class CleanupResponse
    {
        public bool Success { get; set; }
        public int TestsDeleted { get; set; }
        public int UsersDeleted { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
