# Getting Started with TestIT API Tests

## Quick Start

### 1. Verify .NET Installation
```bash
dotnet --version
# Should show: 8.0.417 or higher
```

### 2. Restore Dependencies
```bash
cd TestIT.ApiTests
dotnet restore
```

### 3. Build the Project
```bash
dotnet build
```

### 4. Run All Tests
```bash
dotnet test
```

## Running Specific Test Groups

### Authentication Tests Only
```bash
dotnet test --filter "FullyQualifiedName~AuthenticationTests"
```

This will run:
- User registration tests (valid data, mismatched passwords, weak passwords, etc.)
- Login tests (valid/invalid credentials)
- Token management tests (refresh tokens)
- Current user retrieval tests

### Test Management Tests Only
```bash
dotnet test --filter "FullyQualifiedName~TestsManagementTests"
```

This will run:
- Test CRUD operations (Create, Read, Update, Delete)
- Authorization tests
- Question management tests

### Run Individual Test
```bash
# Example: Run just the registration test
dotnet test --filter "Register_WithValidData_ShouldReturn200AndUserData"

# Example: Run just the login test
dotnet test --filter "Login_WithValidCredentials_ShouldReturn200AndTokens"
```

## Test Output Options

### Detailed Console Output
```bash
dotnet test --logger "console;verbosity=detailed"
```

### Generate Test Results File
```bash
dotnet test --logger "trx;LogFileName=test-results.xml"
```

### Show Only Failed Tests
```bash
dotnet test --logger "console;verbosity=normal"
```

## Configuration

### Update API Base URL
Edit `appsettings.json` to change the API endpoint:
```json
{
  "ApiSettings": {
    "BaseUrl": "https://your-api-domain.com/api"
  }
}
```

### Test Different Environments
You can create multiple configuration files:
- `appsettings.json` - Default
- `appsettings.Development.json` - Local development
- `appsettings.Production.json` - Production API

## Troubleshooting

### Issue: Tests hang or timeout
**Cause:** API is not accessible or responding slowly

**Solution:**
1. Check if the API URL is correct in `appsettings.json`
2. Test connectivity: `curl https://exampractice.com/api/`
3. Increase timeout if needed (modify test code)

### Issue: All authentication tests fail with 401
**Cause:** API authentication might require additional headers or different format

**Solution:**
1. Check API documentation for current auth requirements
2. Verify the endpoint paths in the specification
3. Test with curl or Postman first to confirm endpoints

### Issue: Registration tests fail with "Email already exists"
**Cause:** Test users already registered from previous runs

**Solution:** Tests automatically generate unique emails using GUIDs to avoid conflicts

### Issue: Build errors
**Cause:** Missing dependencies or .NET SDK version mismatch

**Solution:**
```bash
# Clean and rebuild
dotnet clean
dotnet restore
dotnet build
```

## Next Steps

1. **Add More Test Coverage:**
   - Company management tests
   - Folder management tests
   - Test-taking flow tests
   - Analytics tests

2. **Add Test Data Builders:**
   Create helper classes for building test data more easily

3. **Add Integration with CI/CD:**
   - GitHub Actions
   - Azure Pipelines
   - Jenkins

4. **Add Test Reports:**
   - HTML test reports
   - Code coverage reports
   - Performance metrics

## Example: Running Tests in CI/CD

### GitHub Actions
```yaml
name: API Tests

on: [push, pull_request]

jobs:
  test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v2
      - name: Setup .NET
        uses: actions/setup-dotnet@v1
        with:
          dotnet-version: '8.0.x'
      - name: Restore dependencies
        run: dotnet restore TestIT.ApiTests/TestIT.ApiTests.csproj
      - name: Build
        run: dotnet build TestIT.ApiTests/TestIT.ApiTests.csproj
      - name: Run tests
        run: dotnet test TestIT.ApiTests/TestIT.ApiTests.csproj --logger "trx"
```

## Useful Commands Reference

```bash
# List all tests without running them
dotnet test --list-tests

# Run tests in parallel
dotnet test --parallel

# Run with specific framework
dotnet test --framework net8.0

# Set environment variable for tests
API_URL=https://staging.api.com dotnet test

# Run tests and collect code coverage
dotnet test /p:CollectCoverage=true

# Watch mode - automatically rerun tests on code changes
dotnet watch test
```
