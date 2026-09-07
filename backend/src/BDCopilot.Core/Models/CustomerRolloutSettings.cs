namespace BDCopilot.Core.Models;

/// <summary>Identifies the deployed customer tenant for rollout tracking and go-live gates.</summary>
public class CustomerRolloutSettings
{
    public const string SectionName = "Customer";

    public string Code { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string ProfilePath { get; set; } = "docs/azure/customers/customer.template.json";
    public string? GoLiveTargetDate { get; set; }
    public string? DemoOwner { get; set; }
}
