using System.Net;
using FluentAssertions;
using TestIT.ApiTests.Helpers;
using TestIT.ApiTests.Models;
using Xunit;

namespace TestIT.ApiTests.Tests;

public class CompanyTests : IDisposable
{
    private readonly ApiClient _apiClient;

    public CompanyTests(ITestOutputHelper output)
    {
        ApiClient.SetOutput(output.WriteLine);
        _apiClient = new ApiClient(TestConfiguration.GetBaseUrl());
    }

    private async Task SetupAuth()
    {
        await TestDataHelper.RegisterAndLoginAsync(_apiClient);
    }

    /// <summary>
    /// Creates a company and returns the full response object including ID.
    /// API now returns complete object in create response (fixed in v1.8).
    /// </summary>
    private async Task<CompanyResponse> CreateCompanyAsync(string name)
    {
        var response = await _apiClient.PostAsync("companies/", new CreateCompanyRequest { Name = name });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await _apiClient.DeserializeResponseAsync<CompanyResponse>(response))!;
    }

    [Fact]
    public async Task CompanyCreation_WithValidData_CreatesCompanySuccessfully()
    {
        await SetupAuth();

        var companyName = $"TestCorp_{Guid.NewGuid().ToString("N")[..8]}";
        var response = await _apiClient.PostAsync("companies/",
            new CreateCompanyRequest { Name = companyName });
        var body = await _apiClient.GetResponseBodyAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.Created, $"Response: {body}");

        // API now returns full object including ID in create response
        var company = await _apiClient.DeserializeResponseAsync<CompanyResponse>(response);
        company.Should().NotBeNull();
        company!.Name.Should().Be(companyName);
        company.Id.Should().BePositive("API should return ID in create response");
    }

    [Fact]
    public async Task CompanyCreation_WhenUnauthenticated_DeniesAccess()
    {
        _apiClient.ClearAuthToken();

        var response = await _apiClient.PostAsync("companies/",
            new CreateCompanyRequest { Name = "Unauthorized Corp" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "because authentication is required");
    }

    [Fact]
    public async Task CompanyListing_AfterCreation_IncludesNewCompany()
    {
        await SetupAuth();

        var companyName = $"ListCorp_{Guid.NewGuid().ToString("N")[..8]}";
        await _apiClient.PostAsync("companies/", new CreateCompanyRequest { Name = companyName });

        var response = await _apiClient.GetAsync("companies/");
        var body = await _apiClient.GetResponseBodyAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {body}");

        var companies = await _apiClient.DeserializeResponseAsync<List<CompanyResponse>>(response);
        companies.Should().NotBeNull();
        companies.Should().Contain(c => c.Name == companyName);
    }

    [Fact]
    public async Task CompanyDetails_AsAdmin_ReturnsCompanyData()
    {
        await SetupAuth();

        var company = await CreateCompanyAsync($"GetCorp_{Guid.NewGuid().ToString("N")[..8]}");

        var response = await _apiClient.GetAsync($"companies/{company.Id}/");
        var body = await _apiClient.GetResponseBodyAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {body}");

        var retrieved = await _apiClient.DeserializeResponseAsync<CompanyResponse>(response);
        retrieved.Should().NotBeNull();
        retrieved!.Id.Should().Be(company.Id);
    }

    [Fact]
    public async Task CompanyUpdate_WithValidData_UpdatesCompanySuccessfully()
    {
        await SetupAuth();

        var company = await CreateCompanyAsync($"OldName_{Guid.NewGuid().ToString("N")[..8]}");

        var response = await _apiClient.PutAsync($"companies/{company.Id}/",
            new UpdateCompanyRequest { Name = "NewCompanyName" });
        var body = await _apiClient.GetResponseBodyAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {body}");

        var updated = await _apiClient.DeserializeResponseAsync<CompanyResponse>(response);
        updated.Should().NotBeNull();
        updated!.Name.Should().Be("NewCompanyName");
    }

    [Fact]
    public async Task CompanyDeletion_AsAdmin_RemovesCompanyCompletely()
    {
        await SetupAuth();

        var company = await CreateCompanyAsync($"DeleteCorp_{Guid.NewGuid().ToString("N")[..8]}");

        var response = await _apiClient.DeleteAsync($"companies/{company.Id}/");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, "because company deletion should succeed");

        // Verify deleted
        var getResponse = await _apiClient.GetAsync($"companies/{company.Id}/");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound, "because company should be deleted");
    }

    [Fact]
    public async Task MemberListing_NewCompany_CreatorIsOnlyAdmin()
    {
        await SetupAuth();

        var company = await CreateCompanyAsync($"MemberCorp_{Guid.NewGuid().ToString("N")[..8]}");

        var response = await _apiClient.GetAsync($"companies/{company.Id}/members/");
        var body = await _apiClient.GetResponseBodyAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {body}");

        var members = await _apiClient.DeserializeResponseAsync<List<MemberResponse>>(response);
        members.Should().NotBeNull();
        members.Should().HaveCount(1, "because only the creator should be a member initially");
        members![0].Role.Should().Be("admin", "because the creator becomes admin automatically");
    }

    [Fact]
    public async Task CompanyTestListing_AsAdmin_ReturnsCompanyTests()
    {
        await SetupAuth();

        var company = await CreateCompanyAsync($"TestListCorp_{Guid.NewGuid().ToString("N")[..8]}");

        // Create a company test so the listing has something to return
        var testResp = await _apiClient.PostAsync($"tests/company/{company.Id}/",
            new CreateCompanyTestRequest { Title = $"ListingTest_{Guid.NewGuid().ToString("N")[..8]}" });
        testResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdTest = await _apiClient.DeserializeResponseAsync<TestResponse>(testResp);

        var response = await _apiClient.GetAsync($"tests/company/{company.Id}/");
        var body = await _apiClient.GetResponseBodyAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {body}");

        var tests = await _apiClient.DeserializeResponseAsync<List<TestResponse>>(response);
        tests.Should().NotBeNull();
        tests.Should().Contain(t => t.Slug == createdTest!.Slug,
            "because the test just created should appear in the company test listing");
    }

    [Fact]
    public async Task CompanyTestCreation_WithValidData_CreatesTestInCompany()
    {
        await SetupAuth();

        var company = await CreateCompanyAsync($"CompTestCorp_{Guid.NewGuid().ToString("N")[..8]}");

        var response = await _apiClient.PostAsync($"tests/company/{company.Id}/", new CreateCompanyTestRequest
        {
            Title = "Company Quiz",
            Description = "Internal company test"
        });
        var body = await _apiClient.GetResponseBodyAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.Created, $"Response: {body}");

        var test = await _apiClient.DeserializeResponseAsync<TestResponse>(response);
        test.Should().NotBeNull();
        test!.Title.Should().Be("Company Quiz");
    }

    [Fact]
    public async Task CompanyTestCreation_AsStudent_DeniesAccess()
    {
        // Arrange: admin creates company, invites user B as student, user B accepts
        await SetupAuth();

        var company = await CreateCompanyAsync($"RoleCorp_{Guid.NewGuid().ToString("N")[..8]}");

        // Register a second user without disturbing the admin token on _apiClient
        var (studentEmail, studentPassword) = await TestDataHelper.RegisterUserAsync(_apiClient);

        // Admin invites the student
        var inviteResp = await _apiClient.PostAsync($"companies/{company.Id}/invites/",
            new CreateInviteRequest { Email = studentEmail, Role = "student" });
        inviteResp.StatusCode.Should().Be(HttpStatusCode.Created,
            "admin should be able to send an invite");

        var invite = await _apiClient.DeserializeResponseAsync<InviteResponse>(inviteResp);

        // Student logs in and accepts the invite
        using var studentClient = new ApiClient(TestConfiguration.GetBaseUrl());
        var loginResp = await studentClient.PostAsync<LoginRequest, LoginResponse>(
            "auth/login/",
            new LoginRequest { Email = studentEmail, Password = studentPassword });
        studentClient.SetAuthToken(loginResp!.AccessToken);

        var acceptResp = await studentClient.PostAsync(
            $"invites/{invite!.Token}/accept/",
            new Dictionary<string, object>());
        acceptResp.StatusCode.Should().Be(HttpStatusCode.OK,
            "student should be able to accept the invite");

        // Act: student attempts to create a company test
        var response = await studentClient.PostAsync($"tests/company/{company.Id}/",
            new CreateCompanyTestRequest { Title = "Student Attempt" });
        var body = await studentClient.GetResponseBodyAsync(response);

        // Assert: students are not authorised to create company tests
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            $"because students must not be allowed to create company tests. Response: {body}");
    }

    public void Dispose()
    {
        _apiClient?.Dispose();
    }
}
