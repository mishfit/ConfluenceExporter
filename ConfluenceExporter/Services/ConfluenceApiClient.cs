using System.Net.Http.Headers;
using System.Text;
using System.Linq;
using System.Web;
using ConfluenceExporter.Configuration;
using ConfluenceExporter.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace ConfluenceExporter.Services;

public interface IConfluenceApiClient
{
    Task<List<ConfluenceSpace>> GetSpacesAsync(CancellationToken cancellationToken = default);
    Task<ConfluenceSpace?> GetSpaceAsync(string spaceKey, CancellationToken cancellationToken = default);
    Task<List<ConfluencePage>> GetSpacePagesAsync(string spaceKey, CancellationToken cancellationToken = default);
    Task<ConfluencePage?> GetPageAsync(string pageId, CancellationToken cancellationToken = default);
    Task<List<ConfluencePage>> GetPageChildrenAsync(string pageId, CancellationToken cancellationToken = default);
    Task<List<ConfluencePage>> GetPageHierarchyAsync(string pageId, CancellationToken cancellationToken = default);
    Task<byte[]> GetAttachmentAsync(string attachmentUrl, CancellationToken cancellationToken = default);
}

public class ConfluenceApiClient : IConfluenceApiClient, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ConfluenceApiClient> _logger;
    private readonly ExportConfiguration _config;

    public ConfluenceApiClient(HttpClient httpClient, ILogger<ConfluenceApiClient> logger, ExportConfiguration config)
    {
        _httpClient = httpClient;
        _logger = logger;
        _config = config;

        SetupHttpClient();
    }

    private void SetupHttpClient()
    {
        _httpClient.BaseAddress = new Uri(_config.ConfluenceBaseUrl);
        
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_config.Username}:{_config.ApiToken}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        
        _httpClient.DefaultRequestHeaders.Accept.Clear();
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        
        _httpClient.Timeout = TimeSpan.FromMinutes(5);
    }

    public async Task<List<ConfluenceSpace>> GetSpacesAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Fetching all spaces");
        
        var spaces = new List<ConfluenceSpace>();
        string? cursor = null;
        const int limit = 50;
        
        while (true)
        {
            var url = $"/wiki/api/v2/spaces?limit={limit}";
            if (!string.IsNullOrEmpty(cursor))
                url += $"&cursor={cursor}";
                
            var response = await _httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();
            
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonConvert.DeserializeObject<ConfluenceSpacesResult>(content);
            
            if (result?.Results == null || result.Results.Count == 0)
                break;
                
            spaces.AddRange(result.Results);
            
            if (result.Results.Count < limit || result.Links?.Next == null)
                break;
                
            // Extract cursor from next URL if present
            cursor = ExtractCursorFromUrl(result.Links.Next);
        }
        
        _logger.LogInformation("Found {Count} spaces", spaces.Count);
        return spaces;
    }

    private static string? ExtractCursorFromUrl(string? nextUrl)
    {
        if (string.IsNullOrEmpty(nextUrl))
            return null;
            
        var uri = new Uri(nextUrl, UriKind.RelativeOrAbsolute);
        var query = uri.IsAbsoluteUri ? uri.Query : nextUrl.Contains('?') ? nextUrl.Substring(nextUrl.IndexOf('?')) : "";
        
        if (string.IsNullOrEmpty(query))
            return null;
            
        var queryParams = System.Web.HttpUtility.ParseQueryString(query);
        return queryParams["cursor"];
    }

    public async Task<ConfluenceSpace?> GetSpaceAsync(string spaceKey, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Fetching space: {SpaceKey}", spaceKey);
        
        // First, get all spaces and find the one with matching key
        var spaces = await GetSpacesAsync(cancellationToken);
        var space = spaces.FirstOrDefault(s => s.Key.Equals(spaceKey, StringComparison.OrdinalIgnoreCase));
        
        return space;
    }

    public async Task<List<ConfluencePage>> GetSpacePagesAsync(string spaceKey, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Fetching pages for space: {SpaceKey}", spaceKey);
        
        // First get the space to get its ID
        var space = await GetSpaceAsync(spaceKey, cancellationToken);
        if (space == null)
        {
            _logger.LogWarning("Space {SpaceKey} not found", spaceKey);
            return new List<ConfluencePage>();
        }
        
        var pages = new List<ConfluencePage>();
        string? cursor = null;
        const int limit = 50;
        
        while (true)
        {
            var url = $"/wiki/api/v2/pages?space-id={space.Id}&limit={limit}&body-format=storage";
            if (!string.IsNullOrEmpty(cursor))
                url += $"&cursor={cursor}";
                
            var response = await _httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();
            
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonConvert.DeserializeObject<ConfluenceV2PagesResult>(content);
            
            if (result?.Results == null || result.Results.Count == 0)
                break;
                
            pages.AddRange(result.Results);
            
            if (result.Results.Count < limit || result.Links?.Next == null)
                break;
                
            cursor = ExtractCursorFromUrl(result.Links.Next);
            
            await Task.Delay(_config.RequestDelayMs, cancellationToken);
        }
        
        _logger.LogInformation("Found {Count} pages in space {SpaceKey}", pages.Count, spaceKey);
        return pages;
    }

    public async Task<ConfluencePage?> GetPageAsync(string pageId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Fetching page: {PageId}", pageId);
        
        var url = $"/wiki/api/v2/pages/{pageId}?body-format=storage&include-labels=false";
        var response = await _httpClient.GetAsync(url, cancellationToken);
        
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
            
        response.EnsureSuccessStatusCode();
        
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        var page = JsonConvert.DeserializeObject<ConfluencePage>(content);
        
        return page;
    }

    public async Task<List<ConfluencePage>> GetPageChildrenAsync(string pageId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Fetching children for page: {PageId}", pageId);
        
        var children = new List<ConfluencePage>();
        string? cursor = null;
        const int limit = 50;
        
        while (true)
        {
            var url = $"/wiki/api/v2/pages/{pageId}/children?limit={limit}";
            if (!string.IsNullOrEmpty(cursor))
                url += $"&cursor={cursor}";
                
            var response = await _httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();
            
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonConvert.DeserializeObject<ConfluenceV2ChildrenResult>(content);
            
            if (result?.Results == null || result.Results.Count == 0)
                break;
            
            // Convert ConfluenceChild objects to ConfluencePage objects
            // We'll need to fetch full page data for each child
            foreach (var child in result.Results.Where(c => c.Type == "page"))
            {
                var fullPage = await GetPageAsync(child.Id, cancellationToken);
                if (fullPage != null)
                {
                    children.Add(fullPage);
                }
            }
            
            if (result.Results.Count < limit || result.Links?.Next == null)
                break;
                
            cursor = ExtractCursorFromUrl(result.Links.Next);
            
            await Task.Delay(_config.RequestDelayMs, cancellationToken);
        }
        
        _logger.LogInformation("Found {Count} children for page {PageId}", children.Count, pageId);
        return children;
    }

    public async Task<List<ConfluencePage>> GetPageHierarchyAsync(string pageId, CancellationToken cancellationToken = default)
    {
        var allPages = new List<ConfluencePage>();
        var visited = new HashSet<string>();
        
        await GetPageHierarchyRecursive(pageId, allPages, visited, cancellationToken);
        
        return allPages;
    }

    private async Task GetPageHierarchyRecursive(string pageId, List<ConfluencePage> allPages, HashSet<string> visited, CancellationToken cancellationToken)
    {
        if (visited.Contains(pageId))
            return;
            
        visited.Add(pageId);
        
        var page = await GetPageAsync(pageId, cancellationToken);
        if (page != null)
        {
            allPages.Add(page);
            
            var children = await GetPageChildrenAsync(pageId, cancellationToken);
            foreach (var child in children)
            {
                await GetPageHierarchyRecursive(child.Id, allPages, visited, cancellationToken);
            }
        }
    }

    public async Task<byte[]> GetAttachmentAsync(string attachmentUrl, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Downloading attachment: {Url}", attachmentUrl);
        
        var response = await _httpClient.GetAsync(attachmentUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
        GC.SuppressFinalize(this);
    }
}