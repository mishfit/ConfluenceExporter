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

        var baseUrlOption = new Option<string>(
            name: "--base-url",
            description: "Confluence base URL (e.g., https://company.atlassian.net)")
        { IsRequired = true };

        var usernameOption = new Option<string>(
            name: "--username",
            description: "Confluence username/email")
        { IsRequired = true };

        var tokenOption = new Option<string>(
            name: "--token",
            description: "Confluence API token")
        { IsRequired = true };

        var outputOption = new Option<string>(
            name: "--output",
            description: "Output directory",
            getDefaultValue: () => "output");

        var formatOption = new Option<ExportFormat>(
            name: "--format",
            description: "Export format",
            getDefaultValue: () => ExportFormat.Markdown);

        var concurrentOption = new Option<int>(
            name: "--concurrent",
            description: "Maximum concurrent requests",
            getDefaultValue: () => 5);

        var delayOption = new Option<int>(
            name: "--delay",
            description: "Request delay in milliseconds",
            getDefaultValue: () => 100);

        var preserveHierarchyOption = new Option<bool>(
            name: "--preserve-hierarchy",
            description: "Preserve page hierarchy in folder structure",
            getDefaultValue: () => true);

        var includeImagesOption = new Option<bool>(
            name: "--include-images",
            description: "Download and include images",
            getDefaultValue: () => true);

        var includeAttachmentsOption = new Option<bool>(
            name: "--include-attachments",
            description: "Download and include attachments",
            getDefaultValue: () => true);

        var createIndexOption = new Option<bool>(
            name: "--create-index",
            description: "Create index files",
            getDefaultValue: () => true);

        var verboseOption = new Option<bool>(
            name: "--verbose",
            description: "Enable verbose logging");

        rootCommand.AddGlobalOption(baseUrlOption);
        rootCommand.AddGlobalOption(usernameOption);
        rootCommand.AddGlobalOption(tokenOption);
        rootCommand.AddGlobalOption(outputOption);
        rootCommand.AddGlobalOption(formatOption);
        rootCommand.AddGlobalOption(concurrentOption);
        rootCommand.AddGlobalOption(delayOption);
        rootCommand.AddGlobalOption(preserveHierarchyOption);
        rootCommand.AddGlobalOption(includeImagesOption);
        rootCommand.AddGlobalOption(includeAttachmentsOption);
        rootCommand.AddGlobalOption(createIndexOption);
        rootCommand.AddGlobalOption(verboseOption);

        var exportPageCommand = new Command("page", "Export a single page and its children")
        {
            new Argument<string>("page-id", "Confluence page ID to export")
        };

        var exportSpaceCommand = new Command("space", "Export an entire space")
        {
            new Argument<string>("space-key", "Confluence space key to export")
        };

        var exportHierarchyCommand = new Command("hierarchy", "Export a page hierarchy")
        {
            new Argument<string>("page-id", "Root page ID for hierarchy export")
        };

        var exportAllCommand = new Command("all", "Export all accessible spaces")
        {
            new Option<string[]>("--include-spaces", "Only export these spaces (space keys)") { AllowMultipleArgumentsPerToken = true },
            new Option<string[]>("--exclude-spaces", "Exclude these spaces (space keys)") { AllowMultipleArgumentsPerToken = true }
        };

        var listSpacesCommand = new Command("list-spaces", "List all accessible spaces");

        rootCommand.AddCommand(exportPageCommand);
        rootCommand.AddCommand(exportSpaceCommand);
        rootCommand.AddCommand(exportHierarchyCommand);
        rootCommand.AddCommand(exportAllCommand);
        rootCommand.AddCommand(listSpacesCommand);

        exportPageCommand.SetHandler(async (string pageId, string baseUrl, string username, string token, string output, ExportFormat format, int concurrent, int delay, bool preserveHierarchy, bool includeImages, bool includeAttachments, bool createIndex) =>
        {
            var config = CreateConfiguration(baseUrl, username, token, output, format, concurrent, delay, preserveHierarchy, includeImages, includeAttachments, createIndex);
            await ExecuteExportAsync(config, ExportScope.Page, pageId);
        }, exportPageCommand.Arguments[0], baseUrlOption, usernameOption, tokenOption, outputOption, formatOption, concurrentOption, delayOption, preserveHierarchyOption, includeImagesOption, includeAttachmentsOption, createIndexOption);

        exportSpaceCommand.SetHandler(async (string spaceKey, string baseUrl, string username, string token, string output, ExportFormat format, int concurrent, int delay, bool preserveHierarchy, bool includeImages, bool includeAttachments, bool createIndex) =>
        {
            var config = CreateConfiguration(baseUrl, username, token, output, format, concurrent, delay, preserveHierarchy, includeImages, includeAttachments, createIndex);
            await ExecuteExportAsync(config, ExportScope.Space, spaceKey);
        }, exportSpaceCommand.Arguments[0], baseUrlOption, usernameOption, tokenOption, outputOption, formatOption, concurrentOption, delayOption, preserveHierarchyOption, includeImagesOption, includeAttachmentsOption, createIndexOption);

        exportHierarchyCommand.SetHandler(async (string pageId, string baseUrl, string username, string token, string output, ExportFormat format, int concurrent, int delay, bool preserveHierarchy, bool includeImages, bool includeAttachments, bool createIndex) =>
        {
            var config = CreateConfiguration(baseUrl, username, token, output, format, concurrent, delay, preserveHierarchy, includeImages, includeAttachments, createIndex);
            await ExecuteExportAsync(config, ExportScope.Hierarchy, pageId);
        }, exportHierarchyCommand.Arguments[0], baseUrlOption, usernameOption, tokenOption, outputOption, formatOption, concurrentOption, delayOption, preserveHierarchyOption, includeImagesOption, includeAttachmentsOption, createIndexOption);

        exportAllCommand.SetHandler(async (string[] includeSpaces, string[] excludeSpaces, string baseUrl, string username, string token, string output, ExportFormat format, int concurrent, int delay, bool preserveHierarchy, bool includeImages, bool includeAttachments, bool createIndex) =>
        {
            var config = CreateConfiguration(baseUrl, username, token, output, format, concurrent, delay, preserveHierarchy, includeImages, includeAttachments, createIndex);
            config.IncludedSpaces = includeSpaces ?? Array.Empty<string>();
            config.ExcludedSpaces = excludeSpaces ?? Array.Empty<string>();
            await ExecuteExportAsync(config, ExportScope.AllSpaces, null);
        }, exportAllCommand.Options[0], exportAllCommand.Options[1], baseUrlOption, usernameOption, tokenOption, outputOption, formatOption, concurrentOption, delayOption, preserveHierarchyOption, includeImagesOption, includeAttachmentsOption, createIndexOption);

        listSpacesCommand.SetHandler(async (string baseUrl, string username, string token, string output, ExportFormat format, int concurrent, int delay, bool preserveHierarchy, bool includeImages, bool includeAttachments, bool createIndex) =>
        {
            var config = CreateConfiguration(baseUrl, username, token, output, format, concurrent, delay, preserveHierarchy, includeImages, includeAttachments, createIndex);
            await ListSpacesAsync(config);
        }, baseUrlOption, usernameOption, tokenOption, outputOption, formatOption, concurrentOption, delayOption, preserveHierarchyOption, includeImagesOption, includeAttachmentsOption, createIndexOption);

        return await rootCommand.InvokeAsync(args);
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