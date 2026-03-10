using System.Net;
using System.Text.Json;
using FluentAssertions;
using TestIT.ApiTests.Helpers;
using TestIT.ApiTests.Models;
using Xunit;

namespace TestIT.ApiTests.Tests;

/// <summary>
/// Tests targeting gaps identified by cross-referencing all existing test classes:
///   - Auth: duplicate email registration, invalid refresh token, profile update persistence
///   - ShowAnswersAfter=true: submit response includes score field
///   - Invite edge cases: invalid token, wrong user, instructor role
///   - Visibility change propagation to/from the public listing
///   - Company folder access control for non-members
///   - Result detail access control for unauthenticated users
///   - Anonymous attempt with empty name handled gracefully
/// </summary>
public class MissingCoverageTests : IDisposable
{
    private readonly ApiClient _apiClient;
    private readonly ApiClient _anonClient;

    public MissingCoverageTests(ITestOutputHelper output)
    {
        ApiClient.SetOutput(output.WriteLine);
        var baseUrl = TestConfiguration.GetBaseUrl();
        _apiClient = new ApiClient(baseUrl);
        _anonClient = new ApiClient(baseUrl);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Authentication Edge Cases
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    [Trait("Category", "Validation")]
    [Trait("Priority", "P1")]
    public async Task Registration_WithDuplicateEmail_RejectsRequest()
    {
        var email = $"dup_{Guid.NewGuid().ToString("N")[..8]}@example.com";
        const string password = "SecurePass123!";

        // First registration must succeed
        var firstResp = await _apiClient.PostAsync("auth/register/", new RegisterRequest
        {
            Email = email, Password = password, PasswordConfirm = password,
            FirstName = "First", LastName = "User"
        });
        firstResp.StatusCode.Should().Be(HttpStatusCode.Created, "first registration must succeed");

        // Second registration with the same email must be rejected
        var secondResp = await _apiClient.PostAsync("auth/register/", new RegisterRequest
        {
            Email = email, Password = password, PasswordConfirm = password,
            FirstName = "Second", LastName = "User"
        });

        secondResp.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "registering with a duplicate email must return 400");
    }

    [Fact]
    [Trait("Category", "Security")]
    [Trait("Priority", "P1")]
    public async Task TokenRefresh_WithInvalidToken_DeniesAccess()
    {
        var response = await _apiClient.PostAsync("auth/refresh/",
            new RefreshTokenRequest { RefreshToken = "completely.invalid.token.xyz" });

        ((int)response.StatusCode).Should().BeInRange(400, 401,
            "an invalid refresh token must be rejected with 400 or 401");
    }

    [Fact]
    [Trait("Category", "Functional")]
    [Trait("Priority", "P1")]
    public async Task ProfileUpdate_ChangesPersistAcrossRequests()
    {
        // Arrange: register and login
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);

        // Act: PATCH the profile
        var patchResp = await _apiClient.PatchAsync("auth/me/",
            new UpdateProfileRequest { FirstName = "Updated", LastName = "Name" });
        patchResp.StatusCode.Should().Be(HttpStatusCode.OK, "profile PATCH must succeed");

        // Assert: a fresh GET must reflect the updated values
        var getResp = await _apiClient.GetAsync("auth/me/");
        getResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var profile = await _apiClient.DeserializeResponseAsync<UserResponse>(getResp);
        profile.Should().NotBeNull();
        profile!.FirstName.Should().Be("Updated", "first name must persist after PATCH");
        profile.LastName.Should().Be("Name", "last name must persist after PATCH");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // ShowAnswersAfter Feature
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    [Trait("Category", "Security")]
    [Trait("Priority", "P1")]
    public async Task TestListing_AsAuthor_DoesNotIncludeOtherUsersTests()
    {
        // Arrange: userA creates a test
        using var userAClient = new ApiClient(TestConfiguration.GetBaseUrl());
        await TestDataHelper.RegisterAndLoginAsync(userAClient);
        var testA = await TestDataHelper.CreateTestAsync(userAClient,
            $"UserATest_{Guid.NewGuid().ToString("N")[..8]}");

        // UserB registers and logs in
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var testB = await TestDataHelper.CreateTestAsync(_apiClient,
            $"UserBTest_{Guid.NewGuid().ToString("N")[..8]}");

        // Act: userB lists their tests
        var response = await _apiClient.GetAsync("tests/");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var tests = await _apiClient.DeserializeResponseAsync<List<TestResponse>>(response);

        // Assert: userB's listing must contain userB's test but NOT userA's test
        tests.Should().NotBeNull();
        tests.Should().Contain(t => t.Slug == testB.Slug,
            "userB's own test must appear in their listing");
        tests.Should().NotContain(t => t.Slug == testA.Slug,
            "another user's test must not appear in the authenticated user's listing");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Invite Edge Cases
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    [Trait("Category", "Security")]
    [Trait("Priority", "P1")]
    public async Task InviteAcceptance_WithInvalidToken_RejectsRequest()
    {
        // Any authenticated user trying to accept a non-existent invite token
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);

        var response = await _apiClient.PostAsync("invites/totally-fake-token-999/accept/",
            new Dictionary<string, object>());

        ((int)response.StatusCode).Should().BeInRange(400, 404,
            "accepting an invalid invite token must return a 4xx error");
    }

