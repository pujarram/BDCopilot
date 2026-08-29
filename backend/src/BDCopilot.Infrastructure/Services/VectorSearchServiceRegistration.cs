using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BDCopilot.Infrastructure.Services;

/// <summary>
/// Helper for choosing the vector search backend at DI registration time.
/// </summary>
public static class VectorSearchServiceRegistration
{
    /// <summary>
    /// Registers <see cref="AzureAiSearchVectorSearchService"/> when <c>AzureSearch:Endpoint</c>
    /// is set; otherwise registers <see cref="VectorSearchService"/> (Postgres/pgvector).
    /// </summary>
    public static IServiceCollection AddBdCopilotVectorSearch(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AzureAiSearchSettings>(configuration.GetSection(AzureAiSearchSettings.SectionName));

        var endpoint = configuration.GetSection(AzureAiSearchSettings.SectionName).GetValue<string>("Endpoint");
        if (!string.IsNullOrWhiteSpace(endpoint))
        {
            services.AddScoped<Core.Interfaces.IVectorSearchService, AzureAiSearchVectorSearchService>();
        }
        else
        {
            services.AddScoped<Core.Interfaces.IVectorSearchService, VectorSearchService>();
        }

        return services;
    }
}
