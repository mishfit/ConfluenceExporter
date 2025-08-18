using System.CommandLine;
using ConfluenceExporter.Configuration;
using ConfluenceExporter.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ConfluenceExporter;

internal class Program
{
    public static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("Confluence Exporter - Export Confluence content to Markdown");

        var baseUrlOption = new Option<string>("--base-url", "Confluence base URL (e.g., https://company.atlassian.net)");
        var usernameOption = new Option<string>("--username", "Confluence username/email");
        var tokenOption = new Option<string>("--token", "Confluence API token");
        var outputOption = new Option<string>("--output", "Output directory");
        var formatOption = new Option<ExportFormat>("--format", "Export format");
        var concurrentOption = new Option<int>("--concurrent", "Maximum concurrent requests");
        var delayOption = new Option<int>("--delay", "Request delay in milliseconds");
        var preserveHierarchyOption = new Option<bool>("--preserve-hierarchy", "Preserve page hierarchy in folder structure");
        var includeImagesOption = new Option<bool>("--include-images", "Download and include images");
        var includeAttachmentsOption = new Option<bool>("--include-attachments", "Download and include attachments");
        var createIndexOption = new Option<bool>("--create-index", "Create index files");
        var verboseOption = new Option<bool>("--verbose", "Enable verbose logging");

        rootCommand.Add(baseUrlOption);
        rootCommand.Add(usernameOption);
        rootCommand.Add(tokenOption);
        rootCommand.Add(outputOption);
        rootCommand.Add(formatOption);
        rootCommand.Add(concurrentOption);
        rootCommand.Add(delayOption);
        rootCommand.Add(preserveHierarchyOption);
        rootCommand.Add(includeImagesOption);
        rootCommand.Add(includeAttachmentsOption);
        rootCommand.Add(createIndexOption);
        rootCommand.Add(verboseOption);

        var pageIdArgument = new Argument<string>("page-id");
        var spaceKeyArgument = new Argument<string>("space-key");
        var rootPageIdArgument = new Argument<string>("page-id");
        var includeSpacesOption = new Option<string[]>("--include-spaces", "Only export these spaces (space keys)");
        var excludeSpacesOption = new Option<string[]>("--exclude-spaces", "Exclude these spaces (space keys)");

        var exportPageCommand = new Command("page", "Export a single page and its children");
        exportPageCommand.Add(pageIdArgument);

        var exportSpaceCommand = new Command("space", "Export an entire space");
        exportSpaceCommand.Add(spaceKeyArgument);

        var exportHierarchyCommand = new Command("hierarchy", "Export a page hierarchy");
        exportHierarchyCommand.Add(rootPageIdArgument);

        var exportAllCommand = new Command("all", "Export all accessible spaces");
        exportAllCommand.Add(includeSpacesOption);
        exportAllCommand.Add(excludeSpacesOption);

        var listSpacesCommand = new Command("list-spaces", "List all accessible spaces");

        rootCommand.Add(exportPageCommand);
        rootCommand.Add(exportSpaceCommand);
        rootCommand.Add(exportHierarchyCommand);
        rootCommand.Add(exportAllCommand);
        rootCommand.Add(listSpacesCommand);

        listSpacesCommand.SetHandler(async (context) =>
        {
            var baseUrl = context.ParseResult.GetValueForOption<string>(baseUrlOption);
            var username = context.ParseResult.GetValueForOption<string>(usernameOption);
            var token = context.ParseResult.GetValueForOption<string>(tokenOption);

            var config = new ExportConfiguration { ConfluenceBaseUrl = baseUrl, Username = username, ApiToken = token };
            
            await ListSpacesAsync(config);
        });
        
        return rootCommand.Invoke(args);

