using System.Net;
using TestIT.ApiTests.Models;

namespace TestIT.ApiTests.Helpers;

/// <summary>
/// Utility for cleaning up test data created during test runs.
/// Can identify and delete tests, users, companies, etc. created by automated tests.
/// </summary>
public class TestDataCleanup : IDisposable
{
    private readonly ApiClient _apiClient;
    private readonly List<string> _testPrefixes = new()
    {
        "Stress_",
        "IntegrityTest_",
        "Scorable_",
        "UpdateQ_",
        "DeleteQ_",
        "Reorder_",
        "MultiSelect_",
        "SingleQ_",
        "MultiSelectPartialCredit_",
        "PublicTake_",
        "PwProtected_",
        "VerifyPW_",
        "WrongPW_",
        "AnonStart_",
        "ReadStress_",
        "XSSSec_",
        "UpdateSec_",
        "DeleteSec_",
        "AnswerSec_",
        "ResultsSec_",
        "DoubleSubmit_",
        "DraftAfterSubmit_",
        "AnalyticsEmpty_",
        "AnalyticsTest_",
        "My Test",
        "Original Title",
        "Test To Delete",
        "Detailed Test",
        "Unauthorized Test",
        "Math Test",
        "JavaScript Basics"
    };

    private readonly List<string> _companyPrefixes = new()
    {
        "CompanyA_",
        "CompanyB_",
        "TestCompany_",
        "Stress"
    };

    public TestDataCleanup(string baseUrl)
    {
        _apiClient = new ApiClient(baseUrl);
    }

    /// <summary>
    /// Identifies test data matching known patterns.
    /// </summary>
    public async Task<CleanupReport> AnalyzeTestDataAsync(string adminToken)
    {
        _apiClient.SetAuthToken(adminToken);
        var report = new CleanupReport();

        try
        {
            // Get all tests
            var testsResponse = await _apiClient.GetAsync("tests/");
            if (testsResponse.StatusCode == HttpStatusCode.OK)
            {
                var tests = await _apiClient.DeserializeResponseAsync<List<TestResponse>>(testsResponse);
                Console.WriteLine($"DEBUG: API returned {tests?.Count ?? 0} total tests");
                if (tests != null)
                {
                    foreach (var test in tests)
                    {
                        if (IsTestData(test.Title))
                        {
                            report.TestsToDelete.Add(new TestItem
                            {
                                Slug = test.Slug,
                                Title = test.Title,
                                CreatedAt = test.CreatedAt
                            });
                        }
                    }
                }
            }

            // Get all companies (if accessible)
            var companiesResponse = await _apiClient.GetAsync("companies/");
            if (companiesResponse.StatusCode == HttpStatusCode.OK)
            {
                var companies = await _apiClient.DeserializeResponseAsync<List<CompanyResponse>>(companiesResponse);
                if (companies != null)
                {
                    foreach (var company in companies)
                    {
                        if (IsTestCompany(company.Name))
                        {
                            report.CompaniesToDelete.Add(new CompanyItem
                            {
                                Id = company.Id,
                                Name = company.Name
                            });
                        }
                    }
                }
            }

            report.TotalItemsFound = report.TestsToDelete.Count + report.CompaniesToDelete.Count;
        }
        catch (Exception ex)
        {
            report.Errors.Add($"Analysis error: {ex.Message}");
        }

        return report;
    }

