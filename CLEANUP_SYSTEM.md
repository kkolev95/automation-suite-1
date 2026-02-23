# Test Account Cleanup System

## Overview

The test suite now includes a comprehensive cleanup system to avoid polluting the website with test data. Instead of deleting individual tests and companies, we delete the test accounts themselves, which triggers CASCADE DELETE in the database to remove all associated data.

## How It Works

### TestAccountManager

The `TestAccountManager` class (`Helpers/TestAccountManager.cs`) provides:

1. **Automatic Tracking**: All accounts created via `CreateAndTrackAccountAsync()` are tracked in memory
2. **CASCADE DELETE**: Deleting an account via `DELETE /auth/me/` removes:
   - The account itself
   - All tests created by that account
   - All questions and answers in those tests
   - All companies created by that account
   - All test attempts made by that account
3. **Process Exit Cleanup**: Optionally registers cleanup to run when tests finish
4. **Manual Cleanup**: Provides methods to clean up on-demand

### Key Methods

```csharp
// Create and track a test account
var (email, password, token) = await TestAccountManager.CreateAndTrackAccountAsync(
    client,
    emailPrefix: "testuser",
    autoCleanup: true
);

// Delete a specific account (cascade deletes all data)
bool deleted = await TestAccountManager.DeleteAccountAsync(client, email, password);

// Delete all tracked accounts
CleanupSummary summary = await TestAccountManager.CleanupAllAccountsAsync();

// Delete only old accounts
CleanupSummary summary = await TestAccountManager.CleanupOldAccountsAsync(TimeSpan.FromHours(1));

// Get tracked account info
int count = TestAccountManager.GetTrackedAccountsCount();
List<string> emails = TestAccountManager.GetTrackedAccountEmails();
```

## Integration with Test Suite

### TestDataHelper

The `TestDataHelper.RegisterAndLoginAsync()` method now uses `TestAccountManager` automatically:

```csharp
// Old way (manual registration - no tracking)
var token = await TestDataHelper.RegisterAndLoginAsync(client);

// Now automatically tracks for cleanup
var token = await TestDataHelper.RegisterAndLoginAsync(client, autoCleanup: true);

// Disable tracking for specific tests if needed
var token = await TestDataHelper.RegisterAndLoginAsync(client, autoCleanup: false);
```

### Stress Tests

The stress tests have been updated to track created accounts:

- `StressTest_ConcurrentUserRegistrations_HandlesLoad`: Now tracks all successfully registered users
- Other stress tests use `PersistentTestUser` (fixed account) for easier manual cleanup

## Manual Cleanup

### Running Manual Cleanup

The `CleanupTests` class includes several manual cleanup tests:

```bash
# Clean up all tracked accounts from current session
dotnet test --filter "FullyQualifiedName~Manual_CleanupAllTrackedAccounts"

# Clean up old accounts (older than 1 hour)
dotnet test --filter "FullyQualifiedName~Manual_CleanupOldAccounts"
```

Note: These tests are marked with `[Fact(Skip = "...")]` so they won't run automatically. Remove the Skip to run them.

### Automated Cleanup Tests

Run these to verify the cleanup system works:

```bash
# Test basic account cleanup
dotnet test --filter "FullyQualifiedName~AccountCleanup_AllTrackedAccounts_DeletesSuccessfully"

# Test CASCADE DELETE behavior
dotnet test --filter "FullyQualifiedName~AccountCleanup_WithCascadeData_DeletesEverything"
```

## Benefits

### Efficiency

- **Faster**: One DELETE request per account vs many DELETE requests for individual items
- **Complete**: CASCADE DELETE ensures ALL data is removed (tests, questions, companies, attempts)
- **Reliable**: Database handles cascade logic, no need to track individual items

### Safety

- **No Data Leaks**: All test data is guaranteed to be deleted when account is deleted
- **Thread-Safe**: Uses `ConcurrentBag` for tracking accounts
- **Automatic**: Can register cleanup on process exit

### Debugging

- **Tracking**: Can see how many accounts are tracked and their emails
- **Summary Reports**: Get detailed information about cleanup operations
- **Error Handling**: Cleanup continues even if individual deletions fail

## Example: Test with Cleanup

```csharp
[Fact]
public async Task MyTest_CreatesDataAndCleansUp()
{
    // Arrange: Create account (automatically tracked)
    var token = await TestDataHelper.RegisterAndLoginAsync(_apiClient);

    // Create test data
    var test = await TestDataHelper.CreateTestAsync(_apiClient, "My Test");
    var company = await _apiClient.PostAsync<CreateCompanyRequest, CompanyResponse>(
        "companies/",
        new CreateCompanyRequest { Name = "My Company" }
    );

    // Act: Do your test...

    // Assert: Verify behavior...

    // Cleanup happens automatically when test process exits
    // Or manually trigger cleanup:
    // await TestAccountManager.CleanupAllAccountsAsync();
}
```

## Comparison: Old vs New

### Old Pattern-Based Cleanup (TestDataCleanup.cs)

❌ Requires listing all tests and companies
❌ Individual DELETE requests for each item
❌ Doesn't delete attempts or other data
❌ Pattern matching can miss items
❌ Slower for large datasets

### New Account-Based Cleanup (TestAccountManager.cs)

✅ One DELETE per account
✅ CASCADE DELETE removes ALL data
✅ Thread-safe tracking
✅ Automatic cleanup on process exit
✅ Detailed summary reports
✅ Time-based cleanup options

## Configuration

### Enable Automatic Cleanup on Exit

```csharp
// In your test class constructor or setup
TestAccountManager.RegisterCleanup();

// Now cleanup runs automatically when process exits
```

### Disable Tracking for Specific Accounts

```csharp
// Don't track this account
var (email, password, token) = await TestAccountManager.CreateAndTrackAccountAsync(
    client,
    autoCleanup: false
);
```

### Clear Tracking Without Deleting

```csharp
// Clear tracking list without deleting accounts
TestAccountManager.ClearTracking();
```

## CleanupSummary

All cleanup methods return a `CleanupSummary` object:

```csharp
public class CleanupSummary
{
    public int TotalAccounts { get; set; }
    public int AccountsDeleted { get; set; }
    public List<string> Errors { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public TimeSpan Duration { get; set; }
    public bool Success => Errors.Count == 0 && AccountsDeleted == TotalAccounts;
}
```

## Future Improvements

- [ ] Add cleanup to xUnit test fixtures for automatic per-test-class cleanup
- [ ] Add CI/CD integration to clean up after test runs
- [ ] Add metrics/logging for cleanup operations
- [ ] Consider time-based auto-cleanup background task
- [ ] Add cleanup verification (confirm data is actually gone)
