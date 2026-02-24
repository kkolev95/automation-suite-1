using System.Net;
using FluentAssertions;
using TestIT.ApiTests.Helpers;
using TestIT.ApiTests.Models;
using Xunit;

namespace TestIT.ApiTests.Tests;

/// <summary>
/// Integration tests covering realistic end-to-end user journeys across multiple features.
/// These tests verify that features work correctly together in real-world scenarios.
/// </summary>
public class IntegrationTests : IDisposable
{
    private readonly ApiClient _apiClient;
    private readonly string _baseUrl;

    public IntegrationTests(ITestOutputHelper output)
    {
        ApiClient.SetOutput(output.WriteLine);
        _baseUrl = TestConfiguration.GetBaseUrl();
        _apiClient = new ApiClient(_baseUrl);
    }

    public void Dispose()
    {
        _apiClient?.Dispose();
    }

    /// <summary>
    /// Tests complete test author journey from registration to publishing.
    /// Workflow: Register → Login → Create test → Add 3 questions → Verify public access.
    /// Validates that a new user can create an account, author a test with multiple questions,
    /// and that the published test is accessible to anonymous users.
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Priority", "P0")]
    public async Task CompleteTestAuthorJourney_RegisterLoginCreateTestAddQuestionsPublish()
    {
        // Complete workflow: Register → Login → Create test → Add 3 questions → Verify public access
        var uniqueEmail = $"author_{Guid.NewGuid().ToString("N").Substring(0, 12)}@example.com";
        var password = "SecurePass123!";

        // Step 1: Register
        var registerRequest = new RegisterRequest
        {
            Email = uniqueEmail,
            Password = password,
            PasswordConfirm = password,
            FirstName = "Integration",
            LastName = "Author"
        };
        var registerResponse = await _apiClient.PostAsync<RegisterRequest, UserResponse>("auth/register/", registerRequest);
        registerResponse.Should().NotBeNull();
        registerResponse!.Email.Should().Be(uniqueEmail);

        // Step 2: Login
        var loginRequest = new LoginRequest { Email = uniqueEmail, Password = password };
        var loginResponse = await _apiClient.PostAsync<LoginRequest, LoginResponse>("auth/login/", loginRequest);
        loginResponse.Should().NotBeNull();
        _apiClient.SetAuthToken(loginResponse!.AccessToken);

        // Track account for cleanup
        TestAccountManager.TrackAccount(uniqueEmail, password, loginResponse!.AccessToken);

        // Step 3: Create test
        var createTestRequest = new CreateTestRequest
        {
            Title = $"Integration Test {Guid.NewGuid().ToString("N").Substring(0, 8)}",
            Description = "Complete end-to-end integration test",
            Visibility = "public",
            TimeLimitMinutes = 30,
            MaxAttempts = 5
        };
        var testResponse = await _apiClient.PostAsync<CreateTestRequest, TestResponse>("tests/", createTestRequest);
        testResponse.Should().NotBeNull();
        var testSlug = testResponse!.Slug;
        testSlug.Should().NotBeNullOrEmpty();

        // Step 4: Add 3 questions
        for (int i = 1; i <= 3; i++)
        {
            var questionRequest = new CreateQuestionRequest
            {
                QuestionText = $"Integration Question {i}?",
                QuestionType = "multiple_choice",
                Answers = new List<CreateAnswerRequest>
                {
                    new() { AnswerText = "Correct Answer", IsCorrect = true, Order = 1 },
                    new() { AnswerText = "Wrong Answer 1", IsCorrect = false, Order = 2 },
                    new() { AnswerText = "Wrong Answer 2", IsCorrect = false, Order = 3 }
                }
            };
            await _apiClient.PostAsync<CreateQuestionRequest, QuestionResponse>(
                $"tests/{testSlug}/questions/", questionRequest);
        }

        // Step 5: Fetch test details and verify 3 questions exist
        var testDetails = await _apiClient.GetAsync<TestResponse>($"tests/{testSlug}/");
        testDetails.Should().NotBeNull();
        testDetails!.Questions.Should().NotBeNull();
        testDetails.Questions!.Count.Should().Be(3, "test should have 3 questions");

        // Step 6: Verify public access (remove auth and fetch as anonymous)
        _apiClient.ClearAuthToken();
        var publicTest = await _apiClient.GetAsync<TakeTestResponse>($"tests/{testSlug}/take/");
        publicTest.Should().NotBeNull();
        publicTest!.Questions.Should().HaveCount(3, "public endpoint should show all 3 questions");
    }