    /// <summary>
    /// Deletes all identified test data.
    /// </summary>
    public async Task<CleanupResult> CleanupTestDataAsync(string adminToken, bool dryRun = false)
    {
        _apiClient.SetAuthToken(adminToken);
        var result = new CleanupResult { DryRun = dryRun };

        // First, analyze what needs to be deleted
        var report = await AnalyzeTestDataAsync(adminToken);
        result.ItemsFound = report.TotalItemsFound;

        if (dryRun)
        {
            result.Message = $"DRY RUN: Would delete {report.TestsToDelete.Count} tests and {report.CompaniesToDelete.Count} companies";
            result.TestsDeleted = report.TestsToDelete.Count;
            result.CompaniesDeleted = report.CompaniesToDelete.Count;
            return result;
        }

        // Delete tests
        foreach (var test in report.TestsToDelete)
        {
            try
            {
                var response = await _apiClient.DeleteAsync($"tests/{test.Slug}/");
                if (response.StatusCode == HttpStatusCode.NoContent || response.StatusCode == HttpStatusCode.OK)
                {
                    result.TestsDeleted++;
                    result.Details.Add($"✓ Deleted test: {test.Title}");
                }
                else
                {
                    result.Errors.Add($"✗ Failed to delete test {test.Title}: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add($"✗ Error deleting test {test.Title}: {ex.Message}");
            }

            // Small delay to avoid rate limiting
            await Task.Delay(50);
        }

        // Delete companies
        foreach (var company in report.CompaniesToDelete)
        {
            try
            {
                var response = await _apiClient.DeleteAsync($"companies/{company.Id}/");
                if (response.StatusCode == HttpStatusCode.NoContent || response.StatusCode == HttpStatusCode.OK)
                {
                    result.CompaniesDeleted++;
                    result.Details.Add($"✓ Deleted company: {company.Name}");
                }
                else
                {
                    result.Errors.Add($"✗ Failed to delete company {company.Name}: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add($"✗ Error deleting company {company.Name}: {ex.Message}");
            }

            await Task.Delay(50);
        }

        result.Success = result.Errors.Count == 0;
        result.Message = $"Deleted {result.TestsDeleted} tests and {result.CompaniesDeleted} companies";

        return result;
    }

    /// <summary>
    /// Deletes test data older than specified days.
    /// </summary>
    public async Task<CleanupResult> CleanupOldTestDataAsync(string adminToken, int olderThanDays, bool dryRun = false)
    {
        _apiClient.SetAuthToken(adminToken);
        var result = new CleanupResult { DryRun = dryRun };

        var cutoffDate = DateTime.UtcNow.AddDays(-olderThanDays);

        // Get all tests
        var testsResponse = await _apiClient.GetAsync("tests/");
        if (testsResponse.StatusCode != HttpStatusCode.OK)
        {
            result.Errors.Add("Failed to fetch tests");
            return result;
        }

        var tests = await _apiClient.DeserializeResponseAsync<List<TestResponse>>(testsResponse);
        if (tests == null)
        {
            result.Errors.Add("No tests found");
            return result;
        }

        var oldTests = tests.Where(t =>
        {
            return t.CreatedAt < cutoffDate && IsTestData(t.Title);
        }).ToList();

        result.ItemsFound = oldTests.Count;

        if (dryRun)
        {
            result.Message = $"DRY RUN: Would delete {oldTests.Count} tests older than {olderThanDays} days";
            result.TestsDeleted = oldTests.Count;
            return result;
        }

        foreach (var test in oldTests)
        {
            try
            {
                var response = await _apiClient.DeleteAsync($"tests/{test.Slug}/");
                if (response.StatusCode == HttpStatusCode.NoContent || response.StatusCode == HttpStatusCode.OK)
                {
                    result.TestsDeleted++;
                    result.Details.Add($"✓ Deleted old test: {test.Title} (created: {test.CreatedAt})");
                }
                else
                {
                    result.Errors.Add($"✗ Failed to delete {test.Title}: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add($"✗ Error deleting {test.Title}: {ex.Message}");
            }

            await Task.Delay(50);
        }

        result.Success = result.Errors.Count == 0;
        result.Message = $"Deleted {result.TestsDeleted} old tests (older than {olderThanDays} days)";

        return result;
    }

    private bool IsTestData(string title)
    {
        return _testPrefixes.Any(prefix =>
            title.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private bool IsTestCompany(string name)
    {
        return _companyPrefixes.Any(prefix =>
            name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        _apiClient?.Dispose();
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// Data Models
// ═══════════════════════════════════════════════════════════════════════════

public class CleanupReport
{
    public List<TestItem> TestsToDelete { get; set; } = new();
    public List<CompanyItem> CompaniesToDelete { get; set; } = new();
    public int TotalItemsFound { get; set; }
    public List<string> Errors { get; set; } = new();
}

public class TestItem
{
    public string Slug { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public DateTime? CreatedAt { get; set; }
}

public class CompanyItem
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class CleanupResult
{
    public bool Success { get; set; }
    public bool DryRun { get; set; }
    public string Message { get; set; } = string.Empty;
    public int ItemsFound { get; set; }
    public int TestsDeleted { get; set; }
    public int CompaniesDeleted { get; set; }
    public List<string> Details { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}
