using System.Text.Json.Serialization;

namespace TestIT.ApiTests.Models;

public class RegisterRequest
{
    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("password")]
    public string Password { get; set; } = string.Empty;

    [JsonPropertyName("password_confirm")]
    public string PasswordConfirm { get; set; } = string.Empty;

    [JsonPropertyName("first_name")]
    public string FirstName { get; set; } = string.Empty;

    [JsonPropertyName("last_name")]
    public string LastName { get; set; } = string.Empty;
}

public class RegisterResponse
{
    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("first_name")]
    public string FirstName { get; set; } = string.Empty;

    [JsonPropertyName("last_name")]
    public string LastName { get; set; } = string.Empty;
}

public class LoginRequest
{
    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("password")]
    public string Password { get; set; } = string.Empty;
}

public class LoginResponse
{
    [JsonPropertyName("access")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("refresh")]
    public string RefreshToken { get; set; } = string.Empty;
}

public class RefreshTokenRequest
{
    [JsonPropertyName("refresh")]
    public string RefreshToken { get; set; } = string.Empty;
}

public class RefreshTokenResponse
{
    [JsonPropertyName("access")]
    public string AccessToken { get; set; } = string.Empty;
}

public class UserResponse
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("first_name")]
    public string FirstName { get; set; } = string.Empty;

    [JsonPropertyName("last_name")]
    public string LastName { get; set; } = string.Empty;
}

public class UpdateProfileRequest
{
    [JsonPropertyName("first_name")]
    public string? FirstName { get; set; }

    [JsonPropertyName("last_name")]
    public string? LastName { get; set; }
}

public class ErrorResponse
{
    [JsonPropertyName("detail")]
    public string? Detail { get; set; }

    [JsonPropertyName("email")]
    public List<string>? Email { get; set; }

    [JsonPropertyName("password")]
    public List<string>? Password { get; set; }

    [JsonPropertyName("password_confirm")]
    public List<string>? PasswordConfirm { get; set; }

    [JsonPropertyName("first_name")]
    public List<string>? FirstName { get; set; }

    [JsonPropertyName("last_name")]
    public List<string>? LastName { get; set; }
}