    /// <summary>
    /// Tests multi-user lifecycle involving author and student interactions.
    /// Workflow: Author creates test → Student takes test → Author views analytics.
    /// Validates collaboration between different user roles and verifies that analytics
    /// correctly reflect student activity.
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Priority", "P0")]
    public async Task AuthorStudentLifecycle_AuthorCreatesStudentTakesAuthorViewsAnalytics()
    {
        // Multi-user journey: Author creates test → Student takes test → Author views analytics

        // Create author and login
        var authorEmail = $"author_{Guid.NewGuid().ToString("N").Substring(0, 12)}@example.com";
        var authorPassword = "AuthorPass123!";

        await _apiClient.PostAsync<RegisterRequest, UserResponse>("auth/register/", new RegisterRequest
        {
            Email = authorEmail,
            Password = authorPassword,
            PasswordConfirm = authorPassword,
            FirstName = "Test",
            LastName = "Author"
        });

        var authorLogin = await _apiClient.PostAsync<LoginRequest, LoginResponse>("auth/login/",
            new LoginRequest { Email = authorEmail, Password = authorPassword });
        var authorToken = authorLogin!.AccessToken;
        _apiClient.SetAuthToken(authorToken);
        TestAccountManager.TrackAccount(authorEmail, authorPassword, authorToken);

        // Author creates test with 2 questions
        var testRequest = new CreateTestRequest
        {
            Title = $"Student Test {Guid.NewGuid().ToString("N").Substring(0, 8)}",
            Description = "Test for students",
            Visibility = "public",
            TimeLimitMinutes = 20,
            MaxAttempts = 3
        };
        var test = await _apiClient.PostAsync<CreateTestRequest, TestResponse>("tests/", testRequest);
        var testSlug = test!.Slug;

        // Add 2 questions
        for (int i = 1; i <= 2; i++)
        {
            await _apiClient.PostAsync<CreateQuestionRequest, QuestionResponse>($"tests/{testSlug}/questions/",
                new CreateQuestionRequest
                {
                    QuestionText = $"Question {i}?",
                    QuestionType = "multiple_choice",
                    Answers = new List<CreateAnswerRequest>
                    {
                        new() { AnswerText = "Correct", IsCorrect = true, Order = 1 },
                        new() { AnswerText = "Wrong", IsCorrect = false, Order = 2 }
                    }
                });
        }

        // Create student with separate ApiClient
        using var studentClient = new ApiClient(_baseUrl);
        var studentEmail = $"student_{Guid.NewGuid().ToString("N").Substring(0, 12)}@example.com";
        var studentPassword = "StudentPass123!";

        await studentClient.PostAsync<RegisterRequest, UserResponse>("auth/register/", new RegisterRequest
        {
            Email = studentEmail,
            Password = studentPassword,
            PasswordConfirm = studentPassword,
            FirstName = "Test",
            LastName = "Student"
        });

        var studentLogin = await studentClient.PostAsync<LoginRequest, LoginResponse>("auth/login/",
            new LoginRequest { Email = studentEmail, Password = studentPassword });
        studentClient.SetAuthToken(studentLogin!.AccessToken);
        TestAccountManager.TrackAccount(studentEmail, studentPassword, studentLogin!.AccessToken);

        // Student starts and submits attempt
        var attempt = await studentClient.PostAsync<object, AttemptResponse>($"tests/{testSlug}/attempts/", new { });
        attempt.Should().NotBeNull();

        var submit = await studentClient.PostAsync<object, object>(
            $"tests/{testSlug}/attempts/{attempt!.Id}/submit/", new { });
        submit.Should().NotBeNull();

        // Author views analytics
        _apiClient.SetAuthToken(authorToken);
        var analytics = await _apiClient.GetAsync<AnalyticsResponse>($"analytics/tests/{testSlug}/");
        analytics.Should().NotBeNull();
        analytics!.TotalAttempts.Should().BeGreaterThan(0, "analytics should show at least one attempt");
    }

