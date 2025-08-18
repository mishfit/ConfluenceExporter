using ConfluenceExporter.Configuration;
using ConfluenceExporter.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Resilience;

namespace ConfluenceExporter.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddConfluenceExporter(this IServiceCollection services, ExportConfiguration configuration)
    {
        services.AddSingleton(configuration);

        services.AddHttpClient<IConfluenceApiClient, ConfluenceApiClient>()
            .ConfigureHttpClient(client =>
            {
                client.Timeout = TimeSpan.FromMinutes(10);
                client.DefaultRequestHeaders.Add("User-Agent", "ConfluenceExporter/1.0");
            })
            .AddStandardResilienceHandler();

        services.AddHttpClient<IAtlassianMarketplaceService, AtlassianMarketplaceService>()
            .ConfigureHttpClient(client =>
            {
                client.Timeout = TimeSpan.FromMinutes(5);
                client.DefaultRequestHeaders.Add("User-Agent", "ConfluenceExporter/1.0");
            })
            .AddStandardResilienceHandler();

        services.AddScoped<IMarkdownConverter, MarkdownConverter>();
        services.AddScoped<IContentExporter, ContentExporter>();

        return services;
    }
}