        // TODO: Add command handlers when System.CommandLine API is stable
    }

    private static ExportConfiguration CreateConfiguration(string baseUrl, string username, string token, string output, ExportFormat format, int concurrent, int delay, bool preserveHierarchy, bool includeImages, bool includeAttachments, bool createIndex)
    {
        return new ExportConfiguration
        {
            ConfluenceBaseUrl = baseUrl.TrimEnd('/'),
            Username = username,
            ApiToken = token,
            OutputDirectory = output,
            Format = format,
            MaxConcurrentRequests = concurrent,
            RequestDelayMs = delay,
            PreserveHierarchy = preserveHierarchy,
            IncludeImages = includeImages,
            IncludeAttachments = includeAttachments,
            CreateIndexFile = createIndex
        };
    }

    private static async Task ExecuteExportAsync(ExportConfiguration config, ExportScope scope, string? target)
    {
        var host = CreateHost(config);
        
        using var serviceScope = host.Services.CreateScope();
        var logger = serviceScope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        var exporter = serviceScope.ServiceProvider.GetRequiredService<IContentExporter>();

        try
        {
            logger.LogInformation("Starting export - Scope: {Scope}, Target: {Target}", scope, target ?? "N/A");
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            switch (scope)
            {
                case ExportScope.Page:
                    if (!string.IsNullOrEmpty(target))
                    {
                        var apiClient = serviceScope.ServiceProvider.GetRequiredService<IConfluenceApiClient>();
                        var page = await apiClient.GetPageAsync(target);
                        if (page != null)
                        {
                            await exporter.ExportPageAsync(page, config.OutputDirectory);
                        }
                        else
                        {
                            logger.LogError("Page with ID {PageId} not found", target);
                        }
                    }
                    break;

                case ExportScope.Space:
                    if (!string.IsNullOrEmpty(target))
                    {
                        await exporter.ExportSpaceAsync(target);
                    }
                    break;

                case ExportScope.Hierarchy:
                    if (!string.IsNullOrEmpty(target))
                    {
                        await exporter.ExportPageHierarchyAsync(target);
                    }
                    break;

                case ExportScope.AllSpaces:
                    await exporter.ExportAllSpacesAsync();
                    break;
            }

            stopwatch.Stop();
            logger.LogInformation("Export completed successfully in {Duration}", stopwatch.Elapsed);

            var metricsService = serviceScope.ServiceProvider.GetRequiredService<IAtlassianMarketplaceService>();
            try
            {
                await SendUsageMetricsAsync(metricsService, config, scope, stopwatch.Elapsed);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to send usage metrics");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Export failed");
            Environment.Exit(1);
        }
    }

    private static async Task ListSpacesAsync(ExportConfiguration config)
    {
        var host = CreateHost(config);
        
        using var serviceScope = host.Services.CreateScope();
        var logger = serviceScope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        var apiClient = serviceScope.ServiceProvider.GetRequiredService<IConfluenceApiClient>();

        try
        {
            logger.LogInformation("Fetching spaces...");
            var spaces = await apiClient.GetSpacesAsync();

            Console.WriteLine($"\nFound {spaces.Count} spaces:\n");
            Console.WriteLine("Key".PadRight(20) + "Name".PadRight(40) + "Type");
            Console.WriteLine(new string('-', 70));

            foreach (var space in spaces.OrderBy(s => s.Key))
            {
                Console.WriteLine($"{space.Key.PadRight(20)}{space.Name.PadRight(40)}{space.Type}");
            }

            Console.WriteLine();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to list spaces");
            Environment.Exit(1);
        }
    }

    private static async Task SendUsageMetricsAsync(IAtlassianMarketplaceService metricsService, ExportConfiguration config, ExportScope scope, TimeSpan duration)
    {
        var metrics = new UsageMetrics
        {
            InstallationId = Environment.MachineName,
            ExportDuration = duration,
            ExportFormat = config.Format.ToString(),
            CustomMetrics = new Dictionary<string, object>
            {
                ["scope"] = scope.ToString(),
                ["includeImages"] = config.IncludeImages,
                ["includeAttachments"] = config.IncludeAttachments,
                ["preserveHierarchy"] = config.PreserveHierarchy
            }
        };

        await metricsService.SendUsageMetricsAsync(metrics.InstallationId, metrics);
    }

    private static IHost CreateHost(ExportConfiguration config)
    {
        var isVerbose = Environment.GetCommandLineArgs().Contains("--verbose");

        return Host.CreateDefaultBuilder()
            .ConfigureLogging(builder =>
            {
                builder.ClearProviders();
                builder.AddConsole();
                builder.SetMinimumLevel(isVerbose ? LogLevel.Debug : LogLevel.Information);
            })
            .ConfigureServices(services =>
            {
                services.AddSingleton(config);
                services.AddHttpClient<IConfluenceApiClient, ConfluenceApiClient>();
                services.AddHttpClient<IAtlassianMarketplaceService, AtlassianMarketplaceService>();
                services.AddScoped<IMarkdownConverter, MarkdownConverter>();
                services.AddScoped<IContentExporter, ContentExporter>();
            })
            .Build();
    }
}