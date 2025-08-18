using System.Net.Http.Headers;
using System.Text;
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
        var start = 0;
        const int limit = 50;
        
        while (true)
        {
            var url = $"/rest/api/space?start={start}&limit={limit}&expand=permissions";
            var response = await _httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();
            
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonConvert.DeserializeObject<ConfluenceSearchResult>(content);
            
            if (result?.Results == null || result.Results.Count == 0)
                break;
                
            spaces.AddRange(result.Results.Select(r => new ConfluenceSpace
            {
                Id = r.Id,
                Key = r.Space?.Key ?? "",
                Name = r.Title,
                Type = r.Type,
                Status = r.Status
            }));
            
            if (result.Results.Count < limit)
                break;
                
            start += limit;
        }
        
        _logger.LogInformation("Found {Count} spaces", spaces.Count);
        return spaces;
    }

    public async Task<ConfluenceSpace?> GetSpaceAsync(string spaceKey, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Fetching space: {SpaceKey}", spaceKey);
        
        var url = $"/rest/api/space/{spaceKey}?expand=permissions";
        var response = await _httpClient.GetAsync(url, cancellationToken);
        
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
            
        response.EnsureSuccessStatusCode();
        
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        var space = JsonConvert.DeserializeObject<ConfluenceSpace>(content);
        
        return space;
    }

    public async Task<List<ConfluencePage>> GetSpacePagesAsync(string spaceKey, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Fetching pages for space: {SpaceKey}", spaceKey);
        
        var pages = new List<ConfluencePage>();
        var start = 0;
        const int limit = 50;
        
        while (true)
        {
            var url = $"/rest/api/space/{spaceKey}/content?start={start}&limit={limit}&expand=body.storage,version,space,ancestors";
            var response = await _httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();
            
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonConvert.DeserializeObject<ConfluenceSearchResult>(content);
            
            if (result?.Results == null || result.Results.Count == 0)
                break;
                
            pages.AddRange(result.Results);
            
            if (result.Results.Count < limit)
                break;
                
            start += limit;
            
            await Task.Delay(_config.RequestDelayMs, cancellationToken);
        }
        
        _logger.LogInformation("Found {Count} pages in space {SpaceKey}", pages.Count, spaceKey);
        return pages;
    }

    public async Task<ConfluencePage?> GetPageAsync(string pageId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Fetching page: {PageId}", pageId);
        
        var url = $"/rest/api/content/{pageId}?expand=body.storage,version,space,ancestors,children.page";
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
        var start = 0;
        const int limit = 50;
        
        while (true)
        {
            var url = $"/rest/api/content/{pageId}/child/page?start={start}&limit={limit}&expand=body.storage,version,space,ancestors";
            var response = await _httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();
            
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonConvert.DeserializeObject<ConfluenceSearchResult>(content);
            
            if (result?.Results == null || result.Results.Count == 0)
                break;
                
            children.AddRange(result.Results);
            
            if (result.Results.Count < limit)
                break;
                
            start += limit;
            
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