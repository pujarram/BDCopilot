namespace BDCopilot.Core.Models;

public class LocalDocsSyncResult
{
    public required string RootPath { get; set; }
    public bool RootExists { get; set; }
    public int FilesFound { get; set; }
    public int Indexed { get; set; }
    public int SkippedUnchanged { get; set; }
    public int Failed { get; set; }
    public List<string> Errors { get; set; } = [];
    public string StatusMessage { get; set; } = "";
}
