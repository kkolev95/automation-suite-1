using System.Net;
using FluentAssertions;
using TestIT.ApiTests.Helpers;
using TestIT.ApiTests.Models;
using Xunit;
using Xunit.Abstractions;

namespace TestIT.ApiTests.Tests;

/// <summary>
/// Validates the token refresh lifecycle beyond the basic happy-path already in AuthenticationTests:
///   - Refreshed access token is a newly issued JWT (different from the original)
///   - Refreshed access token grants full read and write API access
///   - Blank refresh token is rejected gracefully (400/401, never 500)
///   - Refreshed token can be used for write operations (creates a test)
///   - Refreshing with a structurally valid but unknown token returns 401, not 500
/// </summary>
public class TokenRefreshTests : IDisposable
{
    private readonly ApiClient _apiClient;

    public TokenRefreshTests(ITestOutputHelper output)
    {
        ApiClient.SetOutput(output.WriteLine);
        _apiClient = new ApiClient(TestConfiguration.GetBaseUrl());
    }

    /// <summary>
    /// Registers a fresh user, logs in, and returns the raw token pair.
    /// Tracks the account for cleanup.
    /// </summary>
    private async Task<(string Email, string Password, string AccessToken, string RefreshToken)>
        RegisterAndGetTokensAsync()
    {
        var email = $"refresh_{Guid.NewGuid().ToString("N")[..8]}@example.com";
        const string password = "Test123!";

        await _apiClient.PostAsync("auth/register/", new RegisterRequest
        {
            Email = email, Password = password, PasswordConfirm = password,
            FirstName = "Refresh", LastName = "User"
        });

        var loginResp = await _apiClient.PostAsync<LoginRequest, LoginResponse>(
            "auth/login/", new LoginRequest { Email = email, Password = password });

        TestAccountManager.TrackAccount(email, password, loginResp!.AccessToken);
        return (email, password, loginResp.AccessToken, loginResp.RefreshToken);
    }

    [Fact]
    [Trait("Category", "Authentication")]
    [Trait("Priority", "P1")]
    public async Task TokenRefresh_NewAccessTokenIsDifferentFromOriginal()
    {
        // Arrange
        var (_, _, originalAccess, refreshToken) = await RegisterAndGetTokensAsync();

        // Act
        var resp = await _apiClient.PostAsync("auth/refresh/",
            new RefreshTokenRequest { RefreshToken = refreshToken });
        resp.StatusCode.Should().Be(HttpStatusCode.OK, "refresh must succeed");

        var refreshed = await _apiClient.DeserializeResponseAsync<RefreshTokenResponse>(resp);

        // Assert: the new access token must be a newly issued JWT, not a re-issue of the old one
        refreshed!.AccessToken.Should().NotBeNullOrEmpty(
            "refreshed access token must be non-empty");
        refreshed.AccessToken.Should().NotBe(originalAccess,
            "a refreshed access token must be a newly issued JWT, not the same token as before");
    }

    [Fact]
    [Trait("Category", "Authentication")]
    [Trait("Priority", "P1")]
    public async Task TokenRefresh_RefreshedAccessToken_GrantsReadAccess()
    {
        // Arrange
        var (_, _, _, refreshToken) = await RegisterAndGetTokensAsync();

        var resp = await _apiClient.PostAsync("auth/refresh/",
            new RefreshTokenRequest { RefreshToken = refreshToken });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var refreshed = await _apiClient.DeserializeResponseAsync<RefreshTokenResponse>(resp);

        // Act: swap in the refreshed token and hit protected endpoints
        _apiClient.SetAuthToken(refreshed!.AccessToken);

        var meResp = await _apiClient.GetAsync("auth/me/");
        var testsResp = await _apiClient.GetAsync("tests/");

        // Assert
        meResp.StatusCode.Should().Be(HttpStatusCode.OK,
            "the refreshed access token must allow GET /auth/me/");
        testsResp.StatusCode.Should().Be(HttpStatusCode.OK,
            "the refreshed access token must allow GET /tests/");
    }

    [Fact]
    [Trait("Category", "Authentication")]
    [Trait("Priority", "P1")]
    public async Task TokenRefresh_RefreshedAccessToken_GrantsWriteAccess()
    {
        // Arrange
        var (_, _, _, refreshToken) = await RegisterAndGetTokensAsync();

        var resp = await _apiClient.PostAsync("auth/refresh/",
            new RefreshTokenRequest { RefreshToken = refreshToken });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var refreshed = await _apiClient.DeserializeResponseAsync<RefreshTokenResponse>(resp);

        _apiClient.SetAuthToken(refreshed!.AccessToken);

        // Act: create a test using the refreshed token
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"RefreshWrite_{Guid.NewGuid().ToString("N")[..8]}");

        // Assert
        test.Should().NotBeNull(
            "a test creation using a refreshed access token must succeed");
        test!.Slug.Should().NotBeNullOrEmpty(
            "the slug must be returned when creating a test with a refreshed token");
    }

    [Fact]
    [Trait("Category", "Authentication")]
    [Trait("Priority", "P1")]
    public async Task TokenRefresh_WithBlankRefreshToken_IsRejectedGracefully()
    {
        // Arrange: no account needed — testing with a blank token string

        // Act
        var response = await _apiClient.PostAsync("auth/refresh/",
            new RefreshTokenRequest { RefreshToken = string.Empty });
        var body = await response.Content.ReadAsStringAsync();

        // Assert: blank token must be rejected — 400 or 401, never 500
        ((int)response.StatusCode).Should().BeOneOf(new[] { 400, 401 },
            $"blank refresh token must be rejected with 400/401. " +
            $"Status: {(int)response.StatusCode}, Body: {body[..Math.Min(200, body.Length)]}");
    }

    [Fact]
    [Trait("Category", "Authentication")]
    [Trait("Priority", "P2")]
    public async Task TokenRefresh_WithUnknownValidFormatToken_ReturnsClientError()
    {
        // Arrange: a structurally plausible but completely unknown JWT-like token
        const string unknownToken =
            "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9" +
            ".eyJzdWIiOiI5OTk5OTkiLCJleHAiOjE3MDAwMDAwMDB9" +
            ".AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

        // Act
        var response = await _apiClient.PostAsync("auth/refresh/",
            new RefreshTokenRequest { RefreshToken = unknownToken });
        var body = await response.Content.ReadAsStringAsync();

        // Assert: unknown token must return a client error (4xx), never 500
        ((int)response.StatusCode).Should().NotBe(500,
            $"an unknown refresh token must not cause a server error. " +
            $"Body: {body[..Math.Min(200, body.Length)]}");
        ((int)response.StatusCode).Should().BeInRange(400, 499,
            $"an unknown refresh token must return a 4xx error, not {(int)response.StatusCode}");
    }

    public void Dispose() => _apiClient?.Dispose();
}
