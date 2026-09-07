namespace BDCopilot.Core.Models;

/// <summary>
/// Persisted RFP draft history — section text stored in columns (not as a Word file).
/// </summary>
public class RfpDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid GenerationId { get; set; }

    public required string Title { get; set; }

    public required string Customer { get; set; }

    public string Tone { get; set; } = "Formal";

    public string Status { get; set; } = "Draft"; // Draft | Approved | Exported | SavedToChannel

    public required string CreatedByUserObjectId { get; set; }

    public string? CreatedByDisplayName { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? UpdatedAt { get; set; }

    public string? ExecutiveSummary { get; set; }

    public string? UnderstandingOfRequirements { get; set; }

    public string? ProposedSolutionArchitecture { get; set; }

    public string? SecurityCompliance { get; set; }

    public string? DeliveryTimelineTeam { get; set; }

    public string? CommercialsPricing { get; set; }

    /// <summary>SharePoint / Teams channel URL after "Save to BD channel".</summary>
    public string? ChannelSharePointUrl { get; set; }

    public string? ChannelUploadStatus { get; set; } // Pending | Uploaded | Failed | SkippedNoGraph

    public string? ChannelUploadError { get; set; }

    /// <summary>Open | Win | Loss — Phase 6 win/loss tagging.</summary>
    public string Outcome { get; set; } = "Open";

    public string? OutcomeNotes { get; set; }

    public Guid? OpportunityId { get; set; }

    public DateTimeOffset? OutcomeTaggedAt { get; set; }
}

public class SaveRfpDocumentRequest
{
    public required GeneratedDocument Document { get; set; }
    public required string UserObjectId { get; set; }
    public string? DisplayName { get; set; }
    public string? Customer { get; set; }
    public string? Tone { get; set; }
}

public class SaveRfpToChannelRequest
{
    public Guid RfpDocumentId { get; set; }
    public required string UserObjectId { get; set; }
}

public class RfpDocumentListItem
{
    public Guid Id { get; set; }
    public Guid GenerationId { get; set; }
    public required string Title { get; set; }
    public required string Customer { get; set; }
    public string Status { get; set; } = "Draft";
    public required string CreatedByUserObjectId { get; set; }
    public string? CreatedByDisplayName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string? ChannelUploadStatus { get; set; }
    public string? ChannelSharePointUrl { get; set; }
    public string Outcome { get; set; } = "Open";
    public Guid? OpportunityId { get; set; }
}

public class AdminLoginRequest
{
    public required string Username { get; set; }
    public required string Password { get; set; }
}

public class AdminLoginResponse
{
    public required string Token { get; set; }
    public required string Username { get; set; }
    public required string DisplayName { get; set; }
    public required string Role { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}

public class AdminUserSettings
{
    public const string SectionName = "AdminAuth";
    public List<AdminUserAccount> Users { get; set; } = new();
}

public class AdminUserAccount
{
    public required string Username { get; set; }
    public required string Password { get; set; }
    public string DisplayName { get; set; } = "Admin";
    public string Role { get; set; } = "Admin";
}