    /// <summary>
    /// Tests complete company workflow including member invitation and collaboration.
    /// Workflow: Admin creates company → Invites member → Member takes test → Admin views analytics.
    /// Validates company creation, member invitation system, test taking within company context,
    /// and analytics visibility for company admins.
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Priority", "P0")]
    public async Task CompanyWorkflow_AdminCreatesInvitesMemberTakesTest()
    {
        // Company collaboration: Admin creates company → Invites member → Member takes company test → Admin sees analytics

        // Create admin account
        var adminEmail = $"admin_{Guid.NewGuid().ToString("N").Substring(0, 12)}@example.com";
        var adminPassword = "AdminPass123!";

        await _apiClient.PostAsync<RegisterRequest, UserResponse>("auth/register/", new RegisterRequest
        {
            Email = adminEmail,
            Password = adminPassword,
            PasswordConfirm = adminPassword,
            FirstName = "Company",
            LastName = "Admin"
        });

        var adminLogin = await _apiClient.PostAsync<LoginRequest, LoginResponse>("auth/login/",
            new LoginRequest { Email = adminEmail, Password = adminPassword });
        _apiClient.SetAuthToken(adminLogin!.AccessToken);
        TestAccountManager.TrackAccount(adminEmail, adminPassword, adminLogin!.AccessToken);

        // Admin creates company
        var companyRequest = new CreateCompanyRequest
        {
            Name = $"Integration Co {Guid.NewGuid().ToString("N").Substring(0, 8)}"
        };
        var company = await _apiClient.PostAsync<CreateCompanyRequest, CompanyResponse>("companies/", companyRequest);
        company.Should().NotBeNull();
        var companyId = company!.Id;

        // Admin creates company test
        var testRequest = new CreateCompanyTestRequest
        {
            Title = $"Company Test {Guid.NewGuid().ToString("N").Substring(0, 8)}",
            Description = "Test for company members",
            TimeLimitMinutes = 15
        };
        var test = await _apiClient.PostAsync<CreateCompanyTestRequest, TestResponse>(
            $"tests/company/{companyId}/", testRequest);
        test.Should().NotBeNull();
        var testSlug = test!.Slug;

        // Add question to test
        await _apiClient.PostAsync<CreateQuestionRequest, QuestionResponse>($"tests/{testSlug}/questions/",
            new CreateQuestionRequest
            {
                QuestionText = "Company question?",
                QuestionType = "multiple_choice",
                Answers = new List<CreateAnswerRequest>
                {
                    new() { AnswerText = "Correct", IsCorrect = true },
                    new() { AnswerText = "Wrong", IsCorrect = false }
                }
            });

        // Create member account
        using var memberClient = new ApiClient(_baseUrl);
        var memberEmail = $"member_{Guid.NewGuid().ToString("N").Substring(0, 12)}@example.com";
        var memberPassword = "MemberPass123!";

        await memberClient.PostAsync<RegisterRequest, UserResponse>("auth/register/", new RegisterRequest
        {
            Email = memberEmail,
            Password = memberPassword,
            PasswordConfirm = memberPassword,
            FirstName = "Company",
            LastName = "Member"
        });

        var memberLogin = await memberClient.PostAsync<LoginRequest, LoginResponse>("auth/login/",
            new LoginRequest { Email = memberEmail, Password = memberPassword });
        var memberToken = memberLogin!.AccessToken;
        TestAccountManager.TrackAccount(memberEmail, memberPassword, memberToken);

        // Admin invites member
        var inviteRequest = new CreateInviteRequest
        {
            Email = memberEmail,
            Role = "student"
        };
        var invite = await _apiClient.PostAsync<CreateInviteRequest, InviteResponse>(
            $"companies/{companyId}/invites/", inviteRequest);
        invite.Should().NotBeNull();

        // Member accepts invite
        memberClient.SetAuthToken(memberToken);
        var accept = await memberClient.PostAsync<object, object>($"invites/{invite!.Token}/accept/", new { });
        accept.Should().NotBeNull();

        // Member takes test
        var attempt = await memberClient.PostAsync<object, AttemptResponse>($"tests/{testSlug}/attempts/", new { });
        attempt.Should().NotBeNull();

        var submit = await memberClient.PostAsync<object, object>(
            $"tests/{testSlug}/attempts/{attempt!.Id}/submit/", new { });
        submit.Should().NotBeNull();

        // Admin views analytics
        _apiClient.SetAuthToken(adminLogin.AccessToken);
        var analytics = await _apiClient.GetAsync<AnalyticsResponse>($"analytics/tests/{testSlug}/");
        analytics.Should().NotBeNull();
        analytics!.TotalAttempts.Should().BeGreaterThan(0, "admin should see member's attempt in analytics");
    }

