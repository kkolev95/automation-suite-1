using TestIT.ApiTests.Models;

namespace TestIT.ApiTests.Helpers;

/// <summary>
/// Manages a persistent test user account for stress tests and cleanup.
/// Using one account makes cleanup possible.
/// </summary>
public static class PersistentTestUser
{
    // Fixed credentials for stress testing - same user for all test runs
    public const string Email = "stress_test_user@example.com";
    public const string Password = "StressTest123!";
    public const string FirstName = "Stress";
    public const string LastName = "Tester";

    /// <summary>
    /// Ensures the persistent test user exists and returns logged-in token.
    /// Registers if doesn't exist, or just logs in if already exists.
    /// </summary>
    public static async Task<string> GetOrCreateAndLoginAsync(ApiClient client)
    {
        // Try to login first
        try
        {
            var loginResponse = await client.PostAsync<LoginRequest, LoginResponse>(
                "auth/login/",
                new LoginRequest
                {
                    Email = Email,
                    Password = Password
                });

            if (loginResponse?.AccessToken != null)
            {
                client.SetAuthToken(loginResponse.AccessToken);
                return loginResponse.AccessToken;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PersistentTestUser] Initial login attempt failed: {ex.Message}");
        }

        // Register the user
        try
        {
            await client.PostAsync("auth/register/", new RegisterRequest
            {
                Email = Email,
                Password = Password,
                PasswordConfirm = Password,
                FirstName = FirstName,
                LastName = LastName
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PersistentTestUser] Registration attempt failed: {ex.Message} (account may already exist)");
        }

        // Try login again after registration
        var finalLoginResponse = await client.PostAsync<LoginRequest, LoginResponse>(
            "auth/login/",
            new LoginRequest
            {
                Email = Email,
                Password = Password
            });

        if (finalLoginResponse?.AccessToken == null)
        {
            throw new InvalidOperationException(
                $"[PersistentTestUser] Setup failed: could not login as '{Email}' after registration attempt. " +
                "Check that the server is reachable and the account credentials are valid.");
        }

        client.SetAuthToken(finalLoginResponse.AccessToken);
        return finalLoginResponse.AccessToken;
    }

    /// <summary>
    /// Login with the persistent test user
    /// </summary>
    public static async Task<bool> LoginAsync(ApiClient client)
    {
        try
        {
            var loginResponse = await client.PostAsync<LoginRequest, LoginResponse>(
                "auth/login/",
                new LoginRequest
                {
                    Email = Email,
                    Password = Password
                });

            if (loginResponse?.AccessToken != null)
            {
                client.SetAuthToken(loginResponse.AccessToken);
                return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }
}
