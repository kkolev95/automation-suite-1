using System.Net;
using TestIT.ApiTests.Helpers;
using TestIT.ApiTests.Models;
using Xunit;
using Xunit.Abstractions;

namespace TestIT.ApiTests.Tests;

/// <summary>
/// Verify that DELETE /api/auth/me/ endpoint exists and works
/// </summary>
public class VerifyDeleteEndpoint
{
    private readonly ITestOutputHelper _output;

    public VerifyDeleteEndpoint(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task VerifyDeleteMeEndpoint_Exists()
    {
        var baseUrl = TestConfiguration.GetBaseUrl();
        using var client = new ApiClient(baseUrl);

        _output.WriteLine("═══════════════════════════════════════════════════════════");
        _output.WriteLine("  Verify DELETE /api/auth/me/ Endpoint");
        _output.WriteLine("═══════════════════════════════════════════════════════════");
        _output.WriteLine("");

        // Create a temporary test user
        var email = $"verify_delete_{Guid.NewGuid().ToString("N")[..8]}@example.com";
        var password = "VerifyTest123!";

        _output.WriteLine($"Step 1: Creating test user: {email}");

        var registerResp = await client.PostAsync("auth/register/", new RegisterRequest
        {
            Email = email,
            Password = password,
            PasswordConfirm = password,
            FirstName = "Delete",
            LastName = "Test"
        });

        if (registerResp.StatusCode != HttpStatusCode.Created)
        {
            _output.WriteLine("✗ Failed to create test user");
            return;
        }

        _output.WriteLine("✓ User created");
        _output.WriteLine("");

        // Login
        _output.WriteLine("Step 2: Logging in...");
        var loginResp = await client.PostAsync<LoginRequest, LoginResponse>(
            "auth/login/",
            new LoginRequest { Email = email, Password = password });

        if (loginResp?.AccessToken == null)
        {
            _output.WriteLine("✗ Failed to login");
            return;
        }

        client.SetAuthToken(loginResp.AccessToken);
        _output.WriteLine("✓ Logged in");
        _output.WriteLine("");

        // Try to delete the user
        _output.WriteLine("Step 3: Testing DELETE /api/auth/me/...");
        var deleteResp = await client.DeleteAsync("auth/me/");

        _output.WriteLine($"Response Status: {deleteResp.StatusCode}");
        _output.WriteLine("");

        if (deleteResp.StatusCode == HttpStatusCode.NotFound)
        {
            _output.WriteLine("═══════════════════════════════════════════════════════════");
            _output.WriteLine("  ❌ ENDPOINT DOES NOT EXIST");
            _output.WriteLine("═══════════════════════════════════════════════════════════");
            _output.WriteLine("");
            _output.WriteLine("The DELETE /api/auth/me/ endpoint is not implemented.");
            _output.WriteLine("");
            _output.WriteLine("You need to add it to your Django backend:");
            _output.WriteLine("");
            _output.WriteLine("@require_http_methods(['DELETE'])");
            _output.WriteLine("@login_required");
            _output.WriteLine("def delete_me(request):");
            _output.WriteLine("    user = request.user");
            _output.WriteLine("    user.delete()  # Cascade deletes tests");
            _output.WriteLine("    return HttpResponse(status=204)");
            _output.WriteLine("");
            _output.WriteLine("Add to urls.py:");
            _output.WriteLine("path('api/auth/me/', views.delete_me)");
        }
        else if (deleteResp.StatusCode == HttpStatusCode.NoContent ||
                 deleteResp.StatusCode == HttpStatusCode.OK ||
                 deleteResp.StatusCode == HttpStatusCode.Accepted)
        {
            _output.WriteLine("═══════════════════════════════════════════════════════════");
            _output.WriteLine("  ✅ ENDPOINT EXISTS AND WORKS!");
            _output.WriteLine("═══════════════════════════════════════════════════════════");
            _output.WriteLine("");
            _output.WriteLine("The DELETE /api/auth/me/ endpoint is working correctly.");
            _output.WriteLine("User was deleted successfully.");
            _output.WriteLine("");
            _output.WriteLine("Now verify that deleting users also deletes their tests:");
            _output.WriteLine("  1. Create a test user");
            _output.WriteLine("  2. Create tests with that user");
            _output.WriteLine("  3. Delete the user");
            _output.WriteLine("  4. Check if tests are gone");
        }
        else
        {
            _output.WriteLine("═══════════════════════════════════════════════════════════");
            _output.WriteLine($"  ⚠ UNEXPECTED RESPONSE: {deleteResp.StatusCode}");
            _output.WriteLine("═══════════════════════════════════════════════════════════");

            var content = await deleteResp.Content.ReadAsStringAsync();
            if (!string.IsNullOrEmpty(content))
            {
                _output.WriteLine("");
                _output.WriteLine($"Response: {content}");
            }
        }
    }
}