    /// <summary>
    /// Tests security boundaries between different companies.
    /// Workflow: User A creates company → User B attempts access → Access denied.
    /// Validates that company data is properly isolated and users cannot access
    /// companies they don't belong to, including company details and test analytics.
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Priority", "P1")]
    public async Task CrossCompanySecurity_UserBCannotAccessUserACompany()
    {
        // Security: User A creates company → User B cannot access it

        // User A creates company
        var userAEmail = $"usera_{Guid.NewGuid().ToString("N").Substring(0, 10)}@example.com";
        await _apiClient.PostAsync<RegisterRequest, UserResponse>("auth/register/", new RegisterRequest
        {
            Email = userAEmail,
            Password = "Pass123!",
            PasswordConfirm = "Pass123!",
            FirstName = "UserA",
            LastName = "Test"
        });

        var userALogin = await _apiClient.PostAsync<LoginRequest, LoginResponse>("auth/login/",
            new LoginRequest { Email = userAEmail, Password = "Pass123!" });
        _apiClient.SetAuthToken(userALogin!.AccessToken);
        TestAccountManager.TrackAccount(userAEmail, "Pass123!", userALogin!.AccessToken);

        var companyA = await _apiClient.PostAsync<CreateCompanyRequest, CompanyResponse>("companies/",
            new CreateCompanyRequest { Name = $"Company A {Guid.NewGuid().ToString("N").Substring(0, 8)}" });
        var companyAId = companyA!.Id;

        // Create test in company A
        var testA = await _apiClient.PostAsync<CreateCompanyTestRequest, TestResponse>(
            $"tests/company/{companyAId}/", new CreateCompanyTestRequest
            {
                Title = $"Private Test {Guid.NewGuid().ToString("N").Substring(0, 8)}"
            });
        var testASlug = testA!.Slug;

        // User B tries to access Company A
        using var userBClient = new ApiClient(_baseUrl);
        var userBEmail = $"userb_{Guid.NewGuid().ToString("N").Substring(0, 10)}@example.com";
        await userBClient.PostAsync<RegisterRequest, UserResponse>("auth/register/", new RegisterRequest
        {
            Email = userBEmail,
            Password = "Pass123!",
            PasswordConfirm = "Pass123!",
            FirstName = "UserB",
            LastName = "Test"
        });

        var userBLogin = await userBClient.PostAsync<LoginRequest, LoginResponse>("auth/login/",
            new LoginRequest { Email = userBEmail, Password = "Pass123!" });
        userBClient.SetAuthToken(userBLogin!.AccessToken);
        TestAccountManager.TrackAccount(userBEmail, "Pass123!", userBLogin!.AccessToken);

        // User B cannot view Company A
        var companyAccess = await userBClient.GetAsync<CompanyResponse>($"companies/{companyAId}/");
        companyAccess.Should().BeNull("User B should not be able to access Company A");

        // User B cannot view Company A's analytics
        var analyticsAccess = await userBClient.GetAsync<AnalyticsResponse>($"analytics/tests/{testASlug}/");
        analyticsAccess.Should().BeNull("User B should not be able to access Company A's test analytics");
    }

