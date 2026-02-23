using System.Net;
using FluentAssertions;
using TestIT.ApiTests.Helpers;
using TestIT.ApiTests.Models;
using Xunit;

namespace TestIT.ApiTests.Tests;

public class AuthenticationTests : IDisposable
{
    private readonly ApiClient _apiClient;
    private readonly string _baseUrl;

    public AuthenticationTests(ITestOutputHelper output)
    {
        ApiClient.SetOutput(output.WriteLine);
        _baseUrl = TestConfiguration.GetBaseUrl();
        _apiClient = new ApiClient(_baseUrl);
    }

    [Fact]
    [Trait("Category", "Smoke")]
    [Trait("Priority", "P0")]
    public async Task Registration_WithValidCredentials_CreatesUserAccount()
    {
        // Arrange
        var uniqueEmail = $"testuser_{Guid.NewGuid().ToString("N").Substring(0, 8)}@example.com";
        var registerRequest = new RegisterRequest
        {
            Email = uniqueEmail,
            Password = "SecurePass123!",
            PasswordConfirm = "SecurePass123!",
            FirstName = "John",
            LastName = "Doe"
        };

        // Act
        var response = await _apiClient.PostAsync("auth/register/", registerRequest);
        var body = await _apiClient.GetResponseBodyAsync(response);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created, $"because registration should succeed. Response: {body}");

