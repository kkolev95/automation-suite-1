using System.Net;
using FluentAssertions;
using TestIT.ApiTests.Helpers;
using TestIT.ApiTests.Models;
using Xunit;
using Xunit.Abstractions;

namespace TestIT.ApiTests.Tests;

/// <summary>
/// Verifies cascade behavior when companies and folders are deleted:
///   - Company deletion cascades to all company-owned tests (they are deleted)
///   - Company deletion does NOT affect the author's personal tests
///   - Folder deletion unlinks assigned tests (folder field → null)
///   - Folder deletion does NOT delete the tests themselves
/// </summary>
public class CascadingDeleteTests : IDisposable
{
    private readonly ApiClient _apiClient;

    public CascadingDeleteTests(ITestOutputHelper output)
    {
        ApiClient.SetOutput(output.WriteLine);
        _apiClient = new ApiClient(TestConfiguration.GetBaseUrl());
    }

    [Fact]
    [Trait("Category", "Functional")]
    [Trait("Priority", "P1")]
    public async Task CompanyDeletion_CascadesToCompanyTests_TheyAreDeleted()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var companyResp = await _apiClient.PostAsync("companies/",
            new CreateCompanyRequest { Name = $"CascadeCo_{Guid.NewGuid().ToString("N")[..6]}" });
        var company = await _apiClient.DeserializeResponseAsync<CompanyResponse>(companyResp);

        var testResp = await _apiClient.PostAsync($"tests/company/{company!.Id}/",
            new CreateCompanyTestRequest { Title = $"CascadeTest_{Guid.NewGuid().ToString("N")[..6]}" });
        var test = await _apiClient.DeserializeResponseAsync<TestResponse>(testResp);

        // Verify the company test exists before deletion
        var beforeResp = await _apiClient.GetAsync($"tests/company/{company.Id}/{test!.Slug}/");
        beforeResp.StatusCode.Should().Be(HttpStatusCode.OK,
            "company test must be accessible before the company is deleted");

        // Act: delete the company
        var deleteResp = await _apiClient.DeleteAsync($"companies/{company.Id}/");
        deleteResp.StatusCode.Should().Be(HttpStatusCode.NoContent,
            "company deletion must return 204 No Content");

        // Assert: company test is gone via both the company endpoint and the personal endpoint
        var viaCompanyEndpoint = await _apiClient.GetAsync($"tests/company/{company.Id}/{test.Slug}/");
        viaCompanyEndpoint.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "company test must be cascade-deleted when its company is deleted");

        var viaPersonalEndpoint = await _apiClient.GetAsync($"tests/{test.Slug}/");
        viaPersonalEndpoint.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "company test must not be accessible via the personal endpoint after the company is deleted");
    }

    [Fact]
    [Trait("Category", "Functional")]
    [Trait("Priority", "P1")]
    public async Task CompanyDeletion_DoesNotAffectPersonalTests()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var companyResp = await _apiClient.PostAsync("companies/",
            new CreateCompanyRequest { Name = $"PersonalCo_{Guid.NewGuid().ToString("N")[..6]}" });
        var company = await _apiClient.DeserializeResponseAsync<CompanyResponse>(companyResp);

        // Create a personal test (not associated with the company)
        var personalTest = await TestDataHelper.CreateTestAsync(_apiClient,
            $"PersonalTest_{Guid.NewGuid().ToString("N")[..6]}");

        // Act: delete the company
        var deleteResp = await _apiClient.DeleteAsync($"companies/{company!.Id}/");
        deleteResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Assert: personal test is unaffected
        var checkResp = await _apiClient.GetAsync($"tests/{personalTest.Slug}/");
        checkResp.StatusCode.Should().Be(HttpStatusCode.OK,
            "personal tests must survive when an unrelated company is deleted");
    }

    [Fact]
    [Trait("Category", "Functional")]
    [Trait("Priority", "P1")]
    public async Task FolderDeletion_UnlinksAssignedTest_FolderFieldBecomesNull()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var companyResp = await _apiClient.PostAsync("companies/",
            new CreateCompanyRequest { Name = $"FolderUnlinkCo_{Guid.NewGuid().ToString("N")[..6]}" });
        var company = await _apiClient.DeserializeResponseAsync<CompanyResponse>(companyResp);

        var folderResp = await _apiClient.PostAsync($"companies/{company!.Id}/folders/",
            new CreateFolderRequest { Name = "Unlink Folder" });
        var folder = await _apiClient.DeserializeResponseAsync<FolderResponse>(folderResp);

        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"FolderTest_{Guid.NewGuid().ToString("N")[..6]}");

        // Assign the test to the folder
        var assignResp = await _apiClient.PatchAsync($"tests/{test.Slug}/", new { folder = folder!.Id });
        assignResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var assigned = await _apiClient.DeserializeResponseAsync<TestResponse>(assignResp);
        assigned!.Folder.Should().Be(folder.Id,
            "test must be assigned to the folder before we test deletion");

        // Act: delete the folder
        var deleteResp = await _apiClient.DeleteAsync($"companies/{company.Id}/folders/{folder.Id}/");
        deleteResp.StatusCode.Should().Be(HttpStatusCode.NoContent,
            "folder deletion must return 204 No Content");

        // Assert: test's folder field is now null
        var testAfter = await _apiClient.GetAsync<TestResponse>($"tests/{test.Slug}/");
        testAfter!.Folder.Should().BeNull(
            "when a folder is deleted, assigned tests must be unlinked — folder field must become null");
    }

    [Fact]
    [Trait("Category", "Functional")]
    [Trait("Priority", "P1")]
    public async Task FolderDeletion_AssignedTest_RemainsFullyAccessible()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var companyResp = await _apiClient.PostAsync("companies/",
            new CreateCompanyRequest { Name = $"FolderSurviveCo_{Guid.NewGuid().ToString("N")[..6]}" });
        var company = await _apiClient.DeserializeResponseAsync<CompanyResponse>(companyResp);

        var folderResp = await _apiClient.PostAsync($"companies/{company!.Id}/folders/",
            new CreateFolderRequest { Name = "Survive Folder" });
        var folder = await _apiClient.DeserializeResponseAsync<FolderResponse>(folderResp);

        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"SurviveTest_{Guid.NewGuid().ToString("N")[..6]}");

        await _apiClient.PatchAsync($"tests/{test.Slug}/", new { folder = folder!.Id });

        // Act: delete the folder
        await _apiClient.DeleteAsync($"companies/{company.Id}/folders/{folder.Id}/");

        // Assert: test still exists and is fully accessible
        var checkResp = await _apiClient.GetAsync($"tests/{test.Slug}/");
        checkResp.StatusCode.Should().Be(HttpStatusCode.OK,
            "deleting a folder must not delete the tests assigned to it — only the folder association is removed");
    }

    public void Dispose() => _apiClient?.Dispose();
}
