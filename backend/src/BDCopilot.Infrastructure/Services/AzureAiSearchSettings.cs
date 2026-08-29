namespace BDCopilot.Infrastructure.Services;

public class AzureAiSearchSettings
{
    public const string SectionName = "AzureSearch";

    public string Endpoint { get; set; } = "";
    public string IndexName { get; set; } = "bdcopilot-chunks";
    public string ApiKey { get; set; } = "";
    public int DefaultTopK { get; set; } = 8;
}