        var registerResponse = await _apiClient.DeserializeResponseAsync<RegisterResponse>(response);
        registerResponse.Should().NotBeNull();
        registerResponse!.Email.Should().Be(uniqueEmail);
        registerResponse.FirstName.Should().Be("John");
        registerResponse.LastName.Should().Be("Doe");
    }

    [Fact]
    [Trait("Category", "Validation")]
    [Trait("Priority", "P1")]
    public async Task Registration_WithMismatchedPasswords_RejectsRequest()
    {
        // Arrange
        var uniqueEmail = $"testuser_{Guid.NewGuid().ToString("N").Substring(0, 8)}@example.com";
        var registerRequest = new RegisterRequest
        {
            Email = uniqueEmail,
            Password = "SecurePass123!",
            PasswordConfirm = "DifferentPass123!",
            FirstName = "John",
            LastName = "Doe"
        };

        // Act
        var response = await _apiClient.PostAsync("auth/register/", registerRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, "because passwords don't match");
    }

    [Fact]
    [Trait("Category", "Validation")]
    [Trait("Priority", "P1")]
    public async Task Registration_WithWeakPassword_RejectsRequest()
    {
        // Arrange
        var uniqueEmail = $"testuser_{Guid.NewGuid().ToString("N").Substring(0, 8)}@example.com";
        var registerRequest = new RegisterRequest
        {
            Email = uniqueEmail,
            Password = "weak",
            PasswordConfirm = "weak",
            FirstName = "John",
            LastName = "Doe"
        };

        // Act
        var response = await _apiClient.PostAsync("auth/register/", registerRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, "because password is too weak");
    }

    [Fact]
    [Trait("Category", "Validation")]
    [Trait("Priority", "P1")]
    public async Task Registration_WithMissingRequiredFields_RejectsRequest()
    {
        // Arrange
        var registerRequest = new RegisterRequest
        {
            Email = "incomplete@example.com",
            Password = "SecurePass123!",
            PasswordConfirm = "SecurePass123!"
            // Missing FirstName and LastName
        };

        // Act
        var response = await _apiClient.PostAsync("auth/register/", registerRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, "because required fields are missing");
    }

    [Fact]
    [Trait("Category", "Smoke")]
    [Trait("Priority", "P0")]
    public async Task Login_WithValidCredentials_ReturnsAuthenticationTokens()
    {
        // Arrange
        // First register a user
        var uniqueEmail = $"testuser_{Guid.NewGuid().ToString("N").Substring(0, 8)}@example.com";
        var password = "SecurePass123!";
        var registerRequest = new RegisterRequest
        {
            Email = uniqueEmail,
            Password = password,
            PasswordConfirm = password,
            FirstName = "Test",
            LastName = "User"
        };
        await _apiClient.PostAsync("auth/register/", registerRequest);

        var loginRequest = new LoginRequest
        {
            Email = uniqueEmail,
            Password = password
        };

        // Act
        var response = await _apiClient.PostAsync("auth/login/", loginRequest);
        var body = await _apiClient.GetResponseBodyAsync(response);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK, $"because login should succeed with valid credentials. Response: {body}");

        var loginResponse = await _apiClient.DeserializeResponseAsync<LoginResponse>(response);
        loginResponse.Should().NotBeNull();
        loginResponse!.AccessToken.Should().NotBeNullOrEmpty("because access token should be provided");
        loginResponse.RefreshToken.Should().NotBeNullOrEmpty("because refresh token should be provided");
    }

    [Fact]
    [Trait("Category", "Security")]
    [Trait("Priority", "P0")]
    public async Task Login_WithInvalidCredentials_DeniesAccess()
    {
        // Arrange
        var loginRequest = new LoginRequest
        {
            Email = "nonexistent@example.com",
            Password = "WrongPassword123!"
        };

        // Act
        var response = await _apiClient.PostAsync("auth/login/", loginRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "because credentials are invalid");
    }

    [Fact]
    [Trait("Category", "Validation")]
    [Trait("Priority", "P1")]
    public async Task Login_WithMissingPassword_RejectsRequest()
    {
        // Arrange
        var loginRequest = new LoginRequest
        {
            Email = "test@example.com"
            // Missing Password
        };

        // Act
        var response = await _apiClient.PostAsync("auth/login/", loginRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, "because password field is missing");
    }

    [Fact]
    [Trait("Category", "Smoke")]
    [Trait("Priority", "P0")]
    public async Task UserProfile_WhenAuthenticated_ReturnsUserDetails()
    {
        // Arrange
        // First register and login a user
        var uniqueEmail = $"testuser_{Guid.NewGuid().ToString("N").Substring(0, 8)}@example.com";
        var password = "SecurePass123!";
        var registerRequest = new RegisterRequest
        {
            Email = uniqueEmail,
            Password = password,
            PasswordConfirm = password,
            FirstName = "Test",
            LastName = "User"
        };
        await _apiClient.PostAsync("auth/register/", registerRequest);

        var loginRequest = new LoginRequest
        {
            Email = uniqueEmail,
            Password = password
        };
        var loginResponse = await _apiClient.PostAsync<LoginRequest, LoginResponse>("auth/login/", loginRequest);

        _apiClient.SetAuthToken(loginResponse!.AccessToken);

        // Act
        var response = await _apiClient.GetAsync("auth/me/");
        var body = await _apiClient.GetResponseBodyAsync(response);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK, $"because valid token is provided. Response: {body}");

        var userResponse = await _apiClient.DeserializeResponseAsync<UserResponse>(response);
        userResponse.Should().NotBeNull();
        userResponse!.Email.Should().Be(uniqueEmail);
        userResponse.FirstName.Should().Be("Test");
        userResponse.LastName.Should().Be("User");
    }

    [Fact]
    [Trait("Category", "Security")]
    [Trait("Priority", "P0")]
    public async Task UserProfile_WhenUnauthenticated_DeniesAccess()
    {
        // Arrange
        _apiClient.ClearAuthToken();

        // Act
        var response = await _apiClient.GetAsync("auth/me/");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "because no authentication token is provided");
    }

    [Fact]
    [Trait("Category", "Authentication")]
    [Trait("Priority", "P1")]
    public async Task TokenRefresh_WithValidRefreshToken_IssuesNewAccessToken()
    {
        // Arrange
        // First register and login a user
        var uniqueEmail = $"testuser_{Guid.NewGuid().ToString("N").Substring(0, 8)}@example.com";
        var password = "SecurePass123!";
        var registerRequest = new RegisterRequest
        {
            Email = uniqueEmail,
            Password = password,
            PasswordConfirm = password,
            FirstName = "Test",
            LastName = "User"
        };
        await _apiClient.PostAsync("auth/register/", registerRequest);

        var loginRequest = new LoginRequest
        {
            Email = uniqueEmail,
            Password = password
        };
        var loginResponse = await _apiClient.PostAsync<LoginRequest, LoginResponse>("auth/login/", loginRequest);

        var refreshRequest = new RefreshTokenRequest
        {
            RefreshToken = loginResponse!.RefreshToken
        };

        // Act
        var response = await _apiClient.PostAsync("auth/refresh/", refreshRequest);
        var body = await _apiClient.GetResponseBodyAsync(response);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK, $"because valid refresh token is provided. Response: {body}");

        var refreshResponse = await _apiClient.DeserializeResponseAsync<RefreshTokenResponse>(response);
        refreshResponse.Should().NotBeNull();
        refreshResponse!.AccessToken.Should().NotBeNullOrEmpty("because new access token should be provided");
    }

    public void Dispose()
    {
        _apiClient?.Dispose();
    }
}
