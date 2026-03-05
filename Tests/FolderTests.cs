using System.Net;
using FluentAssertions;
using TestIT.ApiTests.Helpers;
using TestIT.ApiTests.Models;
using Xunit;

namespace TestIT.ApiTests.Tests;

public class FolderTests : IDisposable
{
    private readonly ApiClient _apiClient;

    public FolderTests(ITestOutputHelper output)
    {
        ApiClient.SetOutput(output.WriteLine);
        _apiClient = new ApiClient(TestConfiguration.GetBaseUrl());
    }

    /// <summary>
    /// Registers a user, logs in, creates a company, and returns the company with its id.
    /// API now returns full object including ID in create response (fixed in v1.8).
    /// </summary>
    private async Task<CompanyResponse> SetupCompanyAsync()
    {
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);

        var name = $"FolderCorp_{Guid.NewGuid().ToString("N")[..8]}";
        var response = await _apiClient.PostAsync("companies/", new CreateCompanyRequest { Name = name });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await _apiClient.DeserializeResponseAsync<CompanyResponse>(response))!;
    }

    /// <summary>
    /// Creates a folder and retrieves its full record (including id) from the list endpoint.
    /// Folder create response doesn't include id.
    /// </summary>
    private async Task<FolderResponse> CreateFolderAsync(int companyId, string name, int? parent = null)
    {
        await _apiClient.PostAsync($"companies/{companyId}/folders/",
            new CreateFolderRequest { Name = name, Parent = parent });
        var folders = await _apiClient.GetAsync<List<FolderResponse>>($"companies/{companyId}/folders/");
        return folders!.First(f => f.Name == name);
    }

    [Fact]
    public async Task FolderCreation_TopLevel_CreatesFolderSuccessfully()
    {
        var company = await SetupCompanyAsync();

        var response = await _apiClient.PostAsync($"companies/{company.Id}/folders/",
            new CreateFolderRequest { Name = "Math Folder" });
        var body = await _apiClient.GetResponseBodyAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.Created, $"Response: {body}");

        // Create response doesn't include id; verify via list
        var folders = await _apiClient.GetAsync<List<FolderResponse>>($"companies/{company.Id}/folders/");
        folders.Should().NotBeNull();
        folders.Should().Contain(f => f.Name == "Math Folder");
        folders!.First(f => f.Name == "Math Folder").Parent.Should().BeNull();
    }

    [Fact]
    public async Task FolderListing_AfterCreation_IncludesNewFolder()
    {
        var company = await SetupCompanyAsync();

        var folderName = $"ListFolder_{Guid.NewGuid().ToString("N")[..8]}";
        await _apiClient.PostAsync($"companies/{company.Id}/folders/",
            new CreateFolderRequest { Name = folderName });

        var response = await _apiClient.GetAsync($"companies/{company.Id}/folders/");
        var body = await _apiClient.GetResponseBodyAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {body}");

        var folders = await _apiClient.DeserializeResponseAsync<List<FolderResponse>>(response);
        folders.Should().NotBeNull();
        folders.Should().Contain(f => f.Name == folderName);
    }

    [Fact]
    public async Task FolderUpdate_WithNewName_RenamesFolderSuccessfully()
    {
        var company = await SetupCompanyAsync();
        var folder = await CreateFolderAsync(company.Id, "OldFolderName");

        var response = await _apiClient.PutAsync($"companies/{company.Id}/folders/{folder.Id}/",
            new UpdateFolderRequest { Name = "NewFolderName" });
        var body = await _apiClient.GetResponseBodyAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {body}");

        var updated = await _apiClient.DeserializeResponseAsync<FolderResponse>(response);
        updated.Should().NotBeNull();
        updated!.Name.Should().Be("NewFolderName");
    }

    [Fact]
    public async Task FolderDeletion_WithValidId_RemovesFolder()
    {
        var company = await SetupCompanyAsync();
        var folder = await CreateFolderAsync(company.Id, "DeleteMe");

        var response = await _apiClient.DeleteAsync($"companies/{company.Id}/folders/{folder.Id}/");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, "because folder deletion should succeed");
    }

    [Fact]
    public async Task FolderCreation_WithParent_CreatesNestedFolderStructure()
    {
        var company = await SetupCompanyAsync();

        // Create parent folder and get its id
        var parent = await CreateFolderAsync(company.Id, "Parent Folder");

        // Create child folder with parent id
        var response = await _apiClient.PostAsync($"companies/{company.Id}/folders/", new CreateFolderRequest
        {
            Name = "Child Folder",
            Parent = parent.Id
        });
        var body = await _apiClient.GetResponseBodyAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.Created, $"Response: {body}");

        // Verify via list
        var folders = await _apiClient.GetAsync<List<FolderResponse>>($"companies/{company.Id}/folders/");
        var child = folders!.First(f => f.Name == "Child Folder");
        child.Parent.Should().Be(parent.Id);
    }

    /// <summary>
    /// Verifies that PATCHing a test with a valid folder ID assigns the test to that
    /// folder and the response reflects the updated folder field.
    /// </summary>
    [Fact]
    public async Task TestFolderAssignment_WithValidFolder_AssignsTestToFolder()
    {
        var company = await SetupCompanyAsync();
        var folder = await CreateFolderAsync(company.Id, "Engineering");

        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"AssignTest_{Guid.NewGuid().ToString("N")[..8]}");

        var response = await _apiClient.PatchAsync($"tests/{test.Slug}/",
            new { folder = folder.Id });
        var body = await _apiClient.GetResponseBodyAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {body}");

        var updated = await _apiClient.DeserializeResponseAsync<TestResponse>(response);
        updated.Should().NotBeNull();
        updated!.Folder.Should().Be(folder.Id, "because the test should be assigned to the specified folder");
    }

    /// <summary>
    /// Verifies that PATCHing a test with folder set to null removes the folder
    /// assignment, returning null in the folder field of the response.
    /// </summary>
    [Fact]
    public async Task TestFolderAssignment_Unassign_ClearsFolder()
    {
        var company = await SetupCompanyAsync();
        var folder = await CreateFolderAsync(company.Id, "TempFolder");

        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"UnassignTest_{Guid.NewGuid().ToString("N")[..8]}");

        // Assign first
        await _apiClient.PatchAsync($"tests/{test.Slug}/", new { folder = folder.Id });

        // Unassign by setting folder to null — use a dictionary so the null value is not dropped by the serializer
        var response = await _apiClient.PatchAsync($"tests/{test.Slug}/",
            new Dictionary<string, object?> { { "folder", null } });
        var body = await _apiClient.GetResponseBodyAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {body}");

        var updated = await _apiClient.DeserializeResponseAsync<TestResponse>(response);
        updated!.Folder.Should().BeNull("because the test was removed from the folder");
    }

    /// <summary>
    /// Verifies that assigning a test to a folder increments the folder's test_count
    /// from 0 to 1, confirming the counter is kept in sync.
    /// </summary>
    [Fact]
    public async Task TestFolderAssignment_UpdatesFolderTestCount()
    {
        var company = await SetupCompanyAsync();
        var folder = await CreateFolderAsync(company.Id, "CountFolder");

        // test_count should start at 0
        var folderBefore = await _apiClient.GetAsync<FolderResponse>(
            $"companies/{company.Id}/folders/{folder.Id}/");
        folderBefore!.TestCount.Should().Be(0, "because no tests are assigned yet");

        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"CountTest_{Guid.NewGuid().ToString("N")[..8]}");

        // Assign test to folder
        await _apiClient.PatchAsync($"tests/{test.Slug}/", new { folder = folder.Id });

        var folderAfter = await _apiClient.GetAsync<FolderResponse>(
            $"companies/{company.Id}/folders/{folder.Id}/");
        folderAfter!.TestCount.Should().Be(1, "because one test is now assigned to this folder");
    }

    /// <summary>
    /// Verifies that the test detail endpoint includes the folder and updated_at fields
    /// after a folder assignment, and that updated_at is later than created_at.
    /// </summary>
    [Fact]
    public async Task TestResponse_DetailEndpoint_IncludesFolderAndUpdatedAt()
    {
        var company = await SetupCompanyAsync();
        var folder = await CreateFolderAsync(company.Id, "MetaFolder");

        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"MetaTest_{Guid.NewGuid().ToString("N")[..8]}");
        await _apiClient.PatchAsync($"tests/{test.Slug}/", new { folder = folder.Id });

        var response = await _apiClient.GetAsync($"tests/{test.Slug}/");
        var full = await _apiClient.DeserializeResponseAsync<TestResponse>(response);

        full.Should().NotBeNull();
        full!.Folder.Should().Be(folder.Id, "because the folder was assigned");
        full.UpdatedAt.Should().NotBeNull("because updated_at is now returned in the test detail endpoint");
        full.UpdatedAt.Should().BeAfter(full.CreatedAt!.Value,
            "because the test was updated after it was created");
    }

    public void Dispose()
    {
        _apiClient?.Dispose();
    }
}