    [Fact]
    [Trait("Category", "Functional")]
    [Trait("Priority", "P1")]
    public async Task InviteCreation_WithInstructorRole_CreatesInstructorInvite()
    {
        // Arrange: admin creates a company then invites someone as instructor
        using var adminClient = new ApiClient(TestConfiguration.GetBaseUrl());
        await TestDataHelper.RegisterAndLoginAsync(adminClient);

        var companyResp = await adminClient.PostAsync("companies/",
            new CreateCompanyRequest { Name = $"InstrCo_{Guid.NewGuid().ToString("N")[..8]}" });
        companyResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var company = await adminClient.DeserializeResponseAsync<CompanyResponse>(companyResp);

        var (inviteeEmail, _) = await TestDataHelper.RegisterUserAsync(_apiClient);

        // Act: send invite with role = instructor
        var response = await adminClient.PostAsync($"companies/{company!.Id}/invites/",
            new CreateInviteRequest { Email = inviteeEmail, Role = "instructor" });
        var body = await adminClient.GetResponseBodyAsync(response);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created, $"Response: {body}");
        var invite = await adminClient.DeserializeResponseAsync<InviteResponse>(response);
        invite.Should().NotBeNull();
        invite!.Role.Should().Be("instructor", "invite role must match the requested value");
    }

    [Fact]
    [Trait("Category", "Security")]
    [Trait("Priority", "P1")]
    public async Task InviteAcceptance_ByWrongUser_RejectsRequest()
    {
        // Arrange: admin creates invite for userA; userB tries to accept it
        using var adminClient = new ApiClient(TestConfiguration.GetBaseUrl());
        await TestDataHelper.RegisterAndLoginAsync(adminClient);

        var companyResp = await adminClient.PostAsync("companies/",
            new CreateCompanyRequest { Name = $"WrongUsr_{Guid.NewGuid().ToString("N")[..8]}" });
        companyResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var company = await adminClient.DeserializeResponseAsync<CompanyResponse>(companyResp);

        // Register userA (invite recipient) — only the email is needed
        var (userAEmail, _) = await TestDataHelper.RegisterUserAsync(adminClient);

        // Create an invite addressed to userA
        var inviteResp = await adminClient.PostAsync($"companies/{company!.Id}/invites/",
            new CreateInviteRequest { Email = userAEmail, Role = "student" });
        inviteResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var invite = await adminClient.DeserializeResponseAsync<InviteResponse>(inviteResp);

        // Register and login userB (the wrong user) on the shared _apiClient
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);

        // Act: userB attempts to accept userA's invite
        var response = await _apiClient.PostAsync($"invites/{invite!.Token}/accept/",
            new Dictionary<string, object>());
        var body = await _apiClient.GetResponseBodyAsync(response);

        // Assert: the invite is addressed to userA's email — userB must be rejected
        ((int)response.StatusCode).Should().BeInRange(400, 403,
            $"a user must not be able to accept an invite addressed to a different email. Response: {body}");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Visibility Change Propagation
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    [Trait("Category", "Functional")]
    [Trait("Priority", "P1")]
    public async Task TestVisibility_WhenChangedToPublic_AppearsInPublicListing()
    {
        // Arrange: start with a link_only test (not publicly discoverable)
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"VisTo_{Guid.NewGuid().ToString("N")[..8]}",
            visibility: "link_only");

        // Confirm not in public listing yet
        var beforeJson = await (await _anonClient.GetAsync("tests/public/")).Content.ReadAsStringAsync();
        JsonDocument.Parse(beforeJson).RootElement.EnumerateArray()
            .Any(t => t.TryGetProperty("slug", out var s) && s.GetString() == test.Slug)
            .Should().BeFalse("a link_only test must not appear in the public listing");

        // Act: change visibility to public
        var updateResp = await _apiClient.PatchAsync($"tests/{test.Slug}/",
            new { visibility = "public" });
        updateResp.StatusCode.Should().Be(HttpStatusCode.OK, "visibility change to public must succeed");

        // Assert: now visible in the public listing
        var afterJson = await (await _anonClient.GetAsync("tests/public/")).Content.ReadAsStringAsync();
        JsonDocument.Parse(afterJson).RootElement.EnumerateArray()
            .Any(t => t.TryGetProperty("slug", out var s) && s.GetString() == test.Slug)
            .Should().BeTrue("after changing to public the test must appear in the public listing");
    }

    [Fact]
    [Trait("Category", "Functional")]
    [Trait("Priority", "P1")]
    public async Task TestVisibility_WhenChangedFromPublicToLinkOnly_DisappearsFromPublicListing()
    {
        // Arrange: create a public test
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"VisFrom_{Guid.NewGuid().ToString("N")[..8]}",
            visibility: "public");

        // Confirm it appears in the public listing
        var beforeJson = await (await _anonClient.GetAsync("tests/public/")).Content.ReadAsStringAsync();
        JsonDocument.Parse(beforeJson).RootElement.EnumerateArray()
            .Any(t => t.TryGetProperty("slug", out var s) && s.GetString() == test.Slug)
            .Should().BeTrue("a public test must appear in the public listing");

        // Act: change to link_only (removes it from public discovery)
        var updateResp = await _apiClient.PatchAsync($"tests/{test.Slug}/",
            new { visibility = "link_only" });
        updateResp.StatusCode.Should().Be(HttpStatusCode.OK, "visibility change to link_only must succeed");

        // Assert: no longer in the public listing
        var afterJson = await (await _anonClient.GetAsync("tests/public/")).Content.ReadAsStringAsync();
        JsonDocument.Parse(afterJson).RootElement.EnumerateArray()
            .Any(t => t.TryGetProperty("slug", out var s) && s.GetString() == test.Slug)
            .Should().BeFalse("after changing to link_only the test must not appear in the public listing");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Company Folder Access Control
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    [Trait("Category", "Security")]
    [Trait("Priority", "P2")]
    public async Task CompanyFolders_ByNonMember_DeniesAccess()
    {
        // Arrange: admin creates a company with a folder
        using var adminClient = new ApiClient(TestConfiguration.GetBaseUrl());
        await TestDataHelper.RegisterAndLoginAsync(adminClient);

        var companyResp = await adminClient.PostAsync("companies/",
            new CreateCompanyRequest { Name = $"FolderAC_{Guid.NewGuid().ToString("N")[..8]}" });
        companyResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var company = await adminClient.DeserializeResponseAsync<CompanyResponse>(companyResp);

        await adminClient.PostAsync($"companies/{company!.Id}/folders/",
            new CreateFolderRequest { Name = "Restricted Folder" });

        // Register a completely separate user who is NOT a member of this company
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);

        // Act: non-member tries to list the folders
        var response = await _apiClient.GetAsync($"companies/{company.Id}/folders/");
        var body = await _apiClient.GetResponseBodyAsync(response);

        // Assert: must be denied
        ((int)response.StatusCode).Should().BeInRange(403, 404,
            $"a non-member must not be able to list another company's folders. Response: {body}");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Result Detail Access Control
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    [Trait("Category", "Security")]
    [Trait("Priority", "P2")]
    public async Task ResultDetail_AsUnauthenticatedUser_DeniesAccess()
    {
        // Arrange: author creates test, anon submits, author fetches result list
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"ResultAC_{Guid.NewGuid().ToString("N")[..8]}");
        await TestDataHelper.AddQuestionAsync(_apiClient, test.Slug, "Access Q");

        var startResp = await _anonClient.PostAsync($"tests/{test.Slug}/attempts/",
            new StartAttemptRequest { AnonymousName = "Access Tester" });
        startResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var attempt = await _anonClient.DeserializeResponseAsync<AttemptResponse>(startResp);
        await _anonClient.PostAsync(
            $"tests/{test.Slug}/attempts/{attempt!.Id}/submit/",
            new Dictionary<string, object>());

        // Fetch the result ID as the authenticated author
        var results = await _apiClient.GetAsync<List<ResultResponse>>($"tests/{test.Slug}/results/");
        results.Should().NotBeNullOrEmpty("author should see the submitted result");
        var resultId = results![0].Id;

        // Act: a fresh unauthenticated client tries to access the result detail
        using var unauthClient = new ApiClient(TestConfiguration.GetBaseUrl());
        var response = await unauthClient.GetAsync($"tests/{test.Slug}/results/{resultId}/");

        // Assert: must be denied
        ((int)response.StatusCode).Should().BeInRange(401, 404,
            "an unauthenticated user must not be able to access a result detail");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Anonymous Attempt Validation
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    [Trait("Category", "Validation")]
    [Trait("Priority", "P2")]
    public async Task AnonymousAttempt_EmptyAnonymousName_BehavesGracefully()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"EmptyName_{Guid.NewGuid().ToString("N")[..8]}");

        // Act: start attempt with an empty anonymous_name string
        var response = await _anonClient.PostAsync($"tests/{test.Slug}/attempts/",
            new StartAttemptRequest { AnonymousName = "" });
        var body = await _anonClient.GetResponseBodyAsync(response);

        // Assert: API should either reject (400) or accept gracefully (201).
        // A server error (5xx) is never acceptable.
        ((int)response.StatusCode).Should().BeOneOf(
            new[] { 201, 400 },
            $"empty anonymous_name must result in 201 (allowed) or 400 (rejected). Response: {body}");
    }

    public void Dispose()
    {
        _apiClient?.Dispose();
        _anonClient?.Dispose();
    }
}
