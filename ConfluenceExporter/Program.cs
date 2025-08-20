using Fclp;
using ConfluenceExporter.Configuration;
using ConfluenceExporter.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Configuration;
using Serilog.Events;

namespace ConfluenceExporter;

internal class Program
{
    public static async Task<int> Main(string[] args)
    {
        var parser = new FluentCommandLineParser<CommandLineArgs>();

        parser.Setup(arg => arg.BaseUrl)
            .As('u', "base-url")
            .WithDescription("Confluence base URL (e.g., https://company.atlassian.net)")
            .Required();

        parser.Setup(arg => arg.Username)
            .As('n', "username")
            .WithDescription("Confluence username/email")
            .Required();

        parser.Setup(arg => arg.Token)
            .As('t', "token")
            .WithDescription("Confluence API token")
            .Required();

        parser.Setup(arg => arg.Output)
            .As('o', "output")
            .WithDescription("Output directory")
            .SetDefault("output");

        parser.Setup(arg => arg.Format)
            .As('f', "format")
            .WithDescription("Export format (Markdown, Html, Both)")
            .SetDefault(ExportFormat.Markdown);

        parser.Setup(arg => arg.Concurrent)
            .As('c', "concurrent")
            .WithDescription("Maximum concurrent requests")
            .SetDefault(5);

        parser.Setup(arg => arg.Delay)
            .As('d', "delay")
            .WithDescription("Request delay in milliseconds")
            .SetDefault(100);

        parser.Setup(arg => arg.PreserveHierarchy)
            .As("preserve-hierarchy")
            .WithDescription("Preserve page hierarchy in folder structure")
            .SetDefault(true);

        parser.Setup(arg => arg.IncludeImages)
            .As("include-images")
            .WithDescription("Download and include images")
            .SetDefault(true);

        parser.Setup(arg => arg.IncludeAttachments)
            .As("include-attachments")
            .WithDescription("Download and include attachments")
            .SetDefault(true);

        parser.Setup(arg => arg.CreateIndex)
            .As("create-index")
            .WithDescription("Create index files")
            .SetDefault(true);

        parser.Setup(arg => arg.Verbose)
            .As('v', "verbose")
            .WithDescription("Enable verbose logging")
            .SetDefault(false);

        parser.Setup(arg => arg.Command)
            .As("command")
            .WithDescription("Command to execute (page, space, hierarchy, all, list-spaces)")
            .Required();

        parser.Setup(arg => arg.Target)
            .As("target")
            .WithDescription("Target ID (page ID or space key)");

        parser.Setup(arg => arg.IncludeSpaces)
            .As("include-spaces")
            .WithDescription("Only export these spaces (comma-separated space keys)");

        parser.Setup(arg => arg.ExcludeSpaces)
            .As("exclude-spaces")
            .WithDescription("Exclude these spaces (comma-separated space keys)");

        parser.SetupHelp("?", "help")
            .WithHeader("Confluence Exporter - Export Confluence content to Markdown")
            .Callback(text => Console.WriteLine(text));

        var result = parser.Parse(args);

        if (result.HasErrors)
        {
            Console.WriteLine("Error parsing arguments:");
            Console.WriteLine(result.ErrorText);
            return 1;
        }

        if (result.HelpCalled)
        {
            return 0;
        }

        var commandArgs = parser.Object;
        var config = CreateConfiguration(commandArgs);
        
        var command = commandArgs.Command?.ToLowerInvariant();
        
        switch (command)
        {
            case "list-spaces":
                await ListSpacesAsync(config, commandArgs.Verbose);
                break;
            case "page":
                if (string.IsNullOrEmpty(commandArgs.Target))
                {
                    Console.WriteLine("Error: page command requires --target parameter with page ID");
                    return 1;
                }
                await ExecuteExportAsync(config, ExportScope.Page, commandArgs.Target, commandArgs.Verbose);
                break;
            case "space":
                if (string.IsNullOrEmpty(commandArgs.Target))
                {
                    Console.WriteLine("Error: space command requires --target parameter with space key");
                    return 1;
                }
                await ExecuteExportAsync(config, ExportScope.Space, commandArgs.Target, commandArgs.Verbose);
                break;
            case "hierarchy":
                if (string.IsNullOrEmpty(commandArgs.Target))
                {
                    Console.WriteLine("Error: hierarchy command requires --target parameter with page ID");
                    return 1;
                }
                await ExecuteExportAsync(config, ExportScope.Hierarchy, commandArgs.Target, commandArgs.Verbose);
                break;
            case "all":
                await ExecuteExportAsync(config, ExportScope.AllSpaces, null, commandArgs.Verbose);
                break;
            default:
                Console.WriteLine($"Error: Unknown command '{command}'. Use --help for available commands.");
                return 1;
        }

        Log.CloseAndFlush();
        return 0;
    }

    private static ExportConfiguration CreateConfiguration(CommandLineArgs args)
    {
        return new ExportConfiguration
        {
            ConfluenceBaseUrl = args.BaseUrl?.TrimEnd('/') ?? string.Empty,
            Username = args.Username ?? string.Empty,
            ApiToken = args.Token ?? string.Empty,
            OutputDirectory = args.Output ?? "output",
            Format = args.Format,
            MaxConcurrentRequests = args.Concurrent,
            RequestDelayMs = args.Delay,
            PreserveHierarchy = args.PreserveHierarchy,
            IncludeImages = args.IncludeImages,
            IncludeAttachments = args.IncludeAttachments,
            CreateIndexFile = args.CreateIndex,
            IncludedSpaces = ParseSpaceList(args.IncludeSpaces),
            ExcludedSpaces = ParseSpaceList(args.ExcludeSpaces)
        };
    }

    private static string[] ParseSpaceList(string? spaceList)
    {
        if (string.IsNullOrWhiteSpace(spaceList))
            return Array.Empty<string>();
        
        return spaceList.Split(',', StringSplitOptions.RemoveEmptyEntries)
                       .Select(s => s.Trim())
                       .Where(s => !string.IsNullOrEmpty(s))
                       .ToArray();
    }

    private static async Task ExecuteExportAsync(ExportConfiguration config, ExportScope scope, string? target, bool isVerbose = false)
    {
        var host = CreateHost(config, isVerbose);
        
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

    private static async Task ListSpacesAsync(ExportConfiguration config, bool isVerbose = false)
    {
        var host = CreateHost(config, isVerbose);
        
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

    private static IHost CreateHost(ExportConfiguration config, bool isVerbose = false)
    {
        // Configure Serilog
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(isVerbose ? LogEventLevel.Debug : LogEventLevel.Information)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(
                path: "logs/confluence-exporter-.log",
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] {SourceContext} - {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        return Host.CreateDefaultBuilder()
            .UseSerilog()
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

public class CommandLineArgs
{
    public string? BaseUrl { get; set; }
    public string? Username { get; set; }
    public string? Token { get; set; }
    public string? Output { get; set; }
    public ExportFormat Format { get; set; } = ExportFormat.Markdown;
    public int Concurrent { get; set; } = 5;
    public int Delay { get; set; } = 100;
    public bool PreserveHierarchy { get; set; } = true;
    public bool IncludeImages { get; set; } = true;
    public bool IncludeAttachments { get; set; } = true;
    public bool CreateIndex { get; set; } = true;
    public bool Verbose { get; set; } = false;
    public string? Command { get; set; }
    public string? Target { get; set; }
    public string? IncludeSpaces { get; set; }
    public string? ExcludeSpaces { get; set; }
}