    /// <summary>
    /// Tests permission enforcement for student role within companies.
    /// Workflow: Admin creates company → Invites student → Student attempts to create test → Access denied.
    /// Validates that students have read-only access and cannot perform administrative
    /// actions like creating company tests. Verifies proper role-based access control.
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Priority", "P1")]
    public async Task PermissionFlow_StudentCannotCreateCompanyTests()
    {
        // Authorization: Verify students cannot create company tests

        // Create admin
        var adminEmail = $"perm_admin_{Guid.NewGuid().ToString("N").Substring(0, 10)}@example.com";
        await _apiClient.PostAsync<RegisterRequest, UserResponse>("auth/register/", new RegisterRequest
        {
            Email = adminEmail,
            Password = "Pass123!",
            PasswordConfirm = "Pass123!",
            FirstName = "Admin",
            LastName = "Test"
        });

        var adminLogin = await _apiClient.PostAsync<LoginRequest, LoginResponse>("auth/login/",
            new LoginRequest { Email = adminEmail, Password = "Pass123!" });
        _apiClient.SetAuthToken(adminLogin!.AccessToken);
        TestAccountManager.TrackAccount(adminEmail, "Pass123!", adminLogin!.AccessToken);

        // Create company
        var company = await _apiClient.PostAsync<CreateCompanyRequest, CompanyResponse>("companies/",
            new CreateCompanyRequest { Name = $"Perm Test Co {Guid.NewGuid().ToString("N").Substring(0, 8)}" });
        var companyId = company!.Id;

        // Create student member
        using var studentClient = new ApiClient(_baseUrl);
        var studentEmail = $"perm_student_{Guid.NewGuid().ToString("N").Substring(0, 10)}@example.com";
        await studentClient.PostAsync<RegisterRequest, UserResponse>("auth/register/", new RegisterRequest
        {
            Email = studentEmail,
            Password = "Pass123!",
            PasswordConfirm = "Pass123!",
            FirstName = "Student",
            LastName = "Test"
        });

        var studentLogin = await studentClient.PostAsync<LoginRequest, LoginResponse>("auth/login/",
            new LoginRequest { Email = studentEmail, Password = "Pass123!" });
        TestAccountManager.TrackAccount(studentEmail, "Pass123!", studentLogin!.AccessToken);

        // Admin invites student and student accepts
        var invite = await _apiClient.PostAsync<CreateInviteRequest, InviteResponse>(
            $"companies/{companyId}/invites/", new CreateInviteRequest { Email = studentEmail, Role = "student" });

        studentClient.SetAuthToken(studentLogin!.AccessToken);
        await studentClient.PostAsync<object, object>($"invites/{invite!.Token}/accept/", new { });

        // Student tries to create company test (should fail)
        var createTest = await studentClient.PostAsync<CreateCompanyTestRequest, TestResponse>(
            $"tests/company/{companyId}/", new CreateCompanyTestRequest { Title = "Should Fail" });
        createTest.Should().BeNull("Student should not be able to create company tests");

        // Verify student is listed as a company member
        var members = await _apiClient.GetAsync<List<MemberResponse>>($"companies/{companyId}/members/");
        members.Should().Contain(m => m.Email == studentEmail, "student should be a company member");
    }
}
