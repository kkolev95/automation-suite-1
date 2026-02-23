using System.Net;
using FluentAssertions;
using TestIT.ApiTests.Helpers;
using TestIT.ApiTests.Models;
using Xunit.Abstractions;

namespace TestIT.ApiTests.Tests;

/// <summary>
/// Tests covering previously untested endpoints:
/// GET /tests/public/, GET /tests/{slug}/results/{id}/,
/// GET /tests/{slug}/questions/{id}/, PUT/DELETE /companies/{id}/members/{userId}/,
/// and GET/PATCH/DELETE for company-scoped test detail.
/// </summary>
public class CoverageTests : IDisposable
{
    private readonly ApiClient _apiClient;
    private bool _disposed;

    public CoverageTests(ITestOutputHelper output)
    {
        ApiClient.SetOutput(output.WriteLine);
        _apiClient = new ApiClient(TestConfiguration.GetBaseUrl());
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // GET /tests/public/ — Public test discovery
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetPublicTests_Unauthenticated_Returns200()
    {
        using var anonClient = new ApiClient(TestConfiguration.GetBaseUrl());

        var response = await anonClient.GetAsync("tests/public/");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "public test list must be accessible without authentication");
    }

    [Fact]
    public async Task GetPublicTests_PublicTest_AppearsInList()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"Public_{Guid.NewGuid().ToString("N")[..8]}",
            visibility: "public");

        // Act
        using var anonClient = new ApiClient(TestConfiguration.GetBaseUrl());
        var publicTests = await anonClient.GetAsync<List<TestResponse>>("tests/public/");

        // Assert — deserialise and check by slug property, not raw string
        publicTests.Should().NotBeNull("public endpoint must return a JSON list");
        publicTests.Should().Contain(t => t.Slug == test.Slug,
            "public test slug should appear in the public discovery list");
    }

    [Fact]
    public async Task GetPublicTests_LinkOnlyTest_DoesNotAppearInList()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"LinkOnly_{Guid.NewGuid().ToString("N")[..8]}",
            visibility: "link_only");

        // Act
        using var anonClient = new ApiClient(TestConfiguration.GetBaseUrl());
        var tests = await anonClient.GetAsync<List<TestResponse>>("tests/public/");

        // Assert — must not silently skip when tests is null
        tests.Should().NotBeNull("public endpoint must return a JSON list");
        tests.Should().NotContain(t => t.Slug == test.Slug,
            "link_only test must not appear in the public discovery list");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // GET /tests/{slug}/results/{id}/ — Individual attempt detail
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetResultDetail_ByAuthor_ReturnsDetailedBreakdown()
    {
        // Arrange — create test, submit an attempt, then fetch the individual result
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"ResDetail_{Guid.NewGuid().ToString("N")[..8]}",
            maxAttempts: 2,
            showAnswersAfter: true);
        await TestDataHelper.AddQuestionAsync(_apiClient, test.Slug, "Detail question?");

        using var takerClient = new ApiClient(TestConfiguration.GetBaseUrl());
        var startResp = await takerClient.PostAsync($"tests/{test.Slug}/attempts/",
            new StartAttemptRequest { AnonymousName = "DetailTaker" });
        var attempt = await takerClient.DeserializeResponseAsync<AttemptResponse>(startResp);

        await takerClient.PostAsync($"tests/{test.Slug}/attempts/{attempt!.Id}/submit/", new { });

        // Author fetches results list to get the result ID
        var results = await _apiClient.GetAsync<List<ResultResponse>>($"tests/{test.Slug}/results/");
        results.Should().NotBeNullOrEmpty("there should be at least one submitted result");
        var resultId = results![0].Id;

        // Act
        var response = await _apiClient.GetAsync($"tests/{test.Slug}/results/{resultId}/");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "author should be able to retrieve individual result detail");
        var detail = await _apiClient.DeserializeResponseAsync<DetailedResultResponse>(response);
        detail.Should().NotBeNull();
        detail!.Id.Should().Be(resultId);
    }

    [Fact]
    public async Task GetResultDetail_ByNonAuthor_DeniesAccess()
    {
        // Arrange — author creates test, someone submits, a third party tries to read the detail
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"ResDetailAuth_{Guid.NewGuid().ToString("N")[..8]}",
            maxAttempts: 2);
        await TestDataHelper.AddQuestionAsync(_apiClient, test.Slug, "Auth check question");

        using var takerClient = new ApiClient(TestConfiguration.GetBaseUrl());
        var startResp = await takerClient.PostAsync($"tests/{test.Slug}/attempts/",
            new StartAttemptRequest { AnonymousName = "Taker" });
        var attempt = await takerClient.DeserializeResponseAsync<AttemptResponse>(startResp);
        await takerClient.PostAsync($"tests/{test.Slug}/attempts/{attempt!.Id}/submit/", new { });

        var results = await _apiClient.GetAsync<List<ResultResponse>>($"tests/{test.Slug}/results/");
        results.Should().NotBeNullOrEmpty("author should see the submitted result");
        var resultId = results![0].Id;

        // User B (authenticated, not the test owner)
        using var userBClient = new ApiClient(TestConfiguration.GetBaseUrl());
        await TestDataHelper.RegisterAndLoginAsync(userBClient);

        // Act
        var response = await userBClient.GetAsync($"tests/{test.Slug}/results/{resultId}/");

        // Assert
        response.StatusCode.Should().BeOneOf(
            new[] { HttpStatusCode.Forbidden, HttpStatusCode.NotFound },
            "non-author must not access someone else's test result detail");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // GET /tests/{slug}/questions/{id}/ — Single question fetch
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetQuestion_WithValidId_ReturnsQuestionData()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"GetQ_{Guid.NewGuid().ToString("N")[..8]}");
        var question = await TestDataHelper.AddQuestionAsync(_apiClient, test.Slug, "Fetchable question");

        // Act
        var response = await _apiClient.GetAsync($"tests/{test.Slug}/questions/{question.Id}/");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "fetching an existing question by ID should succeed");
        var fetched = await _apiClient.DeserializeResponseAsync<QuestionResponse>(response);
        fetched.Should().NotBeNull();
        fetched!.Id.Should().Be(question.Id);
        fetched.QuestionText.Should().Be("Fetchable question");
        fetched.Answers.Should().HaveCount(3, "AddQuestionAsync creates 3 answers by default");
    }

    [Fact]
    public async Task GetQuestion_WithNonExistentId_ReturnsNotFound()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var test = await TestDataHelper.CreateTestAsync(_apiClient,
            $"GetQ404_{Guid.NewGuid().ToString("N")[..8]}");

        // Act
        var response = await _apiClient.GetAsync($"tests/{test.Slug}/questions/99999/");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "fetching a non-existent question should return 404");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // PUT /companies/{id}/members/{userId}/ — Member role update
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task UpdateMemberRole_AsAdmin_ChangesRoleSuccessfully()
    {
        // Arrange — create company, invite user B as student, then promote to instructor
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var companyResp = await _apiClient.PostAsync("companies/",
            new CreateCompanyRequest { Name = $"RoleCo_{Guid.NewGuid().ToString("N")[..6]}" });
        var company = await _apiClient.DeserializeResponseAsync<CompanyResponse>(companyResp);

        using var userBClient = new ApiClient(TestConfiguration.GetBaseUrl());
        var (emailB, passB) = await TestDataHelper.RegisterUserAsync(userBClient);

        var invResp = await _apiClient.PostAsync($"companies/{company!.Id}/invites/",
            new CreateInviteRequest { Email = emailB, Role = "student" });
        var invite = await _apiClient.DeserializeResponseAsync<InviteResponse>(invResp);

        var loginB = await userBClient.PostAsync<LoginRequest, LoginResponse>("auth/login/",
            new LoginRequest { Email = emailB, Password = passB });
        userBClient.SetAuthToken(loginB!.AccessToken);
        await userBClient.PostAsync($"invites/{invite!.Token}/accept/", new { });

        var members = await _apiClient.GetAsync<List<MemberResponse>>($"companies/{company.Id}/members/");
        var memberB = members!.First(m => m.Email == emailB);

        // Act — promote user B to instructor
        var response = await _apiClient.PutAsync(
            $"companies/{company.Id}/members/{memberB.UserId}/",
            new UpdateMemberRoleRequest { Role = "instructor" });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "admin should be able to update a member's role");
        var updated = await _apiClient.DeserializeResponseAsync<MemberResponse>(response);
        updated!.Role.Should().Be("instructor", "role should be updated to instructor");
    }

    [Fact]
    public async Task UpdateMemberRole_ByNonAdmin_DeniesAccess()
    {
        // Arrange — student user B tries to change student user C's role
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var companyResp = await _apiClient.PostAsync("companies/",
            new CreateCompanyRequest { Name = $"RoleGuard_{Guid.NewGuid().ToString("N")[..6]}" });
        var company = await _apiClient.DeserializeResponseAsync<CompanyResponse>(companyResp);

        // Invite and join user B
        using var userBClient = new ApiClient(TestConfiguration.GetBaseUrl());
        var (emailB, passB) = await TestDataHelper.RegisterUserAsync(userBClient);
        var invB = await _apiClient.PostAsync($"companies/{company!.Id}/invites/",
            new CreateInviteRequest { Email = emailB, Role = "student" });
        var invBData = await _apiClient.DeserializeResponseAsync<InviteResponse>(invB);
        var loginB = await userBClient.PostAsync<LoginRequest, LoginResponse>("auth/login/",
            new LoginRequest { Email = emailB, Password = passB });
        userBClient.SetAuthToken(loginB!.AccessToken);
        await userBClient.PostAsync($"invites/{invBData!.Token}/accept/", new { });

        // Invite and join user C
        using var userCClient = new ApiClient(TestConfiguration.GetBaseUrl());
        var (emailC, passC) = await TestDataHelper.RegisterUserAsync(userCClient);
        var invC = await _apiClient.PostAsync($"companies/{company.Id}/invites/",
            new CreateInviteRequest { Email = emailC, Role = "student" });
        var invCData = await _apiClient.DeserializeResponseAsync<InviteResponse>(invC);
        var loginC = await userCClient.PostAsync<LoginRequest, LoginResponse>("auth/login/",
            new LoginRequest { Email = emailC, Password = passC });
        userCClient.SetAuthToken(loginC!.AccessToken);
        await userCClient.PostAsync($"invites/{invCData!.Token}/accept/", new { });

        var members = await _apiClient.GetAsync<List<MemberResponse>>($"companies/{company.Id}/members/");
        var memberC = members!.First(m => m.Email == emailC);

        // Act — user B (student) tries to promote user C
        var response = await userBClient.PutAsync(
            $"companies/{company.Id}/members/{memberC.UserId}/",
            new UpdateMemberRoleRequest { Role = "admin" });

        // Assert
        response.StatusCode.Should().BeOneOf(
            new[] { HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized },
            "non-admin member must not be able to change another member's role");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // DELETE /companies/{id}/members/{userId}/ — Member removal
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RemoveMember_AsAdmin_RemovesMemberSuccessfully()
    {
        // Arrange — invite user B, accept, then admin removes them
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var companyResp = await _apiClient.PostAsync("companies/",
            new CreateCompanyRequest { Name = $"RemoveCo_{Guid.NewGuid().ToString("N")[..6]}" });
        var company = await _apiClient.DeserializeResponseAsync<CompanyResponse>(companyResp);

        using var userBClient = new ApiClient(TestConfiguration.GetBaseUrl());
        var (emailB, passB) = await TestDataHelper.RegisterUserAsync(userBClient);
        var invResp = await _apiClient.PostAsync($"companies/{company!.Id}/invites/",
            new CreateInviteRequest { Email = emailB, Role = "student" });
        var inv = await _apiClient.DeserializeResponseAsync<InviteResponse>(invResp);
        var loginB = await userBClient.PostAsync<LoginRequest, LoginResponse>("auth/login/",
            new LoginRequest { Email = emailB, Password = passB });
        userBClient.SetAuthToken(loginB!.AccessToken);
        await userBClient.PostAsync($"invites/{inv!.Token}/accept/", new { });

        var members = await _apiClient.GetAsync<List<MemberResponse>>($"companies/{company.Id}/members/");
        var memberB = members!.First(m => m.Email == emailB);

        // Act
        var response = await _apiClient.DeleteAsync($"companies/{company.Id}/members/{memberB.UserId}/");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent,
            "admin should be able to remove a member from the company");
        var updatedMembers = await _apiClient.GetAsync<List<MemberResponse>>($"companies/{company.Id}/members/");
        updatedMembers.Should().NotContain(m => m.UserId == memberB.UserId,
            "removed member should no longer appear in the member list");
    }

    [Fact]
    public async Task RemoveMember_LastAdmin_DeniesRemoval()
    {
        // Arrange — only the admin exists; attempt self-removal
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var companyResp = await _apiClient.PostAsync("companies/",
            new CreateCompanyRequest { Name = $"LastAdmin_{Guid.NewGuid().ToString("N")[..6]}" });
        var company = await _apiClient.DeserializeResponseAsync<CompanyResponse>(companyResp);
        var me = await _apiClient.GetAsync<UserResponse>("auth/me/");

        // Act — admin tries to remove themselves (the only admin)
        var response = await _apiClient.DeleteAsync($"companies/{company!.Id}/members/{me!.Id}/");

        // Assert
        response.StatusCode.Should().BeOneOf(
            new[] { HttpStatusCode.BadRequest, HttpStatusCode.Forbidden },
            "removing the last admin should be rejected to prevent orphaned companies");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // GET /tests/company/{id}/{slug}/ — Company test detail
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetCompanyTestDetail_AsMember_ReturnsTestData()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var companyResp = await _apiClient.PostAsync("companies/",
            new CreateCompanyRequest { Name = $"DetailCo_{Guid.NewGuid().ToString("N")[..6]}" });
        var company = await _apiClient.DeserializeResponseAsync<CompanyResponse>(companyResp);

        var testResp = await _apiClient.PostAsync($"tests/company/{company!.Id}/",
            new CreateCompanyTestRequest { Title = $"CompDetail_{Guid.NewGuid().ToString("N")[..6]}" });
        var test = await _apiClient.DeserializeResponseAsync<TestResponse>(testResp);

        // Act
        var response = await _apiClient.GetAsync($"tests/company/{company.Id}/{test!.Slug}/");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "company member should be able to fetch company test detail");
        var detail = await _apiClient.DeserializeResponseAsync<TestResponse>(response);
        detail.Should().NotBeNull();
        detail!.Slug.Should().Be(test.Slug);
    }

    [Fact]
    public async Task GetCompanyTestDetail_AsNonMember_DeniesAccess()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var companyResp = await _apiClient.PostAsync("companies/",
            new CreateCompanyRequest { Name = $"DetailGuard_{Guid.NewGuid().ToString("N")[..6]}" });
        var company = await _apiClient.DeserializeResponseAsync<CompanyResponse>(companyResp);

        var testResp = await _apiClient.PostAsync($"tests/company/{company!.Id}/",
            new CreateCompanyTestRequest { Title = $"GuardTest_{Guid.NewGuid().ToString("N")[..6]}" });
        var test = await _apiClient.DeserializeResponseAsync<TestResponse>(testResp);

        using var userBClient = new ApiClient(TestConfiguration.GetBaseUrl());
        await TestDataHelper.RegisterAndLoginAsync(userBClient);

        // Act
        var response = await userBClient.GetAsync($"tests/company/{company.Id}/{test!.Slug}/");

        // Assert
        response.StatusCode.Should().BeOneOf(
            new[] { HttpStatusCode.Forbidden, HttpStatusCode.NotFound },
            "non-member must not access company test detail");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // PATCH /tests/company/{id}/{slug}/ — Company test partial update
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task PatchCompanyTest_AsAdmin_UpdatesTestSuccessfully()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var companyResp = await _apiClient.PostAsync("companies/",
            new CreateCompanyRequest { Name = $"PatchCo_{Guid.NewGuid().ToString("N")[..6]}" });
        var company = await _apiClient.DeserializeResponseAsync<CompanyResponse>(companyResp);

        var testResp = await _apiClient.PostAsync($"tests/company/{company!.Id}/",
            new CreateCompanyTestRequest { Title = $"OrigTitle_{Guid.NewGuid().ToString("N")[..6]}" });
        var test = await _apiClient.DeserializeResponseAsync<TestResponse>(testResp);

        // Act
        var response = await _apiClient.PatchAsync($"tests/company/{company.Id}/{test!.Slug}/", new
        {
            title = "UpdatedCompanyTitle"
        });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "admin should be able to patch a company test");
        var updated = await _apiClient.DeserializeResponseAsync<TestResponse>(response);
        updated!.Title.Should().Be("UpdatedCompanyTitle", "title should reflect the patch");
    }

    [Fact]
    public async Task PatchCompanyTest_AsNonMember_DeniesAccess()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var companyResp = await _apiClient.PostAsync("companies/",
            new CreateCompanyRequest { Name = $"PatchGuard_{Guid.NewGuid().ToString("N")[..6]}" });
        var company = await _apiClient.DeserializeResponseAsync<CompanyResponse>(companyResp);

        var testResp = await _apiClient.PostAsync($"tests/company/{company!.Id}/",
            new CreateCompanyTestRequest { Title = $"GuardPatch_{Guid.NewGuid().ToString("N")[..6]}" });
        var test = await _apiClient.DeserializeResponseAsync<TestResponse>(testResp);

        using var userBClient = new ApiClient(TestConfiguration.GetBaseUrl());
        await TestDataHelper.RegisterAndLoginAsync(userBClient);

        // Act
        var response = await userBClient.PatchAsync($"tests/company/{company.Id}/{test!.Slug}/", new
        {
            title = "Hijacked"
        });

        // Assert
        response.StatusCode.Should().BeOneOf(
            new[] { HttpStatusCode.Forbidden, HttpStatusCode.NotFound },
            "non-member must not patch a company test");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // DELETE /tests/company/{id}/{slug}/ — Company test deletion
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task DeleteCompanyTest_AsAdmin_RemovesTestSuccessfully()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var companyResp = await _apiClient.PostAsync("companies/",
            new CreateCompanyRequest { Name = $"DelCo_{Guid.NewGuid().ToString("N")[..6]}" });
        var company = await _apiClient.DeserializeResponseAsync<CompanyResponse>(companyResp);

        var testResp = await _apiClient.PostAsync($"tests/company/{company!.Id}/",
            new CreateCompanyTestRequest { Title = $"DeleteMe_{Guid.NewGuid().ToString("N")[..6]}" });
        var test = await _apiClient.DeserializeResponseAsync<TestResponse>(testResp);

        // Act
        var response = await _apiClient.DeleteAsync($"tests/company/{company.Id}/{test!.Slug}/");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent,
            "admin should be able to delete a company test");

        var getResp = await _apiClient.GetAsync($"tests/company/{company.Id}/{test.Slug}/");
        getResp.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "deleted company test should no longer be accessible");
    }

    [Fact]
    public async Task DeleteCompanyTest_AsNonMember_DeniesAccess()
    {
        // Arrange
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
        var companyResp = await _apiClient.PostAsync("companies/",
            new CreateCompanyRequest { Name = $"DelGuard_{Guid.NewGuid().ToString("N")[..6]}" });
        var company = await _apiClient.DeserializeResponseAsync<CompanyResponse>(companyResp);

        var testResp = await _apiClient.PostAsync($"tests/company/{company!.Id}/",
            new CreateCompanyTestRequest { Title = $"GuardDel_{Guid.NewGuid().ToString("N")[..6]}" });
        var test = await _apiClient.DeserializeResponseAsync<TestResponse>(testResp);

        using var userBClient = new ApiClient(TestConfiguration.GetBaseUrl());
        await TestDataHelper.RegisterAndLoginAsync(userBClient);

        // Act
        var response = await userBClient.DeleteAsync($"tests/company/{company.Id}/{test!.Slug}/");

        // Assert
        response.StatusCode.Should().BeOneOf(
            new[] { HttpStatusCode.Forbidden, HttpStatusCode.NotFound },
            "non-member must not delete a company test");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _apiClient?.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
