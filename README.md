# TestIT API Backend Tests

This is a C# test suite for testing the TestIT platform backend API at https://exampractice.com.

## Project Structure

```
TestIT.ApiTests/
├── Models/              # Request/Response DTOs
│   ├── AuthModels.cs   # Authentication models
│   └── TestModels.cs   # Test management models
├── Helpers/            # Utility classes
│   ├── ApiClient.cs    # HTTP client wrapper
│   └── TestConfiguration.cs  # Configuration helper
├── Tests/              # Test classes
│   ├── AuthenticationTests.cs      # Authentication endpoint tests
│   └── TestsManagementTests.cs     # Tests CRUD operations tests
├── appsettings.json    # Configuration file
└── TestIT.ApiTests.csproj  # Project file
```

## Prerequisites

- .NET 8.0 SDK or later
- Internet connection to access https://exampractice.com

## Configuration

The API base URL and test user credentials are configured in `appsettings.json`:

```json
{
  "ApiSettings": {
    "BaseUrl": "https://exampractice.com/api"
  }
}
```

You can modify the base URL if the API is hosted elsewhere.

## Running Tests

### Run all tests
```bash
dotnet test
```

### Run tests with detailed output
```bash
dotnet test --logger "console;verbosity=detailed"
```

### Run specific test class
```bash
dotnet test --filter "FullyQualifiedName~AuthenticationTests"
dotnet test --filter "FullyQualifiedName~TestsManagementTests"
```

### Run a specific test method
```bash
dotnet test --filter "FullyQualifiedName~AuthenticationTests.Login_WithValidCredentials_ShouldReturn200AndTokens"
```

## Test Coverage

### Authentication Tests (AuthenticationTests.cs)
- ✅ User registration with valid data
- ✅ User registration with mismatched passwords
- ✅ User registration with weak password
- ✅ User registration with missing fields
- ✅ Login with valid credentials
- ✅ Login with invalid credentials
- ✅ Login with missing fields
- ✅ Get current user with valid token
- ✅ Get current user without token
- ✅ Refresh token with valid refresh token

### Test Management Tests (TestsManagementTests.cs)
- ✅ Create test with valid data
- ✅ Create test without authentication
- ✅ Create test with missing title
- ✅ List tests (author's tests)
- ✅ Get test details with valid slug
- ✅ Get test details with invalid slug
- ✅ Update test with valid data
- ✅ Delete test with valid slug
- ✅ Add question to test with valid data

## Technology Stack

- **Framework:** .NET 8.0
- **Testing Framework:** xUnit
- **Assertion Library:** FluentAssertions
- **HTTP Client:** System.Net.Http.HttpClient
- **Configuration:** Microsoft.Extensions.Configuration
- **Serialization:** System.Text.Json

## Notes

- Each test creates unique users to avoid conflicts
- Tests use the public API at https://exampractice.com
- Authentication tokens are managed per test class
- Tests are designed to be independent and can run in any order
- Cleanup is handled automatically via IDisposable

## Adding New Tests

1. Create model classes in `Models/` for request/response DTOs
2. Add helper methods to `ApiClient.cs` if needed
3. Create new test class in `Tests/` directory
4. Inherit from `IDisposable` for proper cleanup
5. Use `FluentAssertions` for readable assertions

## Example Test

```csharp
[Fact]
public async Task CreateTest_WithValidData_ShouldReturn201AndTestData()
{
    // Arrange
    var token = await AuthenticateAndGetToken();
    _apiClient.SetAuthToken(token);

    var request = new CreateTestRequest
    {
        Title = "My Test",
        Description = "Test description"
    };

    // Act
    var response = await _apiClient.PostAsync("/tests/", request);
    var body = await _apiClient.GetResponseBodyAsync(response);

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.Created,
        $"because test creation should succeed. Response: {body}");
}
```

## Troubleshooting

### Connection Issues
If tests fail with connection errors, verify:
- The API URL in `appsettings.json` is correct
- You have internet connectivity
- The API server is running and accessible

### Authentication Issues
If authentication tests fail:
- Check if the API is accepting new registrations
- Verify the password requirements haven't changed
- Look at the response body for specific error messages

### Test Failures
For debugging failed tests:
1. Run tests with verbose logging: `dotnet test --logger "console;verbosity=detailed"`
2. Check the response body printed in assertions
3. Verify the API specification hasn't changed
