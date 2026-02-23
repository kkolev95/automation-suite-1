using System.Net;
using FluentAssertions;
using TestIT.ApiTests.Helpers;
using TestIT.ApiTests.Models;
using Xunit;

namespace TestIT.ApiTests.Tests;

public class InviteTests : IDisposable
{
    private readonly ApiClient _adminClient;
    private readonly ApiClient _inviteeClient;

    public InviteTests(ITestOutputHelper output)
    {
        ApiClient.SetOutput(output.WriteLine);
        var baseUrl = TestConfiguration.GetBaseUrl();
        _adminClient = new ApiClient(baseUrl);
        _inviteeClient = new ApiClient(baseUrl);
    }

    /// <summary>
    /// Sets up: admin user with a company, and a registered (but not logged-in) invitee.
    /// API now returns full object including ID in create response (fixed in v1.8).
    /// </summary>
    private async Task<(CompanyResponse company, string inviteeEmail, string inviteePassword)> SetupCompanyAndInviteeAsync()
    {
        await TestDataHelper.RegisterAndLoginAsync(_adminClient);

        var companyName = $"InviteCorp_{Guid.NewGuid().ToString("N")[..8]}";
        var response = await _adminClient.PostAsync("companies/", new CreateCompanyRequest { Name = companyName });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var company = (await _adminClient.DeserializeResponseAsync<CompanyResponse>(response))!;

        var (inviteeEmail, inviteePassword) = await TestDataHelper.RegisterUserAsync(_inviteeClient);

        return (company, inviteeEmail, inviteePassword);
    }

    [Fact]
    public async Task InviteCreation_WithValidEmail_SendsInviteSuccessfully()
    {
        var (company, inviteeEmail, _) = await SetupCompanyAndInviteeAsync();

        var response = await _adminClient.PostAsync($"companies/{company.Id}/invites/", new CreateInviteRequest
        {
            Email = inviteeEmail,
            Role = "student"
        });
        var body = await _adminClient.GetResponseBodyAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.Created, $"Response: {body}");

        var invite = await _adminClient.DeserializeResponseAsync<InviteResponse>(response);
        invite.Should().NotBeNull();
        invite!.Email.Should().Be(inviteeEmail);
        invite.Role.Should().Be("student");
        invite.Token.Should().NotBeNullOrEmpty("because the invite token is returned directly (no email sending)");
    }

    [Fact]
    public async Task InviteCreation_DuplicatePendingInvite_RejectsRequest()
    {
        var (company, inviteeEmail, _) = await SetupCompanyAndInviteeAsync();

        // First invite succeeds
        await _adminClient.PostAsync($"companies/{company.Id}/invites/", new CreateInviteRequest
        {
            Email = inviteeEmail,
            Role = "student"
        });

        // Duplicate invite for the same email
        var response = await _adminClient.PostAsync($"companies/{company.Id}/invites/", new CreateInviteRequest
        {
            Email = inviteeEmail,
            Role = "instructor"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "because a pending invite already exists for this email");
    }

    [Fact]
    public async Task InviteListing_WithPendingInvites_ReturnsPendingInvites()
    {
        var (company, inviteeEmail, _) = await SetupCompanyAndInviteeAsync();

        await _adminClient.PostAsync($"companies/{company.Id}/invites/", new CreateInviteRequest
        {
            Email = inviteeEmail,
            Role = "student"
        });

        var response = await _adminClient.GetAsync($"companies/{company.Id}/invites/");
        var body = await _adminClient.GetResponseBodyAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {body}");

        var invites = await _adminClient.DeserializeResponseAsync<List<InviteResponse>>(response);
        invites.Should().NotBeNull();
        invites.Should().Contain(i => i.Email == inviteeEmail);
    }

    [Fact]
    public async Task InviteAcceptance_WithValidToken_AddsUserToCompany()
    {
        var (company, inviteeEmail, inviteePassword) = await SetupCompanyAndInviteeAsync();

        // Admin creates invite
        var createResponse = await _adminClient.PostAsync($"companies/{company.Id}/invites/", new CreateInviteRequest
        {
            Email = inviteeEmail,
            Role = "student"
        });
        var invite = await _adminClient.DeserializeResponseAsync<InviteResponse>(createResponse);

        // Invitee logs in
        var loginResponse = await _inviteeClient.PostAsync<LoginRequest, LoginResponse>("auth/login/", new LoginRequest
        {
            Email = inviteeEmail,
            Password = inviteePassword
        });
        _inviteeClient.SetAuthToken(loginResponse!.AccessToken);

        // Invitee accepts the invite
        var response = await _inviteeClient.PostAsync($"invites/{invite!.Token}/accept/",
            new Dictionary<string, object>());
        var body = await _inviteeClient.GetResponseBodyAsync(response);

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {body}");

        // Verify invitee is now a member
        var membersResponse = await _adminClient.GetAsync($"companies/{company.Id}/members/");
        var members = await _adminClient.DeserializeResponseAsync<List<MemberResponse>>(membersResponse);
        members.Should().NotBeNull();
        members.Should().HaveCount(2, "because invitee accepted and should now be a member");
        members.Should().Contain(m => m.Email == inviteeEmail);
    }

    public void Dispose()
    {
        _adminClient?.Dispose();
        _inviteeClient?.Dispose();
    }
}
