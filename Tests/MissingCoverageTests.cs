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
///   - ShowAnswersAfter=true: submit response includes score field (BUG: API omits score)
///   - Invite edge cases: invalid token, wrong user (400), instructor role
///   - Visibility change propagation to/from the public listing
///   - Company folder access control for non-members (resource-hiding: 404)
///   - Result detail access control for unauthenticated users
///   - Anonymous attempt with empty name (BUG: API accepts empty name with 201)
///
/// Known failing tests (documented API bugs/missing features):
///   - ShowAnswersAfter_WhenTrue_SubmitResponseIncludesScore
///   - TestVisibility_WhenChangedFromPublicToPrivate_DisappearsFromPublicListing
///   - AnonymousAttempt_EmptyAnonymousName_BehavesGracefully
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

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "an invalid refresh token must return 401 Unauthorized — 400 would incorrectly suggest a malformed request rather than an authentication failure");
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

    /// <summary>
    /// BUG: When show_answers_after=true, submitting an attempt returns HTTP 200 but
    /// the response body contains no "score" field. A taker who just finished the test
    /// has no way to see their result immediately from the submit response alone.
    /// Score must be fetched separately via GET /tests/{slug}/results/ (author-only).
    /// Expected: submit response includes "score" when show_answers_after=true.
    /// </summary>
    [Fact]
    [Trait("Category", "Functional")]
    [Trait("Priority", "P1")]
    public async Task ShowAnswersAfter_WhenTrue_SubmitResponseIncludesScore()
    {
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"ShowAfter_{Guid.NewGuid().ToString("N")[..8]}",
            showAnswersAfter: true);
        var question = await TestDataHelper.AddQuestionAsync(_apiClient, test.Slug, "Capital of France?");
        var correctId = question.Answers.First(a => a.IsCorrect).Id;

        var startResp = await _anonClient.PostAsync($"tests/{test.Slug}/attempts/",
            new StartAttemptRequest { AnonymousName = "ShowAfter Tester" });
        startResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var attempt = await _anonClient.DeserializeResponseAsync<AttemptResponse>(startResp);

        var takeTest = await _anonClient.GetAsync<TakeTestResponse>($"tests/{test.Slug}/take/");
        await _anonClient.PutAsync($"tests/{test.Slug}/attempts/{attempt!.Id}/",
            new SaveDraftRequest
            {
                DraftAnswers = new Dictionary<string, List<int>>
                {
                    { takeTest!.Questions[0].Id.ToString(), new List<int> { correctId } }
                }
            });

        var submitResp = await _anonClient.PostAsync(
            $"tests/{test.Slug}/attempts/{attempt.Id}/submit/",
            new Dictionary<string, object>());
        submitResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await submitResp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.TryGetProperty("score", out _).Should().BeTrue(
            "when show_answers_after=true the submit response must include a 'score' field " +
            "so the taker can see their result immediately");
    }

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

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "accepting a non-existent invite token must return 404 Not Found — the token does not exist in the system");
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

        // Assert: the invite is addressed to userA's email — userB gets 400 with
        // "Invite is for a different email address." (API chooses 400 over 403 here).
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            $"accepting an invite addressed to a different email must return 400. Response: {body}");
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

    /// <summary>
    /// BUG/MISSING FEATURE: PATCH /tests/{slug}/ with visibility="private" returns 400.
    /// The API does not support a "private" visibility state that would hide a test from
    /// the author's own listing without making it entirely inaccessible. Only "public",
    /// "link_only", and "password_protected" are accepted.
    /// Expected: "private" is a valid visibility option allowing the author to archive/hide a test.
    /// </summary>
    [Fact]
    [Trait("Category", "Functional")]
    [Trait("Priority", "P2")]
    public async Task TestVisibility_WhenChangedFromPublicToPrivate_DisappearsFromPublicListing()
    {
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"VisPriv_{Guid.NewGuid().ToString("N")[..8]}",
            visibility: "public");

        var beforeJson = await (await _anonClient.GetAsync("tests/public/")).Content.ReadAsStringAsync();
        JsonDocument.Parse(beforeJson).RootElement.EnumerateArray()
            .Any(t => t.TryGetProperty("slug", out var s) && s.GetString() == test.Slug)
            .Should().BeTrue("a public test must appear in the public listing");

        // This PATCH currently returns 400 — "private" is not an accepted visibility value
        var updateResp = await _apiClient.PatchAsync($"tests/{test.Slug}/",
            new { visibility = "private" });
        updateResp.StatusCode.Should().Be(HttpStatusCode.OK,
            "visibility change to 'private' must succeed — this is a missing API feature");

        var afterJson = await (await _anonClient.GetAsync("tests/public/")).Content.ReadAsStringAsync();
        JsonDocument.Parse(afterJson).RootElement.EnumerateArray()
            .Any(t => t.TryGetProperty("slug", out var s) && s.GetString() == test.Slug)
            .Should().BeFalse("after setting visibility to private the test must leave the public listing");
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

        // Assert: API uses resource-hiding — non-members receive 404 ("No Company matches the given query.")
        // rather than 403, consistent with the rest of the API's access-control pattern.
        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            $"non-member must receive 404 (resource-hiding pattern) when accessing another company's folders. Response: {body}");
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

        // Assert: unauthenticated access must return 401 Unauthorized.
        // 404 would hide the resource entirely instead of requiring authentication.
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "an unauthenticated request to a result detail endpoint must return 401 Unauthorized");
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

        // Assert: empty anonymous_name must be rejected with 400 Bad Request.
        // anonymous_name is a required identifier for the attempt; accepting an empty string
        // would make the attempt untraceable in the results list.
        // If 201 is returned, the API has no validation on this required field — that is a bug.
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            $"empty anonymous_name must be rejected with 400 — it is a required identifier. Response: {body}");
    }

    public void Dispose()
    {
        _apiClient?.Dispose();
        _anonClient?.Dispose();
    }
}
