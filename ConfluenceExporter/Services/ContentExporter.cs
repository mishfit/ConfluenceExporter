using System.Text.RegularExpressions;
using ConfluenceExporter.Configuration;
using ConfluenceExporter.Models;
using Microsoft.Extensions.Logging;
using ReverseMarkdown;

namespace ConfluenceExporter.Services;

public interface IContentExporter
{
    Task ExportPageAsync(ConfluencePage page, string outputPath, CancellationToken cancellationToken = default);
    Task ExportSpaceAsync(string spaceKey, CancellationToken cancellationToken = default);
    Task ExportPageHierarchyAsync(string pageId, CancellationToken cancellationToken = default);
    Task ExportAllSpacesAsync(CancellationToken cancellationToken = default);
}

public class ContentExporter : IContentExporter
{
    private readonly IConfluenceApiClient _apiClient;
    private readonly IMarkdownConverter _markdownConverter;
    private readonly ILogger<ContentExporter> _logger;
    private readonly ExportConfiguration _config;
    private readonly Converter _reverseMarkdownConverter;

    public ContentExporter(
        IConfluenceApiClient apiClient, 
        IMarkdownConverter markdownConverter,
        ILogger<ContentExporter> logger, 
        ExportConfiguration config)
    {
        _apiClient = apiClient;
        _markdownConverter = markdownConverter;
        _logger = logger;
        _config = config;
        
        _reverseMarkdownConverter = new Converter(new Config
        {
            UnknownTags = Config.UnknownTagsOption.Bypass,
            GithubFlavored = true,
            RemoveComments = true,
            SmartHrefHandling = true
        });
    }

    public async Task ExportPageAsync(ConfluencePage page, string outputPath, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Exporting page: {Title} (ID: {Id})", page.Title, page.Id);

        var sanitizedTitle = SanitizeFileName(page.Title);
        var pageDirectory = Path.Combine(outputPath, sanitizedTitle);
        
        if (_config.PreserveHierarchy && page.Ancestors.Any())
        {
            var hierarchyPath = string.Join(Path.DirectorySeparatorChar, 
                page.Ancestors.Select(a => SanitizeFileName(a.Title)));
            pageDirectory = Path.Combine(outputPath, hierarchyPath, sanitizedTitle);
        }

        Directory.CreateDirectory(pageDirectory);

        if (_config.Format == ExportFormat.Markdown || _config.Format == ExportFormat.Both)
        {
            await ExportPageAsMarkdownAsync(page, pageDirectory, cancellationToken);
        }

        if (_config.Format == ExportFormat.Html || _config.Format == ExportFormat.Both)
        {
            await ExportPageAsHtmlAsync(page, pageDirectory, cancellationToken);
        }

        if (_config.IncludeImages || _config.IncludeAttachments)
        {
            await DownloadPageAssetsAsync(page, pageDirectory, cancellationToken);
        }
    }

    public async Task ExportSpaceAsync(string spaceKey, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Exporting space: {SpaceKey}", spaceKey);

        var space = await _apiClient.GetSpaceAsync(spaceKey, cancellationToken);
        if (space == null)
        {
            _logger.LogWarning("Space {SpaceKey} not found", spaceKey);
            return;
        }

        var spaceDirectory = Path.Combine(_config.OutputDirectory, SanitizeFileName(space.Name));
        Directory.CreateDirectory(spaceDirectory);

        var pages = await _apiClient.GetSpacePagesAsync(spaceKey, cancellationToken);
        var semaphore = new SemaphoreSlim(_config.MaxConcurrentRequests);
        var tasks = new List<Task>();

        foreach (var page in pages)
        {
            tasks.Add(ProcessPageWithSemaphore(page, spaceDirectory, semaphore, cancellationToken));
        }

        await Task.WhenAll(tasks);

        if (_config.CreateIndexFile)
        {
            await CreateSpaceIndexAsync(space, pages, spaceDirectory);
        }

        _logger.LogInformation("Completed exporting space: {SpaceKey}", spaceKey);
    }

