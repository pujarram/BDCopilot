namespace BDCopilot.Infrastructure.Services;

/// <summary>
/// Folder of RFP / proposal / knowledge files on disk used when corpus source = Local.
/// Default resolves to the repo <c>docs</c> folder (sibling of <c>backend</c>).
/// </summary>
public class LocalDocsSettings
{
    public const string SectionName = "LocalDocs";

    /// <summary>
    /// Absolute path, or path relative to the API content root.
    /// Empty = auto-resolve to <c>../../../../docs</c> from the API project (repo docs/).
    /// </summary>
    public string RootPath { get; set; } = "";

    /// <summary>When true, index local docs once at API startup.</summary>
    public bool IndexOnStartup { get; set; } = true;

    public string[] ExcludeFolderNames { get; set; } =
    [
        "teams-app",
        "node_modules",
        ".git",
        "bin",
        "obj"
    ];

    public string[] Extensions { get; set; } =
    [
        ".docx", ".pdf", ".pptx", ".xlsx", ".txt", ".md"
    ];
}
