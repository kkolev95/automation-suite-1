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

    public void Dispose()
    {
        _apiClient?.Dispose();
    }
}
