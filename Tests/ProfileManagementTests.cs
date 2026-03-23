using System.Net;
using FluentAssertions;
using TestIT.ApiTests.Helpers;
using TestIT.ApiTests.Models;
using Xunit;
using Xunit.Abstractions;

namespace TestIT.ApiTests.Tests;

/// <summary>
/// Verifies profile management via PATCH /auth/me/:
///   - Both names updated simultaneously persist correctly
///   - Partial update (first_name only) leaves last_name unchanged
///   - Empty first_name is rejected with 400
///   - Empty last_name is rejected with 400
///   - Unauthenticated PATCH is denied with 401
///   - Oversized name values are handled gracefully (never 5xx)
///   - Profile response does not expose any password field
/// </summary>
public class ProfileManagementTests : IDisposable
{
    private readonly ApiClient _apiClient;

    public ProfileManagementTests(ITestOutputHelper output)
    {
        ApiClient.SetOutput(output.WriteLine);
        _apiClient = new ApiClient(TestConfiguration.GetBaseUrl());
    }

    [Fact]
    [Trait("Category", "Functional")]
    [Trait("Priority", "P1")]
    public async Task Profile_UpdateBothNames_AllChangesPersist()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);

        // Act: update both names in a single PATCH
        var patchResp = await _apiClient.PatchAsync("auth/me/", new UpdateProfileRequest
        {
            FirstName = "UpdatedFirst",
            LastName  = "UpdatedLast"
        });
        patchResp.StatusCode.Should().Be(HttpStatusCode.OK,
            "PATCH /auth/me/ with both names must succeed");

        // Assert: subsequent GET reflects both changes
        var profile = await _apiClient.GetAsync<UserResponse>("auth/me/");
        profile!.FirstName.Should().Be("UpdatedFirst",
            "first_name must reflect the PATCHed value");
        profile.LastName.Should().Be("UpdatedLast",
            "last_name must reflect the PATCHed value");
    }

    [Fact]
    [Trait("Category", "Functional")]
    [Trait("Priority", "P1")]
    public async Task Profile_PartialUpdate_LeavesOtherFieldsUnchanged()
    {
        // Arrange: register with known, distinct names
        var email = $"partial_{Guid.NewGuid().ToString("N")[..8]}@example.com";
        const string password = "Test123!";
        await _apiClient.PostAsync("auth/register/", new RegisterRequest
        {
            Email = email, Password = password, PasswordConfirm = password,
            FirstName = "Original", LastName = "Surname"
        });
        var loginResp = await _apiClient.PostAsync<LoginRequest, LoginResponse>(
            "auth/login/", new LoginRequest { Email = email, Password = password });
        TestAccountManager.TrackAccount(email, password, loginResp!.AccessToken);
        _apiClient.SetAuthToken(loginResp.AccessToken);

        // Act: PATCH only first_name
        var patchResp = await _apiClient.PatchAsync("auth/me/", new UpdateProfileRequest
        {
            FirstName = "ChangedFirst"
        });
        patchResp.StatusCode.Should().Be(HttpStatusCode.OK, "partial PATCH must succeed");

        // Assert: first_name changed, last_name untouched
        var profile = await _apiClient.GetAsync<UserResponse>("auth/me/");
        profile!.FirstName.Should().Be("ChangedFirst",
            "first_name must reflect the PATCH update");
        profile.LastName.Should().Be("Surname",
            "last_name must be unchanged by a partial PATCH that did not include it");
    }

    [Fact]
    [Trait("Category", "Validation")]
    [Trait("Priority", "P1")]
    public async Task Profile_UpdateWithEmptyFirstName_IsRejected()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);

        // Act
        var response = await _apiClient.PatchAsync("auth/me/", new UpdateProfileRequest
        {
            FirstName = string.Empty
        });

        // Assert: empty name fields are not valid
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "PATCH /auth/me/ with empty first_name must be rejected with 400");
    }

    [Fact]
    [Trait("Category", "Validation")]
    [Trait("Priority", "P1")]
    public async Task Profile_UpdateWithEmptyLastName_IsRejected()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);

        // Act
        var response = await _apiClient.PatchAsync("auth/me/", new UpdateProfileRequest
        {
            LastName = string.Empty
        });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "PATCH /auth/me/ with empty last_name must be rejected with 400");
    }

    [Fact]
    [Trait("Category", "Security")]
    [Trait("Priority", "P1")]
    public async Task Profile_UnauthenticatedPatch_Returns401()
    {
        // Arrange: no token set
        _apiClient.ClearAuthToken();

        // Act
        var response = await _apiClient.PatchAsync("auth/me/", new UpdateProfileRequest
        {
            FirstName = "Hacker"
        });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "unauthenticated PATCH /auth/me/ must be denied with 401");
    }

    [Fact]
    [Trait("Category", "Boundary")]
    [Trait("Priority", "P2")]
    public async Task Profile_UpdateWithVeryLongName_NeverCauses500()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);

        // Act: 1000-character first_name — may be accepted or rejected, but never 500
        var response = await _apiClient.PatchAsync("auth/me/", new UpdateProfileRequest
        {
            FirstName = new string('A', 1000)
        });
        var body = await response.Content.ReadAsStringAsync();

        // Assert
        ((int)response.StatusCode).Should().NotBe(500,
            $"oversized first_name must not cause a server error. " +
            $"Body: {body[..Math.Min(200, body.Length)]}");
        response.StatusCode.Should().BeOneOf(
            new[] { HttpStatusCode.OK, HttpStatusCode.BadRequest },
            $"oversized first_name must return 200 (accepted) or 400 (rejected), not {response.StatusCode}");
    }

    [Fact]
    [Trait("Category", "Security")]
    [Trait("Priority", "P1")]
    public async Task Profile_ResponseDoesNotExposePasswordField()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);

        // Act
        var response = await _apiClient.GetAsync("auth/me/");
        var body = await response.Content.ReadAsStringAsync();

        // Assert: profile response must never include a password field
        response.StatusCode.Should().Be(HttpStatusCode.OK, "GET /auth/me/ must succeed");
        body.Should().NotContain("\"password\"",
            "the profile response must not expose any password field");
    }

    public void Dispose() => _apiClient?.Dispose();
}
