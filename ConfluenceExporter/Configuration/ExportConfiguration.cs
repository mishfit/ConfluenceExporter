namespace ConfluenceExporter.Configuration;

public class ExportConfiguration
{
    public string ConfluenceBaseUrl { get; set; } = string.Empty;
    public string ApiToken { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string OutputDirectory { get; set; } = "output";
    public bool IncludeImages { get; set; } = true;
    public bool IncludeAttachments { get; set; } = true;
    public int MaxConcurrentRequests { get; set; } = 5;
    public int RequestDelayMs { get; set; } = 100;
    public ExportFormat Format { get; set; } = ExportFormat.Markdown;
    public bool PreserveHierarchy { get; set; } = true;
    public bool CreateIndexFile { get; set; } = true;
    public string[] ExcludedSpaces { get; set; } = Array.Empty<string>();
    public string[] IncludedSpaces { get; set; } = Array.Empty<string>();
}

public enum ExportFormat
{
    Markdown,
    Html,
    Both
}

public enum ExportScope
{
    Page,
    Space,
    Hierarchy,
    AllSpaces
}