    public async Task ExportPageHierarchyAsync(string pageId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Exporting page hierarchy starting from: {PageId}", pageId);

        var rootPage = await _apiClient.GetPageAsync(pageId, cancellationToken);
        if (rootPage == null)
        {
            _logger.LogWarning("Page {PageId} not found", pageId);
            return;
        }

        var hierarchyPages = await _apiClient.GetPageHierarchyAsync(pageId, cancellationToken);
        var spaceDirectory = Path.Combine(_config.OutputDirectory, 
            SanitizeFileName(rootPage.Space?.Name ?? "Unknown Space"));
        
        Directory.CreateDirectory(spaceDirectory);

        var semaphore = new SemaphoreSlim(_config.MaxConcurrentRequests);
        var tasks = new List<Task>();

        foreach (var page in hierarchyPages)
        {
            tasks.Add(ProcessPageWithSemaphore(page, spaceDirectory, semaphore, cancellationToken));
        }

        await Task.WhenAll(tasks);

        if (_config.CreateIndexFile)
        {
            await CreateHierarchyIndexAsync(rootPage, hierarchyPages, spaceDirectory);
        }

        _logger.LogInformation("Completed exporting page hierarchy: {PageId}", pageId);
    }

    public async Task ExportAllSpacesAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Exporting all spaces");

        var spaces = await _apiClient.GetSpacesAsync(cancellationToken);
        var filteredSpaces = FilterSpaces(spaces);

        var tasks = new List<Task>();
        foreach (var space in filteredSpaces)
        {
            tasks.Add(ExportSpaceAsync(space.Key, cancellationToken));
        }

        await Task.WhenAll(tasks);
        
        if (_config.CreateIndexFile)
        {
            await CreateGlobalIndexAsync(filteredSpaces);
        }

