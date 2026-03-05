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

    // =========================================================================
    // VALIDATION — bad input on create / update
    // =========================================================================

    /// <summary>
    /// Verifies that creating a folder with an empty name is rejected with 400 Bad Request,
    /// confirming the API enforces name validation rather than persisting blank folders.
    /// </summary>
    [Fact]
    public async Task FolderCreation_WithEmptyName_ReturnsBadRequest()
    {
        var company = await SetupCompanyAsync();

        var response = await _apiClient.PostAsync($"companies/{company.Id}/folders/",
            new CreateFolderRequest { Name = "" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "because a folder must have a non-empty name");
    }

    /// <summary>
    /// Verifies that updating a folder to have an empty name is rejected with 400 Bad Request.
    /// </summary>
    [Fact]
    public async Task FolderUpdate_WithEmptyName_ReturnsBadRequest()
    {
        var company = await SetupCompanyAsync();
        var folder = await CreateFolderAsync(company.Id, "ValidName");

        var response = await _apiClient.PutAsync($"companies/{company.Id}/folders/{folder.Id}/",
            new UpdateFolderRequest { Name = "" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "because renaming a folder to an empty string should be rejected");
    }

    /// <summary>
    /// Verifies that updating a folder that does not exist returns 404 Not Found,
    /// rather than silently succeeding or creating a new record.
    /// </summary>
    [Fact]
    public async Task FolderUpdate_NonExistentId_ReturnsNotFound()
    {
        var company = await SetupCompanyAsync();

        var response = await _apiClient.PutAsync($"companies/{company.Id}/folders/99999/",
            new UpdateFolderRequest { Name = "Ghost Folder" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "because folder id 99999 does not exist");
    }

    /// <summary>
    /// Verifies that attempting to delete a folder that does not exist returns 404 Not Found.
    /// </summary>
    [Fact]
    public async Task FolderDeletion_NonExistentId_ReturnsNotFound()
    {
        var company = await SetupCompanyAsync();

        var response = await _apiClient.DeleteAsync($"companies/{company.Id}/folders/99999/");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "because folder id 99999 does not exist");
    }

    // =========================================================================
    // ACCESS CONTROL — cross-user operations
    // =========================================================================

    /// <summary>
    /// Verifies that a user cannot create a folder inside another user's company.
    /// </summary>
    [Fact]
    public async Task FolderCreation_InAnotherUsersCompany_DeniesAccess()
    {
        var company = await SetupCompanyAsync();

        using var userB = new ApiClient(TestConfiguration.GetBaseUrl());
        await TestDataHelper.RegisterAndLoginAsync(userB);

        var response = await userB.PostAsync($"companies/{company.Id}/folders/",
            new CreateFolderRequest { Name = "Intruder Folder" });

        response.StatusCode.Should().BeOneOf(
            new[] { HttpStatusCode.Forbidden, HttpStatusCode.NotFound },
            "because user B must not be able to create folders in user A's company");
    }

    /// <summary>
    /// Verifies that a user cannot rename a folder belonging to another user's company.
    /// </summary>
    [Fact]
    public async Task FolderUpdate_ByNonOwner_DeniesAccess()
    {
        var company = await SetupCompanyAsync();
        var folder = await CreateFolderAsync(company.Id, "OwnerFolder");

        using var userB = new ApiClient(TestConfiguration.GetBaseUrl());
        await TestDataHelper.RegisterAndLoginAsync(userB);

        var response = await userB.PutAsync($"companies/{company.Id}/folders/{folder.Id}/",
            new UpdateFolderRequest { Name = "Hijacked Name" });

        response.StatusCode.Should().BeOneOf(
            new[] { HttpStatusCode.Forbidden, HttpStatusCode.NotFound },
            "because user B must not be able to rename user A's folder");
    }

    /// <summary>
    /// Verifies that a user cannot delete a folder belonging to another user's company.
    /// </summary>
    [Fact]
    public async Task FolderDeletion_ByNonOwner_DeniesAccess()
    {
        var company = await SetupCompanyAsync();
        var folder = await CreateFolderAsync(company.Id, "ProtectedFolder");

        using var userB = new ApiClient(TestConfiguration.GetBaseUrl());
        await TestDataHelper.RegisterAndLoginAsync(userB);

        var response = await userB.DeleteAsync($"companies/{company.Id}/folders/{folder.Id}/");

        response.StatusCode.Should().BeOneOf(
            new[] { HttpStatusCode.Forbidden, HttpStatusCode.NotFound },
            "because user B must not be able to delete user A's folder");
    }

    /// <summary>
    /// Verifies that a user cannot list folders in another user's company.
    /// </summary>
    [Fact]
    public async Task FolderListing_AnotherUsersCompany_DeniesAccess()
    {
        var company = await SetupCompanyAsync();
        await CreateFolderAsync(company.Id, "Private Folder");

        using var userB = new ApiClient(TestConfiguration.GetBaseUrl());
        await TestDataHelper.RegisterAndLoginAsync(userB);

        var response = await userB.GetAsync($"companies/{company.Id}/folders/");

        response.StatusCode.Should().BeOneOf(
            new[] { HttpStatusCode.Forbidden, HttpStatusCode.NotFound },
            "because user B must not be able to see user A's folder list");
    }

    // =========================================================================
    // DELETION EDGE CASES
    // =========================================================================

    /// <summary>
    /// Verifies that after deleting a folder it no longer appears in the folder list,
    /// confirming the deletion is reflected immediately rather than soft-deleted.
    /// </summary>
    [Fact]
    public async Task FolderListing_AfterDeletion_FolderNoLongerAppears()
    {
        var company = await SetupCompanyAsync();
        var folder = await CreateFolderAsync(company.Id, "Temporary Folder");

        await _apiClient.DeleteAsync($"companies/{company.Id}/folders/{folder.Id}/");

        var folders = await _apiClient.GetAsync<List<FolderResponse>>($"companies/{company.Id}/folders/");
        folders.Should().NotContain(f => f.Id == folder.Id,
            "because the deleted folder should no longer be listed");
    }

    /// <summary>
    /// Verifies the API's behaviour when deleting a folder that has a test assigned to it.
    /// The folder should either be deleted and the test's folder field cleared, or the
    /// deletion should be rejected — in either case the test record must remain intact.
    /// </summary>
    [Fact]
    public async Task FolderDeletion_WithAssignedTest_TestRecordRemainsIntact()
    {
        var company = await SetupCompanyAsync();
        var folder = await CreateFolderAsync(company.Id, "OccupiedFolder");
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"OccupiedTest_{Guid.NewGuid().ToString("N")[..8]}");

        await _apiClient.PatchAsync($"tests/{test.Slug}/", new { folder = folder.Id });

        var deleteResponse = await _apiClient.DeleteAsync($"companies/{company.Id}/folders/{folder.Id}/");

        if (deleteResponse.IsSuccessStatusCode)
        {
            // Folder was removed — the test's folder field should be cleared
            var updatedTest = await _apiClient.GetAsync<TestResponse>($"tests/{test.Slug}/");
            updatedTest.Should().NotBeNull("because the test itself should not be deleted");
            updatedTest!.Folder.Should().BeNull(
                "because deleting the folder should unlink the assigned test");
        }
        else
        {
            // Deletion was rejected — the folder and test must still exist
            deleteResponse.StatusCode.Should().BeOneOf(
                new[] { HttpStatusCode.BadRequest, HttpStatusCode.Conflict },
                "because the only valid rejection reasons are bad request or conflict");

            var folders = await _apiClient.GetAsync<List<FolderResponse>>($"companies/{company.Id}/folders/");
            folders.Should().Contain(f => f.Id == folder.Id,
                "because if deletion was rejected the folder must still exist");
        }
    }

    // =========================================================================
    // NESTED FOLDERS
    // =========================================================================

    /// <summary>
    /// Verifies that three levels of folder nesting (grandparent → parent → child) are
    /// created correctly, with each folder reporting its immediate parent's id.
    /// </summary>
    [Fact]
    public async Task FolderCreation_ThreeLevelsDeep_CorrectParentChain()
    {
        var company = await SetupCompanyAsync();

        var grandparent = await CreateFolderAsync(company.Id, "Grandparent");
        var parent      = await CreateFolderAsync(company.Id, "Parent",      grandparent.Id);
        var child       = await CreateFolderAsync(company.Id, "Child",       parent.Id);

        grandparent.Parent.Should().BeNull("because grandparent is a top-level folder");
        parent.Parent.Should().Be(grandparent.Id, "because parent's parent is the grandparent");
        child.Parent.Should().Be(parent.Id, "because child's parent is the parent");
    }

    // =========================================================================
    // TEST COUNT ACCURACY
    // =========================================================================

    /// <summary>
    /// Verifies that assigning three tests to the same folder increments test_count to 3,
    /// confirming the counter tracks all assignments rather than just the first.
    /// </summary>
    [Fact]
    public async Task TestFolderAssignment_MultipleTests_CountReflectsAllAssignments()
    {
        var company = await SetupCompanyAsync();
        var folder = await CreateFolderAsync(company.Id, "BulkFolder");

        for (int i = 1; i <= 3; i++)
        {
            var test = await TestDataHelper.CreateTestAsync(_apiClient,
                $"BulkTest{i}_{Guid.NewGuid().ToString("N")[..6]}");
            await _apiClient.PatchAsync($"tests/{test.Slug}/", new { folder = folder.Id });
        }

        var folderDetail = await _apiClient.GetAsync<FolderResponse>(
            $"companies/{company.Id}/folders/{folder.Id}/");
        folderDetail!.TestCount.Should().Be(3,
            "because three tests were assigned to this folder");
    }

    /// <summary>
    /// Verifies that unassigning a test from a folder decrements test_count back to 0,
    /// confirming the counter stays in sync on both assignment and removal.
    /// </summary>
    [Fact]
    public async Task TestFolderAssignment_Unassign_DecrementsTestCount()
    {
        var company = await SetupCompanyAsync();
        var folder = await CreateFolderAsync(company.Id, "DecrementFolder");
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"DecrementTest_{Guid.NewGuid().ToString("N")[..8]}");

        await _apiClient.PatchAsync($"tests/{test.Slug}/", new { folder = folder.Id });

        var afterAssign = await _apiClient.GetAsync<FolderResponse>(
            $"companies/{company.Id}/folders/{folder.Id}/");
        afterAssign!.TestCount.Should().Be(1, "because one test was just assigned");

        await _apiClient.PatchAsync($"tests/{test.Slug}/",
            new Dictionary<string, object?> { { "folder", null } });

        var afterUnassign = await _apiClient.GetAsync<FolderResponse>(
            $"companies/{company.Id}/folders/{folder.Id}/");
        afterUnassign!.TestCount.Should().Be(0,
            "because the test was unassigned and the count should decrement back to 0");
    }

    public void Dispose()
    {
        _apiClient?.Dispose();
    }
}
