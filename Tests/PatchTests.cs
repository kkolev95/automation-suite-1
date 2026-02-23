using System.Net;
using FluentAssertions;
using TestIT.ApiTests.Helpers;
using TestIT.ApiTests.Models;
using Xunit.Abstractions;

namespace TestIT.ApiTests.Tests;

/// <summary>
/// Tests for PATCH endpoints — verifying partial updates leave untouched fields intact.
/// </summary>
public class PatchTests : IDisposable
{
    private readonly ApiClient _apiClient;
    private bool _disposed;

    public PatchTests(ITestOutputHelper output)
    {
        ApiClient.SetOutput(output.WriteLine);
        _apiClient = new ApiClient(TestConfiguration.GetBaseUrl());
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // PATCH /auth/me/ — Profile updates
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task PatchProfile_FirstNameOnly_UpdatesFirstNamePreservesLastName()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var original = await _apiClient.GetAsync<UserResponse>("auth/me/");

        // Act — send only first_name
        var response = await _apiClient.PatchAsync("auth/me/", new UpdateProfileRequest
        {
            FirstName = "UpdatedFirst"
        });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "PATCH with a single field should succeed");

        var updated = await _apiClient.DeserializeResponseAsync<UserResponse>(response);
        updated!.FirstName.Should().Be("UpdatedFirst", "first name should be updated");
        updated.LastName.Should().Be(original!.LastName, "last name should be unchanged");
    }

    [Fact]
    public async Task PatchProfile_LastNameOnly_UpdatesLastNamePreservesFirstName()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var original = await _apiClient.GetAsync<UserResponse>("auth/me/");

        // Act — send only last_name
        var response = await _apiClient.PatchAsync("auth/me/", new UpdateProfileRequest
        {
            LastName = "UpdatedLast"
        });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await _apiClient.DeserializeResponseAsync<UserResponse>(response);
        updated!.LastName.Should().Be("UpdatedLast", "last name should be updated");
        updated.FirstName.Should().Be(original!.FirstName, "first name should be unchanged");
    }

    [Fact]
    public async Task PatchProfile_BothNames_UpdatesBothFields()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);

        // Act
        var response = await _apiClient.PatchAsync("auth/me/", new UpdateProfileRequest
        {
            FirstName = "NewFirst",
            LastName = "NewLast"
        });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await _apiClient.DeserializeResponseAsync<UserResponse>(response);
        updated!.FirstName.Should().Be("NewFirst");
        updated.LastName.Should().Be("NewLast");
    }

    [Fact]
    public async Task PatchProfile_Unauthenticated_DeniesAccess()
    {
        // Arrange — no auth token
        using var anonClient = new ApiClient(TestConfiguration.GetBaseUrl());

        // Act
        var response = await anonClient.PatchAsync("auth/me/", new UpdateProfileRequest
        {
            FirstName = "Hacker"
        });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "unauthenticated profile updates must be rejected");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // PATCH /tests/{slug}/ — Partial test updates
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task PatchTest_TitleOnly_UpdatesTitlePreservesOtherFields()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"Patch_Original_{Guid.NewGuid().ToString("N")[..8]}",
            description: "Original description",
            visibility: "link_only",
            maxAttempts: 3);

        // Act — patch only the title
        var response = await _apiClient.PatchAsync($"tests/{test.Slug}/", new
        {
            title = "Patched Title"
        });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "PATCH with only title should succeed");

        var updated = await _apiClient.DeserializeResponseAsync<TestResponse>(response);
        updated!.Title.Should().Be("Patched Title", "title should be updated");
        updated.Description.Should().Be("Original description", "description should be unchanged");
        updated.Visibility.Should().Be("link_only", "visibility should be unchanged");
        updated.MaxAttempts.Should().Be(3, "max attempts should be unchanged");
    }

    [Fact]
    public async Task PatchTest_VisibilityOnly_UpdatesVisibilityPreservesTitle()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"Patch_Visibility_{Guid.NewGuid().ToString("N")[..8]}",
            visibility: "link_only");

        // Act — patch only visibility
        var response = await _apiClient.PatchAsync($"tests/{test.Slug}/", new
        {
            visibility = "public"
        });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await _apiClient.DeserializeResponseAsync<TestResponse>(response);
        updated!.Visibility.Should().Be("public", "visibility should be updated");
        updated.Title.Should().Be(test.Title, "title should be unchanged");
    }

    [Fact]
    public async Task PatchTest_Unauthenticated_DeniesAccess()
    {
        // Arrange — create test as authenticated user
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"Patch_AuthCheck_{Guid.NewGuid().ToString("N")[..8]}");

        // Act — try to patch without token
        using var anonClient = new ApiClient(TestConfiguration.GetBaseUrl());
        var response = await anonClient.PatchAsync($"tests/{test.Slug}/", new
        {
            title = "Stolen Title"
        });

        // Assert
        response.StatusCode.Should().BeOneOf(
            new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden },
            "unauthenticated test updates must be rejected");
    }

    [Fact]
    public async Task PatchTest_ByNonOwner_DeniesAccess()
    {
        // Arrange — user A creates a test
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"Patch_Ownership_{Guid.NewGuid().ToString("N")[..8]}");

        // User B attempts to patch it
        using var userBClient = new ApiClient(TestConfiguration.GetBaseUrl());
        await TestDataHelper.RegisterAndLoginAsync(userBClient);

        // Act
        var response = await userBClient.PatchAsync($"tests/{test.Slug}/", new
        {
            title = "Hijacked Title"
        });

        // Assert
        response.StatusCode.Should().BeOneOf(
            new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.NotFound },
            "another user must not be able to patch someone else's test");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // PATCH /tests/{slug}/questions/{id}/ — Partial question updates
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task PatchQuestion_TextOnly_UpdatesTextPreservesAnswers()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"Patch_Q_{Guid.NewGuid().ToString("N")[..8]}");
        var question = await TestDataHelper.AddQuestionAsync(_apiClient, test.Slug, "Original question text");

        // Act — patch only question text, omit answers
        var response = await _apiClient.PatchAsync(
            $"tests/{test.Slug}/questions/{question.Id}/", new
            {
                question_text = "Patched question text"
            });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "PATCH with only question text should succeed");

        var updated = await _apiClient.DeserializeResponseAsync<QuestionResponse>(response);
        updated!.QuestionText.Should().Be("Patched question text", "question text should be updated");
        updated.Answers.Should().HaveCount(question.Answers.Count,
            "answer count should be unchanged when answers are not included in patch");
    }

    [Fact]
    public async Task PatchQuestion_ByNonOwner_DeniesAccess()
    {
        // Arrange — user A creates test and question
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"Patch_QOwn_{Guid.NewGuid().ToString("N")[..8]}");
        var question = await TestDataHelper.AddQuestionAsync(_apiClient, test.Slug, "Secure question");

        // User B tries to patch it
        using var userBClient = new ApiClient(TestConfiguration.GetBaseUrl());
        await TestDataHelper.RegisterAndLoginAsync(userBClient);

        // Act
        var response = await userBClient.PatchAsync(
            $"tests/{test.Slug}/questions/{question.Id}/", new
            {
                question_text = "Tampered question"
            });

        // Assert
        response.StatusCode.Should().BeOneOf(
            new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.NotFound },
            "another user must not be able to patch someone else's question");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // PATCH /companies/{id}/ — Company updates
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task PatchCompany_Name_UpdatesNameSuccessfully()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var createResp = await _apiClient.PostAsync("companies/", new CreateCompanyRequest
        {
            Name = $"OriginalCo_{Guid.NewGuid().ToString("N")[..6]}"
        });
        var company = await _apiClient.DeserializeResponseAsync<CompanyResponse>(createResp);

        // Act
        var response = await _apiClient.PatchAsync($"companies/{company!.Id}/",
            new UpdateCompanyRequest { Name = "PatchedCompanyName" });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "PATCH company name should succeed for admin");

        var updated = await _apiClient.DeserializeResponseAsync<CompanyResponse>(response);
        updated!.Name.Should().Be("PatchedCompanyName", "company name should be updated");
    }

    [Fact]
    public async Task PatchCompany_ByNonMember_DeniesAccess()
    {
        // Arrange — user A creates a company
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var createResp = await _apiClient.PostAsync("companies/", new CreateCompanyRequest
        {
            Name = $"PrivateCo_{Guid.NewGuid().ToString("N")[..6]}"
        });
        var company = await _apiClient.DeserializeResponseAsync<CompanyResponse>(createResp);

        // User B (not a member) tries to patch it
        using var userBClient = new ApiClient(TestConfiguration.GetBaseUrl());
        await TestDataHelper.RegisterAndLoginAsync(userBClient);

        // Act
        var response = await userBClient.PatchAsync($"companies/{company!.Id}/",
            new UpdateCompanyRequest { Name = "Hijacked" });

        // Assert
        response.StatusCode.Should().BeOneOf(
            new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.NotFound },
            "non-member must not be able to patch a company");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // PATCH /companies/{id}/folders/{folderId}/ — Folder updates
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task PatchFolder_Name_UpdatesNameSuccessfully()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var companyResp = await _apiClient.PostAsync("companies/", new CreateCompanyRequest
        {
            Name = $"FolderCo_{Guid.NewGuid().ToString("N")[..6]}"
        });
        var company = await _apiClient.DeserializeResponseAsync<CompanyResponse>(companyResp);

        var folderResp = await _apiClient.PostAsync($"companies/{company!.Id}/folders/",
            new CreateFolderRequest { Name = "Original Folder" });
        var folder = await _apiClient.DeserializeResponseAsync<FolderResponse>(folderResp);

        // Act
        var response = await _apiClient.PatchAsync(
            $"companies/{company.Id}/folders/{folder!.Id}/",
            new UpdateFolderRequest { Name = "Renamed Folder" });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "PATCH folder name should succeed for company admin");

        var updated = await _apiClient.DeserializeResponseAsync<FolderResponse>(response);
        updated!.Name.Should().Be("Renamed Folder", "folder name should be updated");
    }

    [Fact]
    public async Task PatchFolder_MoveToParent_UpdatesParentSuccessfully()
    {
        // Arrange — create two folders, then nest one inside the other
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var companyResp = await _apiClient.PostAsync("companies/", new CreateCompanyRequest
        {
            Name = $"NestCo_{Guid.NewGuid().ToString("N")[..6]}"
        });
        var company = await _apiClient.DeserializeResponseAsync<CompanyResponse>(companyResp);

        var parentResp = await _apiClient.PostAsync($"companies/{company!.Id}/folders/",
            new CreateFolderRequest { Name = "Parent Folder" });
        var parent = await _apiClient.DeserializeResponseAsync<FolderResponse>(parentResp);

        var childResp = await _apiClient.PostAsync($"companies/{company.Id}/folders/",
            new CreateFolderRequest { Name = "Child Folder" });
        var child = await _apiClient.DeserializeResponseAsync<FolderResponse>(childResp);

        // Act — move child under parent
        var response = await _apiClient.PatchAsync(
            $"companies/{company.Id}/folders/{child!.Id}/",
            new UpdateFolderRequest { Name = child.Name, Parent = parent!.Id });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "PATCH folder to set parent should succeed");

        var updated = await _apiClient.DeserializeResponseAsync<FolderResponse>(response);
        updated!.Parent.Should().Be(parent.Id, "folder should now be nested under the parent");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _apiClient?.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
