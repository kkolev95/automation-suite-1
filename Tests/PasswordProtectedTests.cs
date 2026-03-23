using System.Net;
using System.Text.Json;
using FluentAssertions;
using TestIT.ApiTests.Helpers;
using TestIT.ApiTests.Models;
using Xunit;
using Xunit.Abstractions;

namespace TestIT.ApiTests.Tests;

/// <summary>
/// Verifies password-protected test access control end-to-end:
///   - Accessing /take/ without a password returns 403 with requires_password flag
///   - Correct password verification returns 200
///   - Wrong password is rejected with 400
///   - Empty password is rejected with 400
///   - After a password change the old password no longer works
///   - After a password change the new password is accepted
///   - Changing visibility to public removes the password gate entirely
/// </summary>
public class PasswordProtectedTests : IDisposable
{
    private readonly ApiClient _apiClient;
    private readonly ApiClient _anonClient;

    public PasswordProtectedTests(ITestOutputHelper output)
    {
        ApiClient.SetOutput(output.WriteLine);
        var baseUrl = TestConfiguration.GetBaseUrl();
        _apiClient = new ApiClient(baseUrl);
        _anonClient = new ApiClient(baseUrl);
    }

    [Fact]
    [Trait("Category", "Security")]
    [Trait("Priority", "P1")]
    public async Task PasswordProtected_NoPassword_Returns403WithRequiresPasswordFlag()
    {
        // Arrange: create a password-protected test
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"PwProt_{Guid.NewGuid().ToString("N")[..8]}",
            visibility: "password_protected",
            password: "secret123");

        // Act: anonymous access to /take/ without supplying a password
        var response = await _anonClient.GetAsync($"tests/{test.Slug}/take/");
        var body = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "accessing a password-protected test without the password must return 403");
        body.TrimStart().Should().StartWith("{",
            "403 response body must be a JSON object, not HTML");

        using var doc = JsonDocument.Parse(body);
        var hasFlag = doc.RootElement.TryGetProperty("requires_password", out var flagElem);
        hasFlag.Should().BeTrue("403 response must include a 'requires_password' field");
        flagElem.GetBoolean().Should().BeTrue(
            "'requires_password' must be true when no password is supplied");
    }

    [Fact]
    [Trait("Category", "Security")]
    [Trait("Priority", "P1")]
    public async Task PasswordProtected_CorrectPassword_VerifyReturns200()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"PwVerify_{Guid.NewGuid().ToString("N")[..8]}",
            visibility: "password_protected",
            password: "correct_pass");

        // Act: POST /tests/{slug}/verify-password/ with the correct password
        var response = await _anonClient.PostAsync(
            $"tests/{test.Slug}/verify-password/",
            new { password = "correct_pass" });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "verifying the correct password must return 200");
    }

    [Fact]
    [Trait("Category", "Security")]
    [Trait("Priority", "P1")]
    public async Task PasswordProtected_WrongPassword_VerifyReturns400()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"PwWrong_{Guid.NewGuid().ToString("N")[..8]}",
            visibility: "password_protected",
            password: "correct_pass");

        // Act: verify with wrong password
        var response = await _anonClient.PostAsync(
            $"tests/{test.Slug}/verify-password/",
            new { password = "totally_wrong_password" });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "verifying a wrong password must be rejected with 400");
    }

    [Fact]
    [Trait("Category", "Security")]
    [Trait("Priority", "P1")]
    public async Task PasswordProtected_EmptyPassword_VerifyReturns400()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"PwEmpty_{Guid.NewGuid().ToString("N")[..8]}",
            visibility: "password_protected",
            password: "actual_pass");

        // Act: verify with empty string
        var response = await _anonClient.PostAsync(
            $"tests/{test.Slug}/verify-password/",
            new { password = string.Empty });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "verifying an empty password must be rejected with 400");
    }

    [Fact]
    [Trait("Category", "Security")]
    [Trait("Priority", "P1")]
    public async Task PasswordProtected_AfterPasswordChange_OldPasswordIsRejected()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"PwOldRej_{Guid.NewGuid().ToString("N")[..8]}",
            visibility: "password_protected",
            password: "old_pass");

        // Update the password via PATCH
        var patchResp = await _apiClient.PatchAsync($"tests/{test.Slug}/",
            new { password = "new_pass" });
        patchResp.StatusCode.Should().Be(HttpStatusCode.OK, "PATCH to update password must succeed");

        // Act: try the old password
        var oldPwResp = await _anonClient.PostAsync(
            $"tests/{test.Slug}/verify-password/",
            new { password = "old_pass" });

        // Assert: old password must now be rejected
        oldPwResp.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "after changing the password the old password must no longer be accepted");
    }

    [Fact]
    [Trait("Category", "Security")]
    [Trait("Priority", "P1")]
    public async Task PasswordProtected_AfterPasswordChange_NewPasswordIsAccepted()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"PwNewOk_{Guid.NewGuid().ToString("N")[..8]}",
            visibility: "password_protected",
            password: "original_pass");

        // Update the password
        await _apiClient.PatchAsync($"tests/{test.Slug}/",
            new { password = "updated_pass" });

        // Act: verify with the new password
        var newPwResp = await _anonClient.PostAsync(
            $"tests/{test.Slug}/verify-password/",
            new { password = "updated_pass" });

        // Assert
        newPwResp.StatusCode.Should().Be(HttpStatusCode.OK,
            "after changing the password the new password must be accepted");
    }

    [Fact]
    [Trait("Category", "Security")]
    [Trait("Priority", "P2")]
    public async Task PasswordProtected_ChangedToPublic_BecomesFreelyAccessible()
    {
        // Arrange: create password-protected test and confirm it's blocked
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"PwToPublic_{Guid.NewGuid().ToString("N")[..8]}",
            visibility: "password_protected",
            password: "was_secret");

        var blockedResp = await _anonClient.GetAsync($"tests/{test.Slug}/take/");
        blockedResp.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "password-protected test must be inaccessible before the visibility change");

        // PATCH visibility to public
        var patchResp = await _apiClient.PatchAsync($"tests/{test.Slug}/",
            new { visibility = "public" });
        patchResp.StatusCode.Should().Be(HttpStatusCode.OK, "visibility change must succeed");

        // Act: anonymous access without any password
        var openResp = await _anonClient.GetAsync($"tests/{test.Slug}/take/");

        // Assert
        openResp.StatusCode.Should().Be(HttpStatusCode.OK,
            "after changing visibility to public the test must be freely accessible without a password");
    }

    public void Dispose()
    {
        _apiClient?.Dispose();
        _anonClient?.Dispose();
    }
}
