using System.Text.Json.Serialization;

namespace TestIT.ApiTests.Models;

// --- Request Models ---

public class CreateCompanyRequest
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

public class UpdateCompanyRequest
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

public class UpdateMemberRoleRequest
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;
}

public class CreateInviteRequest
{
    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("role")]
    public string Role { get; set; } = "student";
}

public class CreateFolderRequest
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("parent")]
    public int? Parent { get; set; }
}

public class UpdateFolderRequest
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("parent")]
    public int? Parent { get; set; }
}

public class CreateCompanyTestRequest
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("folder")]
    public int? Folder { get; set; }

    [JsonPropertyName("time_limit_minutes")]
    public int? TimeLimitMinutes { get; set; }

    [JsonPropertyName("max_attempts")]
    public int MaxAttempts { get; set; } = 1;

    [JsonPropertyName("show_answers_after")]
    public bool ShowAnswersAfter { get; set; }
}

// --- Response Models ---

public class CompanyResponse
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }
}

public class MemberResponse
{
    [JsonPropertyName("user_id")]
    public int UserId { get; set; }

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("first_name")]
    public string FirstName { get; set; } = string.Empty;

    [JsonPropertyName("last_name")]
    public string LastName { get; set; } = string.Empty;

    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;
}

public class InviteResponse
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("token")]
    public string Token { get; set; } = string.Empty;

    [JsonPropertyName("accepted")]
    public bool IsAccepted { get; set; }
}

public class FolderResponse
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("parent")]
    public int? Parent { get; set; }

    [JsonPropertyName("company")]
    public int Company { get; set; }
}
