"""
generate_report.py
Reads a Visual Studio TRX file and produces a self-contained HTML test report.

Usage:
    python3 generate_report.py <input.trx> <output.html>
"""

import xml.etree.ElementTree as ET
from collections import OrderedDict
from datetime import datetime, timezone, timedelta
import re, html, os, sys

# ---------------------------------------------------------------------------
# Paths (defaults or from command-line)
# ---------------------------------------------------------------------------
if len(sys.argv) >= 3:
    TRX_PATH  = sys.argv[1]
    HTML_PATH = sys.argv[2]
else:
    TRX_PATH   = "/home/kolev95/examtest1/TestIT.ApiTests/results.trx"
    HTML_PATH  = "/home/kolev95/examtest1/TestIT.ApiTests/test-report.html"

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------
NS = {"ns": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}

def parse_duration(dur_str: str) -> float:
    """Convert 'HH:MM:SS.fffffff' to total seconds (float)."""
    parts = dur_str.split(":")
    h, m = int(parts[0]), int(parts[1])
    s = float(parts[2])
    return h * 3600 + m * 60 + s

def fmt_dur(seconds: float) -> str:
    """Format seconds to M:SS.ss or H:MM:SS.ss as appropriate."""
    if seconds >= 3600:
        h = int(seconds // 3600)
        remainder = seconds - h * 3600
        m = int(remainder // 60)
        s = remainder - m * 60
        return f"{h}h {m:02d}m {s:05.2f}s"
    elif seconds >= 60:
        m = int(seconds // 60)
        s = seconds - m * 60
        return f"{m}m {s:05.2f}s"
    else:
        return f"{seconds:.2f}s"

def parse_iso_datetime(s: str) -> datetime:
    """Parse an ISO-8601 datetime that may have a +HH:MM tz offset."""
    # Handle timezone format: remove colon if present (+02:00 -> +0200)
    if re.search(r'[+-]\d{2}:\d{2}$', s):
        s = s[:-3] + s[-2:]

    # Handle variable-length fractional seconds (up to 7 digits in TRX files)
    # Truncate to 6 digits for strptime compatibility
    match = re.match(r'(.+\.\d{6})\d*([+-]\d{4})$', s)
    if match:
        s = match.group(1) + match.group(2)

    return datetime.strptime(s, "%Y-%m-%dT%H:%M:%S.%f%z")

# ---------------------------------------------------------------------------
# Test-level descriptions (method name -> description)
# ---------------------------------------------------------------------------
TEST_DESCRIPTIONS = {
    "TokenRefresh_WithValidRefreshToken_IssuesNewAccessToken": "Registers, logs in, then uses the refresh token to obtain a new access token. Confirms a fresh access token is issued.",
    "UserProfile_WhenAuthenticated_ReturnsUserDetails": "Registers and logs in a user, then retrieves the profile using the access token. Verifies the returned data matches the registered user.",
    "Login_WithValidCredentials_ReturnsAuthenticationTokens": "Registers a user, then logs in with the same credentials. Verifies both an access token and a refresh token are returned.",
    "Registration_WithValidCredentials_CreatesUserAccount": "Registers a new user with a valid email, matching passwords and full name. Verifies the account is created and the returned profile data matches the input.",
    "Login_WithInvalidCredentials_DeniesAccess": "Attempts login with a non-existent email and incorrect password. Confirms access is denied.",
    "Registration_WithMismatchedPasswords_RejectsRequest": "Submits a registration where password and confirmation do not match. Confirms the request is rejected before any account is created.",
    "Registration_WithMissingRequiredFields_RejectsRequest": "Sends a registration payload that omits first name and last name. Confirms the API rejects the request for missing required fields.",
    "Registration_WithWeakPassword_RejectsRequest": "Attempts registration with a very short password. Confirms the API enforces minimum password complexity and rejects the request.",
    "Login_WithMissingPassword_RejectsRequest": "Sends a login request containing only an email with no password. Confirms the API rejects the incomplete request.",
    "UserProfile_WhenUnauthenticated_DeniesAccess": "Requests the current-user profile without any authorization header. Confirms the endpoint is protected and access is denied.",
    "TestDetails_WithNonExistentSlug_ReturnsNotFound": "Requests details for a slug that does not exist in the system. Confirms a not-found response is returned.",
    "TestDeletion_WithValidSlug_RemovesTestCompletely": "Creates a test, deletes it, then attempts to fetch it again. Confirms the test is fully removed and no longer accessible.",
    "QuestionAddition_WithValidData_AddsQuestionToTest": "Creates a test, then adds a multiple-choice question with three answer options (one correct). Verifies the question, its type and all answers are stored correctly.",
    "TestListing_AsAuthor_ReturnsOwnTests": "Creates a test, then lists all tests for the authenticated author. Confirms the new test appears in the results.",
    "TestDetails_WithValidSlug_ReturnsTestData": "Creates a test, then fetches it by its slug. Verifies the returned slug and title match the created test.",
    "TestUpdate_WithValidData_UpdatesTestSuccessfully": "Creates a test, then updates its title, description, visibility and max attempts. Verifies every changed field is reflected in the response.",
    "TestCreation_WithMissingTitle_RejectsRequest": "Submits a test creation request with an empty title. Confirms the API rejects it because title is required.",
    "TestCreation_WithValidData_CreatesTestSuccessfully": "Creates a quiz with title, description, visibility, time limit, max attempts and show-answers flag. Verifies all fields are stored correctly and an auto-generated slug is present.",
    "TestCreation_WhenUnauthenticated_DeniesAccess": "Attempts to create a test without an auth token. Confirms the endpoint rejects unauthenticated requests.",
    "QuestionUpdate_WithValidData_UpdatesQuestionSuccessfully": "Creates a test with one question, then updates the question text and replaces the entire answer set. Verifies the new question text is stored.",
    "QuestionDeletion_VerifyRemoval_QuestionNoLongerInTest": "Creates and deletes a question, then re-fetches the parent test. Confirms the deleted question no longer appears in the question list.",
    "QuestionUpdate_WithNonExistentId_ReturnsNotFound": "Attempts to update a question using an ID that does not exist. Confirms a not-found response is returned.",
    "QuestionReorder_WithValidOrder_ReordersSuccessfully": "Creates three questions, then posts a reorder request that reverses their sequence. Confirms the API accepts the new ordering.",
    "QuestionDeletion_WithValidId_RemovesQuestion": "Creates a question, then deletes it by ID. Confirms the deletion is acknowledged.",
    "QuestionCreation_MultiSelectType_CreatesWithMultipleCorrectAnswers": "Creates a multi-select question with four options, two of which are marked correct. Verifies the question type and that both correct answers are stored.",
    "AttemptSubmission_AllWrongAnswers_ScoresZeroMarks": "Same flow as the full-marks test but every saved answer is incorrect. Confirms the score is 0% and no answers are marked correct.",
    "ResultsRetrieval_AsAuthor_ReturnsAllSubmissions": "Completes an attempt, then the test author retrieves the results list. Confirms the list is non-empty and contains the expected submission.",
    "AttemptStart_AsAnonymousUser_CreatesAttemptSuccessfully": "Creates a test and starts an anonymous attempt with a display name. Verifies a new attempt is created and an attempt ID is returned.",
    "TestAccess_PublicTest_AllowsAnonymousAccess": "Creates a public test with a question, then fetches it via the anonymous take endpoint. Verifies the test content is accessible without authentication.",
    "AttemptSubmission_AllCorrectAnswers_ScoresFullMarks": "Runs the full attempt flow: start, save all correct answers as draft, submit. Then fetches results as the author and confirms the score is 100%.",
    "PasswordVerification_WithCorrectPassword_GrantsAccess": "Creates a password-protected test, then submits the correct password to the verification endpoint. Confirms access is granted and a token is issued.",
    "PasswordVerification_WithWrongPassword_DeniesAccess": "Submits an incorrect password to the verification endpoint of a protected test. Confirms the request is rejected.",
    "TestAccess_PasswordProtected_BlocksWithoutPassword": "Creates a password-protected test, then tries to access it without providing the password. Confirms access is blocked and a password-required flag is returned.",
    "AttemptSubmission_AlreadySubmitted_PreventsDoubleSubmission": "Completes and submits an attempt, then immediately tries to submit it a second time. Confirms the duplicate submission is blocked.",
    "DraftSave_WithValidAnswers_SavesDraftSuccessfully": "Starts an attempt, then saves an answer for the first question as a draft. Confirms the draft is accepted.",
    "CompanyTestCreation_WithValidData_CreatesTestInCompany": "Creates a company, then creates a test scoped to that company. Verifies the test is created with the expected title.",
    "CompanyTestListing_AsAdmin_ReturnsCompanyTests": "Creates a company and requests the list of tests scoped to it. Confirms the endpoint responds successfully.",
    "CompanyDetails_AsAdmin_ReturnsCompanyData": "Creates a company and fetches it by ID. Verifies the returned record matches the created company.",
    "CompanyCreation_WithValidData_CreatesCompanySuccessfully": "Creates a company with a unique name. Verifies the company is created and appears in the company list with a valid ID.",
    "CompanyDeletion_AsAdmin_RemovesCompanyCompletely": "Creates a company, deletes it, then confirms a subsequent fetch returns not found — the company is fully removed.",
    "MemberListing_NewCompany_CreatorIsOnlyAdmin": "Creates a company and lists its members. Confirms there is exactly one member — the creator — and they hold the admin role.",
    "CompanyListing_AfterCreation_IncludesNewCompany": "Creates a company, then lists all companies. Confirms the newly created company is included.",
    "CompanyUpdate_WithValidData_UpdatesCompanySuccessfully": "Creates a company, updates its name, then verifies the new name is stored.",
    "CompanyCreation_WhenUnauthenticated_DeniesAccess": "Tries to create a company without an auth token. Confirms unauthenticated requests are rejected.",
    "InviteCreation_DuplicatePendingInvite_RejectsRequest": "Sends two invites to the same email for the same company. Confirms the second request is rejected because a pending invite already exists.",
    "InviteAcceptance_WithValidToken_AddsUserToCompany": "Full invite flow: admin creates the invite, the invitee logs in and accepts it, then the admin lists members and confirms the invitee has joined.",
    "InviteListing_WithPendingInvites_ReturnsPendingInvites": "Creates an invite, then lists all invites for the company. Confirms the pending invite for the expected email is present.",
    "InviteCreation_WithValidEmail_SendsInviteSuccessfully": "An admin creates a company and sends an invite to a registered user. Verifies the invite is created with the correct email, role and a valid token.",
    "FolderCreation_WithParent_CreatesNestedFolderStructure": "Creates a parent folder, then a child folder that references the parent. Verifies the child's parent field correctly points to the parent folder.",
    "FolderListing_AfterCreation_IncludesNewFolder": "Creates a folder, then lists all folders for the company. Confirms the new folder is present.",
    "FolderDeletion_WithValidId_RemovesFolder": "Creates a folder, then deletes it. Confirms the deletion is acknowledged.",
    "FolderUpdate_WithNewName_RenamesFolderSuccessfully": "Creates a folder, renames it, then verifies the updated name is stored.",
    "FolderCreation_TopLevel_CreatesFolderSuccessfully": "Creates a top-level folder inside a company. Verifies it is created and appears in the folder list with no parent.",
    "Analytics_AsNonAuthor_DeniesAccessToOtherUsersTest": "One user creates a test, a different authenticated user tries to access its analytics. Confirms access is denied (the API may hide existence entirely).",
    "Analytics_AfterSubmission_ReflectsAttemptData": "Creates a test, runs one full attempt (draft + submit), then fetches analytics. Confirms the stats reflect the submission.",
    "Analytics_NoSubmissions_ReturnsZeroStats": "Creates a test with a question but no submissions. Fetches analytics and confirms total attempts is zero.",
    "Analytics_WhenUnauthenticated_DeniesAccess": "Creates a test, removes the auth token, then tries to access analytics. Confirms the endpoint is protected.",
    # SecurityTests
    "Authentication_InvalidToken_DeniesAccess": "Attempts to access a protected endpoint with an invalid token. Confirms the request is denied.",
    "Authentication_MalformedToken_DeniesAccess": "Sends a malformed authentication token. Confirms the API rejects it and denies access.",
    "Authentication_NoToken_DeniesProtectedEndpoints": "Tries to access protected endpoints without any token. Confirms all require authentication.",
    "Authorization_UserCannotAccessOtherCompaniesData": "Ensures a user from one company cannot view data belonging to a different company.",
    "Authorization_UserCannotAccessOtherUsersTestResults": "Attempts to access another user's test results. Confirms proper ownership enforcement.",
    "Authorization_UserCannotAccessOtherUsersTests": "Verifies a user cannot view or access tests created by other users without permission.",
    "Authorization_UserCannotDeleteOtherUsersTests": "Attempts to delete a test owned by another user. Confirms deletion is blocked.",
    "Authorization_UserCannotUpdateOtherUsersTests": "Tries to modify a test created by another user. Confirms updates are denied.",
    "DataExposure_CorrectAnswers_NotExposedBeforeSubmission": "Fetches test questions before submitting. Confirms correct answers are not leaked in the response.",
    "DataExposure_DetailedErrorMessages_DoNotLeakSensitiveInfo": "Triggers various errors and inspects messages. Confirms no sensitive data like stack traces or internal paths are exposed.",
    "DataExposure_PasswordNotReturned_InProfileEndpoint": "Retrieves the user profile. Confirms the password field is never included in the response.",
    "InputValidation_ExtremelyLongEmail_IsRejected": "Submits registration with an excessively long email address. Confirms the API validates input length.",
    "InputValidation_NegativeMaxAttempts_IsRejected": "Attempts to create a test with negative max attempts. Confirms validation rejects invalid values.",
    "InputValidation_OversizedPayload_IsRejected": "Sends a request with an extremely large payload. Confirms the API enforces size limits.",
    "InputValidation_SQLInjectionInTestTitle_IsSanitized": "Submits a test title containing SQL injection patterns. Confirms input is sanitized and does not cause database errors.",
    "InputValidation_XSSInQuestionText_IsSanitized": "Creates a question with XSS payloads in the text. Confirms the content is properly escaped.",
    # DataIntegrityTests
    "DataConsistency_AttemptCountAccurate_AfterMultipleSubmissions": "Submits multiple attempts and verifies the analytics accurately reflect the total count.",
    "DataConsistency_ConcurrentAttempts_AllScoredCorrectly": "Simulates concurrent test submissions and confirms all scores are calculated correctly without race conditions.",
    "DataConsistency_DraftSaveDoesNotAffectFinalScore": "Saves multiple draft answers, then submits. Confirms only submitted answers affect the final score.",
    "DataConsistency_QuestionOrderChange_DoesNotCorruptAttempts": "Reorders questions in a test, then verifies existing attempts remain intact and uncorrupted.",
    "DataConsistency_QuestionUpdate_DoesNotAffectSubmittedAttempts": "Modifies a question after attempts are submitted. Confirms historical submissions are not altered.",
    "DataConsistency_ScoreRemainsConstant_AcrossMultipleFetches": "Fetches the same result multiple times. Confirms the score is consistent across all requests.",
    "Immutability_SubmittedAttempts_CannotBeModified": "Attempts to update a submitted attempt. Confirms the API prevents modification of finalized submissions.",
    "Immutability_TestDeletion_PreservesSubmittedResults": "Deletes a test that has submitted results. Confirms the results data is preserved or handled appropriately.",
    "ScoringAccuracy_AllQuestionsCorrect_Scores100Percent": "Answers all questions correctly. Confirms the score is exactly 100%.",
    "ScoringAccuracy_AllQuestionsSkipped_Scores0Percent": "Submits an attempt without answering any questions. Confirms the score is 0%.",
    "ScoringAccuracy_AllQuestionsWrong_Scores0Percent": "Answers all questions incorrectly. Confirms the score is 0%.",
    "ScoringAccuracy_HalfCorrect_Scores50Percent": "Answers half the questions correctly. Confirms the score is 50%.",
    "ScoringAccuracy_MultiSelectPartialAnswer_ScoreIsValidPercentage": "Partially answers a multi-select question. Confirms partial credit is awarded appropriately.",
    "ScoringAccuracy_SingleQuestionTest_ScoresCorrectly": "Creates a single-question test and verifies scoring works correctly for the simplest case.",
    # EdgeCaseTests
    "CompanyInvite_InvalidRoleValue_ShouldFail": "Attempts to create an invite with an invalid role value. Confirms the request is rejected.",
    "CreateQuestion_EmptyQuestionText_ShouldFail": "Tries to create a question with empty text. Confirms the API requires question text.",
    "CreateQuestion_InvalidQuestionType_ShouldFail": "Submits a question with an unsupported or invalid type. Confirms validation rejects it.",
    "CreateQuestion_NoAnswers_ShouldFail": "Creates a question without any answer options. Confirms at least one answer is required.",
    "CreateQuestion_NoCorrectAnswer_ShouldFail": "Creates a question where no answer is marked correct. Confirms at least one correct answer is required.",
    "CreateTest_EmptyPassword_ShouldWork": "Creates a password-protected test with an empty password. Confirms empty passwords are allowed or handled gracefully.",
    "CreateTest_InvalidVisibilityValue_ShouldFail": "Submits a test with an invalid visibility setting. Confirms the API validates visibility values.",
    "CreateTest_NegativeMaxAttempts_ShouldFail": "Attempts to create a test with a negative max attempts value. Confirms validation rejects it.",
    "CreateTest_NegativeTimeLimit_ShouldFail": "Tries to set a negative time limit for a test. Confirms negative values are rejected.",
    "CreateTest_NullDescription_ShouldWork": "Creates a test with a null description field. Confirms null descriptions are handled properly.",
    "CreateTest_SpecialCharactersInTitle_ShouldWork": "Creates a test with special characters in the title. Confirms they are handled correctly.",
    "CreateTest_UnicodeCharactersInTitle_ShouldWork": "Creates a test with Unicode characters in the title. Confirms international characters are supported.",
    "CreateTest_VeryLargeTimeLimit_ShouldHandleGracefully": "Submits a test with an extremely large time limit. Confirms the API handles edge values appropriately.",
    "CreateTest_VeryLongPassword_ShouldHandleGracefully": "Creates a test with an extremely long password. Confirms the API handles long input gracefully.",
    "CreateTest_VeryLongTitle_ShouldHandleGracefully": "Submits a test with a very long title. Confirms length limits are enforced or handled.",
    "CreateTest_WhitespaceOnlyTitle_ShouldFail": "Attempts to create a test with only whitespace in the title. Confirms validation rejects it.",
    "CreateTest_ZeroMaxAttempts_ShouldFail": "Tries to set max attempts to zero. Confirms zero is rejected as invalid.",
    "CreateTest_ZeroTimeLimit_ShouldBeAllowed": "Creates a test with zero time limit (untimed). Confirms zero is accepted to mean no time limit.",
    "GetTest_InvalidSlugFormat_Returns404": "Requests a test with a malformed slug. Confirms a 404 response.",
    "GetTest_NonExistentSlug_Returns404": "Attempts to fetch a test that doesn't exist. Confirms a 404 response.",
    "GetTest_VeryLongSlug_ShouldHandleGracefully": "Requests a test with an extremely long slug. Confirms the API handles it without errors.",
    "Register_EmailWithDots_ShouldWork": "Registers with an email containing dots. Confirms dot notation in emails is supported.",
    "Register_EmailWithPlus_ShouldWork": "Registers with an email containing a plus sign. Confirms plus-addressing is supported.",
    "Register_EmailWithSubdomain_ShouldWork": "Registers with an email from a subdomain. Confirms various email formats are accepted.",
    "Register_VeryLongEmail_ShouldHandleGracefully": "Attempts registration with an extremely long email address. Confirms length validation.",
    "SaveAnswers_EmptyAnswersList_ShouldWork": "Saves a draft with an empty answers array. Confirms empty drafts are handled gracefully.",
    "SubmitAttempt_NoAnswersSaved_ShouldScoreZero": "Submits an attempt without saving any answers. Confirms the score is 0%.",
    # CoverageTests
    "DeleteCompanyTest_AsAdmin_RemovesTestSuccessfully": "Admin user deletes a company-scoped test. Confirms successful deletion.",
    "DeleteCompanyTest_AsNonMember_DeniesAccess": "Non-member attempts to delete a company test. Confirms access is denied.",
    "GetCompanyTestDetail_AsMember_ReturnsTestData": "Company member fetches a company test. Confirms access is granted.",
    "GetCompanyTestDetail_AsNonMember_DeniesAccess": "Non-member tries to view a company test. Confirms access is denied.",
    "GetPublicTests_LinkOnlyTest_DoesNotAppearInList": "Creates a link-only test and fetches public tests list. Confirms link-only tests are excluded.",
    "GetPublicTests_PublicTest_AppearsInList": "Creates a public test. Confirms it appears in the public tests listing.",
    "GetPublicTests_Unauthenticated_Returns200": "Fetches public tests without authentication. Confirms the endpoint is publicly accessible.",
    "GetQuestion_WithNonExistentId_ReturnsNotFound": "Requests a question by a non-existent ID. Confirms a 404 response.",
    "GetQuestion_WithValidId_ReturnsQuestionData": "Fetches a question by valid ID. Confirms the question data is returned.",
    "GetResultDetail_ByAuthor_ReturnsDetailedBreakdown": "Test author fetches detailed results. Confirms full breakdown is provided.",
    "GetResultDetail_ByNonAuthor_DeniesAccess": "Non-author attempts to view detailed results. Confirms access is denied.",
    "PatchCompanyTest_AsAdmin_UpdatesTestSuccessfully": "Admin user patches a company test. Confirms the update is successful.",
    "PatchCompanyTest_AsNonMember_DeniesAccess": "Non-member attempts to patch a company test. Confirms access is denied.",
    "RemoveMember_AsAdmin_RemovesMemberSuccessfully": "Admin removes a member from the company. Confirms the member is removed.",
    "RemoveMember_LastAdmin_DeniesRemoval": "Attempts to remove the last admin from a company. Confirms the operation is blocked.",
    "UpdateMemberRole_AsAdmin_ChangesRoleSuccessfully": "Admin changes a member's role. Confirms the role is updated.",
    "UpdateMemberRole_ByNonAdmin_DeniesAccess": "Non-admin tries to change member roles. Confirms access is denied.",
    # PatchTests
    "PatchCompany_ByNonMember_DeniesAccess": "Non-member attempts to patch company details. Confirms access is denied.",
    "PatchCompany_Name_UpdatesNameSuccessfully": "Patches only the company name. Confirms the name is updated and other fields remain unchanged.",
    "PatchFolder_MoveToParent_UpdatesParentSuccessfully": "Moves a folder to a different parent. Confirms the parent reference is updated.",
    "PatchFolder_Name_UpdatesNameSuccessfully": "Patches only the folder name. Confirms the name is updated.",
    "PatchProfile_BothNames_UpdatesBothFields": "Patches both first and last name. Confirms both fields are updated.",
    "PatchProfile_FirstNameOnly_UpdatesFirstNamePreservesLastName": "Patches only first name. Confirms last name is preserved unchanged.",
    "PatchProfile_LastNameOnly_UpdatesLastNamePreservesFirstName": "Patches only last name. Confirms first name is preserved unchanged.",
    "PatchProfile_Unauthenticated_DeniesAccess": "Attempts to patch profile without authentication. Confirms access is denied.",
    "PatchQuestion_ByNonOwner_DeniesAccess": "Non-owner tries to patch a question. Confirms access is denied.",
    "PatchQuestion_TextOnly_UpdatesTextPreservesAnswers": "Patches only the question text. Confirms answers remain unchanged.",
    "PatchTest_ByNonOwner_DeniesAccess": "Non-owner attempts to patch a test. Confirms access is denied.",
    "PatchTest_TitleOnly_UpdatesTitlePreservesOtherFields": "Patches only the test title. Confirms all other fields remain unchanged.",
    "PatchTest_Unauthenticated_DeniesAccess": "Attempts to patch a test without authentication. Confirms access is denied.",
    "PatchTest_VisibilityOnly_UpdatesVisibilityPreservesTitle": "Patches only the visibility. Confirms title and other fields are preserved.",
    # PerformanceTests
    "Latency_CreateTest_RespondsFast": "Creates a test and measures response time. Confirms it meets the SLA threshold.",
    "Latency_GetAnalytics_RespondsFast": "Fetches analytics and measures latency. Confirms response time is within acceptable limits.",
    "Latency_GetProfile_RespondsFast": "Retrieves user profile and verifies fast response time.",
    "Latency_GetResults_RespondsFast": "Fetches test results and measures latency. Confirms it meets performance requirements.",
    "Latency_GetTakeEndpoint_RespondsFast": "Accesses the take test endpoint and verifies response time is fast.",
    "Latency_GetTestDetail_RespondsFast": "Fetches test details and confirms latency is within SLA.",
    "Latency_ListTests_RespondsFast": "Lists all tests and measures response time. Confirms it meets performance standards.",
    "Latency_Login_RespondsFast": "Performs login and verifies the response time is acceptable.",
    "Latency_StartAttempt_RespondsFast": "Starts a test attempt and confirms fast response time.",
    "Latency_SubmitAttempt_RespondsFast": "Submits a test attempt and measures latency. Confirms it meets SLA requirements.",
    # SchemaValidationTests
    "Schema_AnalyticsResponse_HasRequiredFields": "Fetches analytics and validates the response schema contains all required fields.",
    "Schema_AttemptResponse_HasRequiredFields": "Creates an attempt and validates the response includes all expected fields.",
    "Schema_CompanyResponse_HasRequiredFields": "Fetches company data and confirms the schema matches expectations.",
    "Schema_LoginResponse_HasRequiredFieldsOnly": "Performs login and validates the response contains required fields without leaking extra data.",
    "Schema_QuestionResponse_HasRequiredFields": "Fetches question data and confirms the schema is correct.",
    "Schema_ResultResponse_HasRequiredFields": "Retrieves results and validates all required fields are present.",
    "Schema_TakeTestResponse_DoesNotLeakCorrectAnswers": "Fetches test via take endpoint and confirms correct answers are not exposed.",
    "Schema_TestResponse_HasRequiredFields": "Fetches test details and validates the response schema.",
    "Schema_UserResponse_HasRequiredFieldsOnly": "Retrieves user data and confirms only expected fields are returned without sensitive data leaks.",
    # StressTests
    "StressTest_ConcurrentTestCreations_HandlesLoad": "Simulates many users creating tests concurrently. Confirms the system handles the load.",
    "StressTest_ConcurrentTestRetrieval_HandlesReadLoad": "Simulates concurrent test retrievals. Confirms read performance under high load.",
    "StressTest_ConcurrentTestSubmissions_HandlesLoad": "Simulates many concurrent test submissions. Confirms the system remains stable.",
    "StressTest_ConcurrentUserRegistrations_HandlesLoad": "Simulates high registration volume. Confirms the system handles concurrent user creation.",
    "StressTest_MassQuestionCreation_HandlesLargeTests": "Creates a test with a very large number of questions. Confirms the system handles large datasets.",
    # CleanupTests
    "AccountCleanup_AllTrackedAccounts_DeletesSuccessfully": "Deletes all tracked test accounts. Confirms cleanup is successful.",
    "AccountCleanup_WithCascadeData_DeletesEverything": "Deletes an account with associated data. Confirms cascade deletion works correctly.",
    "Backend_BulkCleanupEndpoint_IfImplemented": "Tests the bulk cleanup endpoint if available in the backend.",
    "Diagnostic_VerifyDeleteEndpoint_Works": "Verifies the delete endpoint is functioning correctly for diagnostic purposes.",
    "ManualCleanup_AnalyzeTestData": "Analyzes test data for manual cleanup. Reports on data requiring cleanup.",
    "ManualCleanup_DeleteAllTestData": "Deletes all test data for cleanup purposes. Confirms deletion is complete.",
    "ManualCleanup_DeleteOldTestData_7Days": "Removes test data older than 7 days. Confirms old data is cleaned up.",
    "ManualCleanup_DryRun": "Runs cleanup in dry-run mode. Reports what would be deleted without making changes.",
    "Manual_CleanupAllTrackedAccounts": "Manually triggers cleanup of all tracked accounts.",
    "Manual_CleanupOldAccounts": "Manually removes old test accounts created during testing.",
    "Manual_CleanupPersistentUserTests_ByPattern": "Cleans up tests matching specific patterns. Confirms targeted cleanup works.",

    # Integration Tests
    "CompleteTestAuthorJourney_RegisterLoginCreateTestAddQuestionsPublish": "Tests complete test author journey from registration to publishing. Registers a new user, logs in, creates a test with 3 questions, and verifies the test is publicly accessible to anonymous users.",
    "AuthorStudentLifecycle_AuthorCreatesStudentTakesAuthorViewsAnalytics": "Tests multi-user lifecycle with author and student interactions. Author creates a test, student takes it and submits an attempt, then author views analytics to confirm the attempt is recorded.",
    "CompanyWorkflow_AdminCreatesInvitesMemberTakesTest": "Tests complete company collaboration workflow. Admin creates a company, creates a company test with questions, invites a member, member accepts and takes the test, then admin views analytics showing the member's attempt.",
    "CrossCompanySecurity_UserBCannotAccessUserACompany": "Tests security boundaries between companies. User A creates a company and test, User B (not a member) attempts to access the company and test analytics, confirming both are properly denied.",
    "PermissionFlow_StudentCannotCreateCompanyTests": "Tests role-based permission enforcement. Admin creates a company and invites a student, student accepts the invite, then attempts to create a company test and is properly denied due to insufficient permissions.",
}

# ---------------------------------------------------------------------------
# Parse TRX
# ---------------------------------------------------------------------------
tree = ET.parse(TRX_PATH)
root = tree.getroot()

# --- run-level times ---
times_el  = root.find("ns:Times", NS)
run_start = parse_iso_datetime(times_el.get("start"))
run_finish = parse_iso_datetime(times_el.get("finish"))
run_duration_sec = (run_finish - run_start).total_seconds()

# --- counters (official summary) ---
counters = root.find(".//ns:Counters", NS)
total_tests = int(counters.get("total", "0"))
passed      = int(counters.get("passed", "0"))
failed      = int(counters.get("failed", "0"))
errors      = int(counters.get("error", "0"))
skipped     = int(counters.get("notExecuted", "0"))

# --- individual results ---
class Test:
    __slots__ = ("full_name", "class_name", "method_name", "outcome", "duration_sec", "description", "stdout")
    def __init__(self, full_name, outcome, duration_sec, description="", stdout=""):
        self.full_name    = full_name
        self.outcome      = outcome
        self.duration_sec = duration_sec
        self.description  = description
        self.stdout       = stdout
        # split into class + method at the last dot
        idx = full_name.rfind(".")
        if idx != -1:
            self.class_name  = full_name[:idx]
            self.method_name = full_name[idx+1:]
        else:
            self.class_name  = ""
            self.method_name = full_name

tests = []
for r in root.findall(".//ns:UnitTestResult", NS):
    # Extract execution log from Output/StdOut
    stdout_el = r.find(".//ns:Output/ns:StdOut", NS)
    stdout = stdout_el.text.strip() if stdout_el is not None and stdout_el.text else ""

    # Extract test name to look up description
    full_test_name = r.get("testName", "")
    # Extract method name from full name (last part after final dot)
    method_name = full_test_name.split(".")[-1] if "." in full_test_name else full_test_name
    test_description = TEST_DESCRIPTIONS.get(method_name, "")

    tests.append(Test(
        full_name    = full_test_name,
        outcome      = r.get("outcome", "Unknown"),
        duration_sec = parse_duration(r.get("duration", "00:00:00.0000000")),
        description  = test_description,
        stdout       = stdout
    ))

# --- group by class, preserving encounter order ---
classes: OrderedDict[str, list] = OrderedDict()
for t in tests:
    classes.setdefault(t.class_name, []).append(t)

# --- Sort classes in logical order (matching old report) ---
CLASS_ORDER = [
    "TestIT.ApiTests.Tests.AuthenticationTests",
    "TestIT.ApiTests.Tests.TestsManagementTests",
    "TestIT.ApiTests.Tests.QuestionManagementTests",
    "TestIT.ApiTests.Tests.TestTakingTests",
    "TestIT.ApiTests.Tests.CompanyTests",
    "TestIT.ApiTests.Tests.InviteTests",
    "TestIT.ApiTests.Tests.FolderTests",
    "TestIT.ApiTests.Tests.AnalyticsTests",
    "TestIT.ApiTests.Tests.SecurityTests",
    "TestIT.ApiTests.Tests.DataIntegrityTests",
    "TestIT.ApiTests.Tests.EdgeCaseTests",
    "TestIT.ApiTests.Tests.CoverageTests",
    "TestIT.ApiTests.Tests.PatchTests",
    "TestIT.ApiTests.Tests.PerformanceTests",
    "TestIT.ApiTests.Tests.SchemaValidationTests",
    "TestIT.ApiTests.Tests.StressTests",
    "TestIT.ApiTests.Tests.CleanupTests",
]

# Sort classes according to CLASS_ORDER
sorted_classes = OrderedDict()
for class_name in CLASS_ORDER:
    if class_name in classes:
        sorted_classes[class_name] = classes[class_name]
# Add any classes not in CLASS_ORDER at the end
for class_name, tests_list in classes.items():
    if class_name not in sorted_classes:
        sorted_classes[class_name] = tests_list
classes = sorted_classes

# --- overall stats ---
pass_rate = (passed / total_tests * 100) if total_tests else 0.0
all_passed = (failed + errors) == 0

slowest = max(tests, key=lambda t: t.duration_sec) if tests else None
fastest = min(tests, key=lambda t: t.duration_sec) if tests else None
avg_dur  = sum(t.duration_sec for t in tests) / len(tests) if tests else 0.0

# ---------------------------------------------------------------------------
# HTML generation
# ---------------------------------------------------------------------------

# Short class label: strip common prefix "TestIT.ApiTests.Tests."
CLASS_PREFIX = "TestIT.ApiTests.Tests."

def short_class(name: str) -> str:
    return name[len(CLASS_PREFIX):] if name.startswith(CLASS_PREFIX) else name

# Class-level descriptions
CLASS_DESCRIPTIONS = {
    "TestIT.ApiTests.Tests.AuthenticationTests": "Registration, login, token refresh and profile retrieval — covers the full authentication lifecycle and input-validation edge cases.",
    "TestIT.ApiTests.Tests.TestsManagementTests": "CRUD operations on quizzes: create, list, fetch by slug, update, delete and add questions — plus auth-enforcement checks.",
    "TestIT.ApiTests.Tests.QuestionManagementTests": "Editing, deleting and reordering questions within a test, including multi-select question creation.",
    "TestIT.ApiTests.Tests.TestTakingTests": "The end-to-end test-taking flow: public and password-protected access, attempt lifecycle, draft saving, scoring and results retrieval.",
    "TestIT.ApiTests.Tests.CompanyTests": "Company CRUD, member listing, company-scoped test listing and creation — plus an unauthenticated-access check.",
    "TestIT.ApiTests.Tests.InviteTests": "Invite lifecycle: creation, duplicate detection, listing and the full accept flow that converts a pending invite into membership.",
    "TestIT.ApiTests.Tests.FolderTests": "Folder CRUD within a company, including nested parent-child folder creation.",
    "TestIT.ApiTests.Tests.AnalyticsTests": "Per-test analytics: empty-state reporting, post-submission stats and access-control enforcement.",
    "TestIT.ApiTests.Tests.SecurityTests": "Input validation, SQL injection prevention, XSS protection, and authentication/authorization enforcement.",
    "TestIT.ApiTests.Tests.DataIntegrityTests": "Scoring accuracy, immutability checks, and data consistency validation.",
    "TestIT.ApiTests.Tests.EdgeCaseTests": "Boundary value testing, malformed input handling, and edge case scenarios.",
    "TestIT.ApiTests.Tests.CoverageTests": "API surface area validation and authorization checks across endpoints.",
    "TestIT.ApiTests.Tests.PatchTests": "PATCH endpoint testing for partial updates and field modifications.",
    "TestIT.ApiTests.Tests.PerformanceTests": "Response latency SLA tests measuring API performance across different operation types.",
    "TestIT.ApiTests.Tests.SchemaValidationTests": "API contract validation ensuring correct field types and absence of sensitive data leaks.",
    "TestIT.ApiTests.Tests.StressTests": "Load testing and concurrency validation under high request volumes.",
}

# Colour palette (OLD DESIGN - restored)
C_BG          = "#eef0f3"
C_WHITE       = "#ffffff"
C_TEXT        = "#333"
C_TEXT_LIGHT  = "#777"
C_BORDER      = "#ddd"
C_HEADER_BG   = "#1a237e"      # Dark blue header
C_GREEN_BG    = "#2e7d32"      # Green summary bar
C_GREEN_LIGHT = "#eafaf1"
C_GREEN_TEXT  = "#1e8449"
C_RED_BG      = "#c62828"      # Red for failures
C_RED_LIGHT   = "#fdedec"
C_RED_TEXT    = "#c0392b"
C_ACCENT      = "#1565c0"      # Blue accent for links

summary_bg  = C_GREEN_BG if all_passed else C_RED_BG
badge_failed_bg = C_RED_LIGHT
badge_passed_bg = C_GREEN_LIGHT

# --- display date/time in local offset kept from TRX ---
run_start_local = run_start.strftime("%Y-%m-%d %H:%M:%S %Z").replace("UTC", "").strip()
# If strftime gave an empty tz name, render the offset manually
if run_start.utcoffset() is not None:
    off = run_start.utcoffset()
    total_secs = int(off.total_seconds())
    sign = "+" if total_secs >= 0 else "-"
    total_secs = abs(total_secs)
    oh, om = divmod(total_secs // 60, 60)
    tz_str = f"UTC{sign}{oh:02d}:{om:02d}"
    run_start_local = run_start.strftime("%Y-%m-%d %H:%M:%S") + f" ({tz_str})"

# ---------------------------------------------------------------------------
# Build HTML string
# ---------------------------------------------------------------------------

def outcome_badge(outcome: str) -> str:
    if outcome == "Passed":
        return f'<span style="font-weight:600;color:{C_GREEN_BG};">{outcome}</span>'
    else:
        return f'<span style="font-weight:600;color:{C_RED_BG};">{html.escape(outcome)}</span>'


def class_table(class_index: int, class_name: str, test_list: list) -> str:
    short    = html.escape(short_class(class_name))
    count    = len(test_list)
    passed_count = sum(1 for t in test_list if t.outcome == "Passed")
    total_d  = sum(t.duration_sec for t in test_list)
    has_fail = any(t.outcome != "Passed" for t in test_list)
    header_color = C_RED_TEXT if has_fail else C_GREEN_TEXT
    header_bg    = C_RED_LIGHT if has_fail else C_GREEN_LIGHT

    rows = ""
    for i, t in enumerate(sorted(test_list, key=lambda x: x.method_name), start=1):
        # Test row with name, description, status, and duration
        rows += (
            f'<tr><td style="padding:10px 14px 4px;vertical-align:top;">'
            f'<div style="font-weight:600;"><span style="color:#546e7a;font-size:13px;margin-right:8px;">{class_index}.{i}</span>'
            f'{html.escape(t.method_name)}</div>'
        )

        # Add description if available
        if t.description:
            rows += f'<div style="font-size:13px;color:#666;margin-top:3px;">{html.escape(t.description)}</div>'

        rows += (
            f'</td><td style="padding:10px 14px 4px;vertical-align:top;font-weight:600;color:{C_GREEN_BG if t.outcome == "Passed" else C_RED_BG};white-space:nowrap;">'
            f'{html.escape(t.outcome)}</td>'
            f'<td style="padding:10px 14px 4px;vertical-align:top;text-align:right;color:#555;white-space:nowrap;">'
            f'{fmt_dur(t.duration_sec)}</td></tr>\n'
        )

        # Execution log row (if stdout exists)
        if t.stdout:
            rows += (
                f'<tr><td colspan="3" style="padding:0 14px 10px;">'
                f'<details><summary style="cursor:pointer;font-size:12px;color:#1565c0;font-weight:600;margin-top:4px;">'
                f'&#9654; Execution Log</summary>'
                f'<pre style="margin:6px 0 0;padding:10px 14px;background:#f4f6f8;border:1px solid #dde1e6;'
                f'border-radius:4px;font-size:11.5px;color:#37474f;line-height:1.8;overflow-x:auto;white-space:pre-wrap;">'
                f'{html.escape(t.stdout)}</pre></details></td></tr>'
            )

    result = (
        f'<details style="margin-bottom:10px;border:1px solid {C_BORDER};border-radius:6px;overflow:hidden;">\n'
        f'<summary style="cursor:pointer;background:#f5f5f5;padding:12px 16px;font-weight:600;font-size:15px;'
        f'display:flex;justify-content:space-between;align-items:center;list-style:none;-webkit-appearance:none;">\n'
        f'  <span>&#9654; <span style="color:#546e7a;">{class_index}.</span> {short}</span>\n'
        f'  <span style="font-weight:400;color:{C_TEXT_LIGHT};font-size:13px;">{passed_count}/{count} &nbsp;|&nbsp; {fmt_dur(total_d)}</span>\n'
        f'</summary>\n'
    )

    # Add class description if available
    class_desc = CLASS_DESCRIPTIONS.get(class_name, "")
    if class_desc:
        result += (
            f'<div style="padding:10px 16px 4px;background:#fff;border-bottom:1px solid #eee;">\n'
            f'  <p style="margin:0;font-size:13px;color:#555;font-style:italic;">{class_desc}</p>\n'
            f'</div>\n'
        )

    result += (
        f'<table style="width:100%;border-collapse:collapse;">\n'
        f'<thead><tr style="background:#fafafa;border-bottom:2px solid #e8e8e8;">\n'
        f'  <th style="padding:8px 14px;text-align:left;font-size:12px;color:#888;font-weight:600;'
        f'text-transform:uppercase;letter-spacing:.5px;">Test</th>\n'
        f'  <th style="padding:8px 14px;text-align:left;font-size:12px;color:#888;font-weight:600;'
        f'text-transform:uppercase;letter-spacing:.5px;">Result</th>\n'
        f'  <th style="padding:8px 14px;text-align:right;font-size:12px;color:#888;font-weight:600;'
        f'text-transform:uppercase;letter-spacing:.5px;">Duration</th>\n'
        f'</tr></thead>\n'
        f'<tbody>\n{rows}</tbody>\n'
        f'</table>\n'
        f'</details>\n'
    )

    return result


# ---- stat card helper ----
def stat_card(label, value, sub="", accent_color=C_ACCENT):
    return (
        f'<div style="background:{C_WHITE};border:1px solid {C_BORDER};border-radius:10px;'
        f'padding:18px 20px;flex:1 1 140px;max-width:240px;text-align:center;'
        f'box-shadow:0 1px 3px rgba(0,0,0,0.06);">\n'
        f'  <div style="font-size:11px;text-transform:uppercase;letter-spacing:1px;'
        f'color:{C_TEXT_LIGHT};margin-bottom:6px;">{label}</div>\n'
        f'  <div style="font-size:26px;font-weight:700;color:{accent_color};">{value}</div>\n'
        + (f'  <div style="font-size:12px;color:{C_TEXT_LIGHT};margin-top:4px;">{sub}</div>\n' if sub else "")
        + f'</div>\n'
    )


# ---- main HTML ----
html_doc = f"""<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8" />
<meta name="viewport" content="width=device-width, initial-scale=1" />
<title>TestIT API Tests &#8212; Test Report</title>
<style>
  /* reset details/summary arrow across browsers */
  details > summary::-webkit-details-marker {{ display:none; }}
  details > summary {{ list-style:none; }}
  details[open] > summary > span:first-child {{ }}
</style>
</head>
<body style="margin:0;padding:0;background:{C_BG};font-family:'Segoe UI',system-ui,Arial,sans-serif;color:{C_TEXT};">

<!-- ============================================================
     HEADER (DARK BLUE)
     ============================================================ -->
<div style="background:{C_HEADER_BG};color:#fff;padding:26px 40px 22px;">
  <h1 style="margin:0 0 6px;font-size:22px;font-weight:600;letter-spacing:.3px;">
    TestIT API Tests &#8212; Test Report
  </h1>
  <p style="margin:0;font-size:13px;opacity:.7;">
    Started: {run_start.strftime("%Y-%m-%d %H:%M:%S")} &nbsp;|&nbsp; Finished: {run_finish.strftime("%Y-%m-%d %H:%M:%S")}
  </p>
</div>

<!-- ============================================================
     SUMMARY BAR (GREEN/RED)
     ============================================================ -->
<div style="background:{summary_bg};color:#fff;padding:18px 40px;display:flex;gap:52px;flex-wrap:wrap;align-items:center;">
  <div style="text-align:center;">
    <div style="font-size:38px;font-weight:700;line-height:1;">{passed}/{total_tests}</div>
    <div style="font-size:12px;opacity:.85;margin-top:2px;text-transform:uppercase;letter-spacing:.8px;">Tests Passed</div>
  </div>
  <div style="text-align:center;">
    <div style="font-size:38px;font-weight:700;line-height:1;">{pass_rate:.0f}%</div>
    <div style="font-size:12px;opacity:.85;margin-top:2px;text-transform:uppercase;letter-spacing:.8px;">Pass Rate</div>
  </div>
  <div style="text-align:center;">
    <div style="font-size:38px;font-weight:700;line-height:1;">{fmt_dur(run_duration_sec)}</div>
    <div style="font-size:12px;opacity:.85;margin-top:2px;text-transform:uppercase;letter-spacing:.8px;">Total Duration</div>
  </div>
  <div style="text-align:center;">
    <div style="font-size:38px;font-weight:700;line-height:1;">{fmt_dur(run_duration_sec / total_tests if total_tests > 0 else 0)}</div>
    <div style="font-size:12px;opacity:.85;margin-top:2px;text-transform:uppercase;letter-spacing:.8px;">Avg / Test</div>
  </div>
</div>

<!-- ============================================================
     BODY / PER-CLASS BREAKDOWN
     ============================================================ -->
<div style="max-width:960px;margin:30px auto;padding:0 20px;">
  <h2 style="font-size:17px;color:{C_HEADER_BG};margin-bottom:14px;">
    Test Classes
  </h2>
  {"".join(class_table(idx, cls, tsts) for idx, (cls, tsts) in enumerate(classes.items(), start=1))}
</div>

<!-- ============================================================
     OVERALL STATS
     ============================================================ -->
<div style="max-width:960px;margin:32px auto 40px;padding:0 24px;">
  <h2 style="font-size:18px;font-weight:600;color:{C_TEXT};margin:0 0 16px;">
    Overall Stats
  </h2>
  <div style="display:flex;gap:16px;flex-wrap:wrap;">
    {stat_card("Slowest Test",
               fmt_dur(slowest.duration_sec) if slowest else "—",
               html.escape(slowest.method_name) if slowest else "",
               C_RED_TEXT)}
    {stat_card("Fastest Test",
               fmt_dur(fastest.duration_sec) if fastest else "—",
               html.escape(fastest.method_name) if fastest else "",
               C_GREEN_TEXT)}
    {stat_card("Avg Duration",
               fmt_dur(avg_dur),
               f"across {len(tests)} tests",
               C_ACCENT)}
  </div>
</div>

<!-- ============================================================
     FOOTER
     ============================================================ -->
<div style="border-top:1px solid {C_BORDER};padding:16px 0;text-align:center;">
  <span style="font-size:12px;color:{C_TEXT_LIGHT};">
    Report auto-generated &middot; source: results.trx
  </span>
</div>

</body>
</html>
"""

# ---------------------------------------------------------------------------
# Write
# ---------------------------------------------------------------------------
with open(HTML_PATH, "w", encoding="utf-8") as fh:
    fh.write(html_doc)

print(f"Report written to: {HTML_PATH}")
