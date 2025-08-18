using System.Text;
using System.Text.Json;
using ConfluenceExporter.Configuration;
using Microsoft.Extensions.Logging;

namespace ConfluenceExporter.Services;

public interface IAtlassianMarketplaceService
{
    Task<string> RegisterAppAsync(AppDescriptor appDescriptor, CancellationToken cancellationToken = default);
    Task<bool> ValidateInstallationAsync(string installationId, CancellationToken cancellationToken = default);
    Task<AppInstallation?> GetInstallationAsync(string installationId, CancellationToken cancellationToken = default);
    Task<bool> SendUsageMetricsAsync(string installationId, UsageMetrics metrics, CancellationToken cancellationToken = default);
}

public class AtlassianMarketplaceService : IAtlassianMarketplaceService, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<AtlassianMarketplaceService> _logger;
    private readonly ExportConfiguration _config;

    public AtlassianMarketplaceService(HttpClient httpClient, ILogger<AtlassianMarketplaceService> logger, ExportConfiguration config)
    {
        _httpClient = httpClient;
        _logger = logger;
        _config = config;

        SetupHttpClient();
    }

    private void SetupHttpClient()
    {
        _httpClient.BaseAddress = new Uri("https://marketplace.atlassian.com/");
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "ConfluenceExporter/1.0");
        _httpClient.Timeout = TimeSpan.FromMinutes(2);
    }

    public async Task<string> RegisterAppAsync(AppDescriptor appDescriptor, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Registering app with Atlassian Marketplace");

        try
        {
            var json = JsonSerializer.Serialize(appDescriptor, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            });

            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync("api/apps", content, cancellationToken);
            
            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
                var result = JsonSerializer.Deserialize<AppRegistrationResult>(responseContent, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                _logger.LogInformation("App registered successfully with ID: {AppId}", result?.AppId);
                return result?.AppId ?? string.Empty;
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Failed to register app. Status: {Status}, Error: {Error}", response.StatusCode, error);
                throw new InvalidOperationException($"App registration failed: {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during app registration");
            throw;
        }
    }

    public async Task<bool> ValidateInstallationAsync(string installationId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Validating installation: {InstallationId}", installationId);

        try
        {
            var response = await _httpClient.GetAsync($"api/installations/{installationId}/validate", cancellationToken);
            
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Installation {InstallationId} is valid", installationId);
                return true;
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning("Installation {InstallationId} not found", installationId);
                return false;
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Installation validation failed. Status: {Status}, Error: {Error}", response.StatusCode, error);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating installation {InstallationId}", installationId);
            return false;
        }
    }

    public async Task<AppInstallation?> GetInstallationAsync(string installationId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting installation details: {InstallationId}", installationId);

        try
        {
            var response = await _httpClient.GetAsync($"api/installations/{installationId}", cancellationToken);
            
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                var installation = JsonSerializer.Deserialize<AppInstallation>(content, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                _logger.LogInformation("Retrieved installation details for: {InstallationId}", installationId);
                return installation;
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning("Installation {InstallationId} not found", installationId);
                return null;
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Failed to get installation details. Status: {Status}, Error: {Error}", response.StatusCode, error);
                return null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting installation {InstallationId}", installationId);
            return null;
        }
    }

    public async Task<bool> SendUsageMetricsAsync(string installationId, UsageMetrics metrics, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Sending usage metrics for installation: {InstallationId}", installationId);

        try
        {
            var json = JsonSerializer.Serialize(metrics, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync($"api/installations/{installationId}/metrics", content, cancellationToken);
            
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Usage metrics sent successfully for installation: {InstallationId}", installationId);
                return true;
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Failed to send usage metrics. Status: {Status}, Error: {Error}", response.StatusCode, error);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending usage metrics for installation {InstallationId}", installationId);
            return false;
        }
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
        GC.SuppressFinalize(this);
    }
}

public class AppDescriptor
{
    public string Name { get; set; } = "Confluence Exporter";
    public string Key { get; set; } = "com.confluence.exporter";
    public string Description { get; set; } = "Export Confluence pages, spaces, or hierarchies to Markdown files";
    public string Version { get; set; } = "1.0.0";
    public string Vendor { get; set; } = "Confluence Tools";
    public string BaseUrl { get; set; } = string.Empty;
    public List<string> Scopes { get; set; } = new() { "READ", "WRITE" };
    public List<AppModule> Modules { get; set; } = new();
    public AppLinks Links { get; set; } = new();
    public AppAuthentication Authentication { get; set; } = new();
}

public class AppModule
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public Dictionary<string, object> Properties { get; set; } = new();
}

public class AppLinks
{
    public string Self { get; set; } = string.Empty;
    public string Homepage { get; set; } = string.Empty;
    public string Documentation { get; set; } = string.Empty;
}

public class AppAuthentication
{
    public string Type { get; set; } = "jwt";
}

public class AppRegistrationResult
{
    public string AppId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class AppInstallation
{
    public string InstallationId { get; set; } = string.Empty;
    public string AppId { get; set; } = string.Empty;
    public string ClientKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string ProductType { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public DateTime InstalledAt { get; set; }
    public AppUser User { get; set; } = new();
}

public class AppUser
{
    public string AccountId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

public class UsageMetrics
{
    public string InstallationId { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public int PagesExported { get; set; }
    public int SpacesExported { get; set; }
    public long TotalSizeBytes { get; set; }
    public TimeSpan ExportDuration { get; set; }
    public string ExportFormat { get; set; } = string.Empty;
    public Dictionary<string, object> CustomMetrics { get; set; } = new();
}