        _logger.LogInformation("Completed exporting all spaces");
    }

    private async Task ProcessPageWithSemaphore(ConfluencePage page, string outputPath, SemaphoreSlim semaphore, CancellationToken cancellationToken)
    {
        await semaphore.WaitAsync(cancellationToken);
        try
        {
            await ExportPageAsync(page, outputPath, cancellationToken);
            await Task.Delay(_config.RequestDelayMs, cancellationToken);
        }
        finally
        {
            semaphore.Release();
        }
    }

    private async Task ExportPageAsMarkdownAsync(ConfluencePage page, string pageDirectory, CancellationToken cancellationToken)
    {
        var markdownContent = await _markdownConverter.ConvertToMarkdownAsync(page.Body?.Storage?.Value ?? "", cancellationToken);
        
        var metadata = CreateMarkdownMetadata(page);
        var fullContent = $"{metadata}\n\n{markdownContent}";

        var filePath = Path.Combine(pageDirectory, "README.md");
        await File.WriteAllTextAsync(filePath, fullContent, cancellationToken);
    }

    private async Task ExportPageAsHtmlAsync(ConfluencePage page, string pageDirectory, CancellationToken cancellationToken)
    {
        var htmlContent = page.Body?.Storage?.Value ?? "";
        var metadata = CreateHtmlMetadata(page);
        
        var fullContent = $@"<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"">
    <title>{page.Title}</title>
    {metadata}
</head>
<body>
    <h1>{page.Title}</h1>
    {htmlContent}
</body>
</html>";

        var filePath = Path.Combine(pageDirectory, "index.html");
        await File.WriteAllTextAsync(filePath, fullContent, cancellationToken);
    }

    private async Task DownloadPageAssetsAsync(ConfluencePage page, string pageDirectory, CancellationToken cancellationToken)
    {
        var assetsDirectory = Path.Combine(pageDirectory, "assets");
        
        var htmlContent = page.Body?.Storage?.Value ?? "";
        var imageUrls = ExtractImageUrls(htmlContent);
        var attachmentUrls = ExtractAttachmentUrls(htmlContent);

        if (imageUrls.Any() || attachmentUrls.Any())
        {
            Directory.CreateDirectory(assetsDirectory);
        }

        foreach (var imageUrl in imageUrls.Take(10)) // Limit to prevent abuse
        {
            try
            {
                var fileName = Path.GetFileName(new Uri(imageUrl).LocalPath);
                var filePath = Path.Combine(assetsDirectory, SanitizeFileName(fileName));
                
                var imageData = await _apiClient.GetAttachmentAsync(imageUrl, cancellationToken);
                await File.WriteAllBytesAsync(filePath, imageData, cancellationToken);
                
                _logger.LogDebug("Downloaded image: {FileName}", fileName);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to download image: {Url}", imageUrl);
            }
        }
    }

    private static string CreateMarkdownMetadata(ConfluencePage page)
    {
        return $@"---
title: {page.Title}
id: {page.Id}
type: {page.Type}
status: {page.Status}
space: {page.Space?.Name ?? ""}
version: {page.Version?.Number ?? 0}
last_modified: {page.Version?.When:yyyy-MM-dd HH:mm:ss}
---";
    }

    private static string CreateHtmlMetadata(ConfluencePage page)
    {
        return $@"    <meta name=""confluence-page-id"" content=""{page.Id}"">
    <meta name=""confluence-space"" content=""{page.Space?.Name ?? ""}"">
    <meta name=""confluence-version"" content=""{page.Version?.Number ?? 0}"">
    <meta name=""last-modified"" content=""{page.Version?.When:yyyy-MM-dd HH:mm:ss}"">";
    }

    private static List<string> ExtractImageUrls(string htmlContent)
    {
        var regex = new Regex(@"<img[^>]+src=""([^""]+)""", RegexOptions.IgnoreCase);
        return regex.Matches(htmlContent)
            .Cast<Match>()
            .Select(m => m.Groups[1].Value)
            .Where(url => !string.IsNullOrEmpty(url))
            .ToList();
    }

    private static List<string> ExtractAttachmentUrls(string htmlContent)
    {
        var regex = new Regex(@"<a[^>]+href=""([^""]+/download/[^""]+)""", RegexOptions.IgnoreCase);
        return regex.Matches(htmlContent)
            .Cast<Match>()
            .Select(m => m.Groups[1].Value)
            .Where(url => !string.IsNullOrEmpty(url))
            .ToList();
    }

    private List<ConfluenceSpace> FilterSpaces(List<ConfluenceSpace> spaces)
    {
        var filtered = spaces.AsEnumerable();

        if (_config.IncludedSpaces.Any())
        {
            filtered = filtered.Where(s => _config.IncludedSpaces.Contains(s.Key, StringComparer.OrdinalIgnoreCase));
        }

        if (_config.ExcludedSpaces.Any())
        {
            filtered = filtered.Where(s => !_config.ExcludedSpaces.Contains(s.Key, StringComparer.OrdinalIgnoreCase));
        }

        return filtered.ToList();
    }

    private async Task CreateSpaceIndexAsync(ConfluenceSpace space, List<ConfluencePage> pages, string spaceDirectory)
    {
        var indexContent = $@"# {space.Name}

**Space Key:** {space.Key}  
**Space ID:** {space.Id}  
**Type:** {space.Type}  
**Status:** {space.Status}  

## Pages ({pages.Count})

";

        foreach (var page in pages.OrderBy(p => p.Title))
        {
            var sanitizedTitle = SanitizeFileName(page.Title);
            indexContent += $"- [{page.Title}](./{sanitizedTitle}/README.md)\n";
        }

        var indexPath = Path.Combine(spaceDirectory, "INDEX.md");
        await File.WriteAllTextAsync(indexPath, indexContent);
    }

    private async Task CreateHierarchyIndexAsync(ConfluencePage rootPage, List<ConfluencePage> pages, string spaceDirectory)
    {
        var indexContent = $@"# {rootPage.Title} - Page Hierarchy

**Root Page ID:** {rootPage.Id}  
**Space:** {rootPage.Space?.Name}  

## Pages in Hierarchy ({pages.Count})

";

        foreach (var page in pages.OrderBy(p => p.Title))
        {
            var sanitizedTitle = SanitizeFileName(page.Title);
            var indent = new string(' ', page.Ancestors.Count * 2);
            indexContent += $"{indent}- [{page.Title}](./{sanitizedTitle}/README.md)\n";
        }

        var indexPath = Path.Combine(spaceDirectory, "HIERARCHY_INDEX.md");
        await File.WriteAllTextAsync(indexPath, indexContent);
    }

    private async Task CreateGlobalIndexAsync(List<ConfluenceSpace> spaces)
    {
        var indexContent = $@"# Confluence Export

**Export Date:** {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC  
**Spaces Exported:** {spaces.Count}  

## Spaces

";

        foreach (var space in spaces.OrderBy(s => s.Name))
        {
            var sanitizedName = SanitizeFileName(space.Name);
            indexContent += $"- [{space.Name}](./{sanitizedName}/INDEX.md) (`{space.Key}`)\n";
        }

        var indexPath = Path.Combine(_config.OutputDirectory, "README.md");
        await File.WriteAllTextAsync(indexPath, indexContent);
    }

    private static string SanitizeFileName(string fileName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = string.Join("_", fileName.Split(invalid, StringSplitOptions.RemoveEmptyEntries));
        return sanitized.Length > 100 ? sanitized[..100] : sanitized;
    }
}