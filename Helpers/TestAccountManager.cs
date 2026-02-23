using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using TestIT.ApiTests.Models;

namespace TestIT.ApiTests.Helpers;

/// <summary>
/// Manages test accounts and provides cleanup to avoid polluting the website.
/// Tracks all created accounts and can delete them (cascade deletes all their data).
///
/// Persistence: account credentials are written to pending_account_cleanup.json
/// BEFORE the register call is made. This means that if the server creates an account
/// but returns a 5xx (so a retry fires and we never reach normal tracking), the account
/// is still captured in the log and cleaned up the next time CleanupAllAccountsAsync runs.
/// Log entries are removed only after a confirmed successful deletion.
/// </summary>
public static class TestAccountManager
{
    private static readonly ConcurrentBag<TestAccount> _createdAccounts = new();
    private static readonly object _lockObject = new();
    private static bool _isCleanupRegistered = false;

    private static readonly string _logFilePath =
        Path.Combine(Directory.GetCurrentDirectory(), "pending_account_cleanup.json");

    private static readonly JsonSerializerOptions _jsonOpts =
        new() { WriteIndented = true };

    // ─────────────────────────────────────────────────────────────────────
    // Log file helpers
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Appends an entry to the persistent log file.
    /// Called before making the register API call so orphaned accounts are captured.
    /// </summary>
    private static void WriteToLog(string email, string password)
    {
        lock (_lockObject)
        {
            try
            {
                var entries = ReadLogEntries();
                if (!entries.Any(e => e.Email == email))
                {
                    entries.Add(new LogEntry { Email = email, Password = password, CreatedAt = DateTime.UtcNow });
                    File.WriteAllText(_logFilePath, JsonSerializer.Serialize(entries, _jsonOpts));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Cleanup] Warning: could not write to log: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Removes an entry from the persistent log file after confirmed deletion.
    /// </summary>
    private static void RemoveFromLog(string email)
    {
        lock (_lockObject)
        {
            try
            {
                var entries = ReadLogEntries();
                var updated = entries.Where(e => e.Email != email).ToList();
                File.WriteAllText(_logFilePath, JsonSerializer.Serialize(updated, _jsonOpts));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Cleanup] Warning: could not update log: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Reads all entries from the persistent log file.
    /// </summary>
    private static List<LogEntry> ReadLogEntries()
    {
        try
        {
            if (!File.Exists(_logFilePath)) return new List<LogEntry>();
            var json = File.ReadAllText(_logFilePath);
            return JsonSerializer.Deserialize<List<LogEntry>>(json) ?? new List<LogEntry>();
        }
        catch
        {
            return new List<LogEntry>();
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Public API
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Registers cleanup to run when the test process exits.
    /// </summary>
    public static void RegisterCleanup()
    {
        lock (_lockObject)
        {
            if (_isCleanupRegistered) return;

            AppDomain.CurrentDomain.ProcessExit += async (s, e) =>
            {
                await CleanupAllAccountsAsync();
            };

            _isCleanupRegistered = true;
        }
    }

    /// <summary>
    /// Creates and registers a new test account for tracking.
    /// Writes credentials to the persistent log BEFORE the register call so that
    /// accounts orphaned by a retried POST are still cleaned up.
    /// </summary>
    public static async Task<(string email, string password, string token)> CreateAndTrackAccountAsync(
        ApiClient client,
        string? emailPrefix = null,
        bool autoCleanup = true)
    {
        var email = $"{emailPrefix ?? "testuser"}_{Guid.NewGuid().ToString("N")[..8]}@example.com";
        var password = "Test123!";

        // Write to log BEFORE the POST so orphaned accounts are always captured
        if (autoCleanup)
            WriteToLog(email, password);

        // Register
        var registerResponse = await client.PostAsync("auth/register/", new RegisterRequest
        {
            Email = email,
            Password = password,
            PasswordConfirm = password,
            FirstName = "Test",
            LastName = "User"
        });

        if (registerResponse.StatusCode != HttpStatusCode.Created)
        {
            // Remove from log — this email was never successfully registered
            if (autoCleanup)
                RemoveFromLog(email);
            throw new Exception($"Failed to create test account: {registerResponse.StatusCode}");
        }

        // Login
        var loginResponse = await client.PostAsync<LoginRequest, LoginResponse>(
            "auth/login/",
            new LoginRequest { Email = email, Password = password });

        var token = loginResponse!.AccessToken;
        client.SetAuthToken(token);

        // Track in memory for this run
        if (autoCleanup)
        {
            _createdAccounts.Add(new TestAccount
            {
                Email = email,
                Password = password,
                Token = token,
                CreatedAt = DateTime.UtcNow
            });
            RegisterCleanup();
        }

        return (email, password, token);
    }

    /// <summary>
    /// Tracks an existing account for cleanup.
    /// </summary>
    public static void TrackAccount(string email, string password, string token)
    {
        WriteToLog(email, password);

        _createdAccounts.Add(new TestAccount
        {
            Email = email,
            Password = password,
            Token = token,
            CreatedAt = DateTime.UtcNow
        });
        RegisterCleanup();
    }

    /// <summary>
    /// Deletes a specific account and all its data (cascade delete).
    /// Removes the account from the persistent log on success.
    /// </summary>
    public static async Task<bool> DeleteAccountAsync(ApiClient client, string email, string password)
    {
        try
        {
            var loginResponse = await client.PostAsync<LoginRequest, LoginResponse>(
                "auth/login/",
                new LoginRequest { Email = email, Password = password });

            if (loginResponse?.AccessToken == null)
            {
                Console.WriteLine($"[Cleanup] Failed to login as {email}");
                return false;
            }

            client.SetAuthToken(loginResponse.AccessToken);

            var deleteResponse = await client.DeleteAsync("auth/me/");

            if (deleteResponse.StatusCode == HttpStatusCode.NoContent ||
                deleteResponse.StatusCode == HttpStatusCode.OK)
            {
                Console.WriteLine($"[Cleanup] ✓ Deleted account: {email}");
                RemoveFromLog(email);
                return true;
            }

            Console.WriteLine($"[Cleanup] ✗ Failed to delete {email}: {deleteResponse.StatusCode}");
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Cleanup] ✗ Error deleting {email}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Deletes all tracked test accounts plus any orphaned accounts found in the
    /// persistent log file (e.g. from previous runs or retried register requests).
    /// </summary>
    public static async Task<CleanupSummary> CleanupAllAccountsAsync()
    {
        // Merge in-memory accounts with anything remaining in the log file
        var inMemoryEmails = _createdAccounts.Select(a => a.Email).ToHashSet();
        var logAccounts = ReadLogEntries()
            .Where(e => !inMemoryEmails.Contains(e.Email))
            .Select(e => new TestAccount { Email = e.Email, Password = e.Password, CreatedAt = e.CreatedAt })
            .ToList();

        var accountsToDelete = _createdAccounts.ToList();
        accountsToDelete.AddRange(logAccounts);

        var summary = new CleanupSummary
        {
            TotalAccounts = accountsToDelete.Count,
            StartTime = DateTime.UtcNow
        };

        if (accountsToDelete.Count == 0)
        {
            Console.WriteLine("[Cleanup] No test accounts to clean up");
            return summary;
        }

        var logOnly = logAccounts.Count > 0 ? $" ({logAccounts.Count} from log)" : "";
        Console.WriteLine($"[Cleanup] Starting cleanup of {accountsToDelete.Count} test accounts{logOnly}...");

        var client = new ApiClient(TestConfiguration.GetBaseUrl());

        foreach (var account in accountsToDelete)
        {
            var deleted = await DeleteAccountAsync(client, account.Email, account.Password);
            if (deleted)
                summary.AccountsDeleted++;
            else
                summary.Errors.Add($"Failed to delete: {account.Email}");

            await Task.Delay(100);
        }

        client.Dispose();

        summary.EndTime = DateTime.UtcNow;
        summary.Duration = summary.EndTime - summary.StartTime;

        Console.WriteLine($"[Cleanup] Completed: {summary.AccountsDeleted}/{summary.TotalAccounts} accounts deleted");
        if (summary.Errors.Any())
            Console.WriteLine($"[Cleanup] Errors: {summary.Errors.Count}");

        return summary;
    }

    /// <summary>
    /// Deletes accounts older than specified time.
    /// </summary>
    public static async Task<CleanupSummary> CleanupOldAccountsAsync(TimeSpan olderThan)
    {
        var cutoffTime = DateTime.UtcNow - olderThan;
        var oldAccounts = _createdAccounts.Where(a => a.CreatedAt < cutoffTime).ToList();

        var summary = new CleanupSummary
        {
            TotalAccounts = oldAccounts.Count,
            StartTime = DateTime.UtcNow
        };

        if (oldAccounts.Count == 0)
        {
            Console.WriteLine($"[Cleanup] No accounts older than {olderThan.TotalMinutes} minutes");
            return summary;
        }

        Console.WriteLine($"[Cleanup] Deleting {oldAccounts.Count} accounts older than {olderThan.TotalMinutes} minutes...");

        var client = new ApiClient(TestConfiguration.GetBaseUrl());

        foreach (var account in oldAccounts)
        {
            var deleted = await DeleteAccountAsync(client, account.Email, account.Password);
            if (deleted)
                summary.AccountsDeleted++;
            else
                summary.Errors.Add($"Failed to delete: {account.Email}");

            await Task.Delay(100);
        }

        client.Dispose();

        summary.EndTime = DateTime.UtcNow;
        summary.Duration = summary.EndTime - summary.StartTime;

        Console.WriteLine($"[Cleanup] Completed: {summary.AccountsDeleted}/{summary.TotalAccounts} old accounts deleted");

        return summary;
    }

    /// <summary>Gets current tracked accounts count (in-memory only).</summary>
    public static int GetTrackedAccountsCount() => _createdAccounts.Count;

    /// <summary>Gets list of tracked account emails (for diagnostics).</summary>
    public static List<string> GetTrackedAccountEmails() =>
        _createdAccounts.Select(a => a.Email).ToList();
}

// ─────────────────────────────────────────────────────────────────────────────
// Supporting types
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Represents a test account for tracking and cleanup.</summary>
public class TestAccount
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

/// <summary>Persistent log entry (Email + Password only — no token needed for cleanup).</summary>
internal class LogEntry
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

/// <summary>Summary of cleanup operation.</summary>
public class CleanupSummary
{
    public int TotalAccounts { get; set; }
    public int AccountsDeleted { get; set; }
    public List<string> Errors { get; set; } = new();
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public TimeSpan Duration { get; set; }

    public bool Success => Errors.Count == 0 && AccountsDeleted == TotalAccounts;
}
