using Azure.Identity;
using BDCopilot.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
#pragma warning disable SKEXP0070 // Ollama connector is experimental in Semantic Kernel as of this writing.
using Microsoft.SemanticKernel.Connectors.Ollama;
#pragma warning restore SKEXP0070

namespace BDCopilot.Infrastructure.Services;

public interface ISemanticKernelFactory
{
    /// <summary>Builds a Kernel wired to whichever provider <c>Ai:Provider</c> selects.
    /// This is the one place that decision is made — everything else in the app talks to the
    /// returned Kernel's chat completion / text embedding services and never branches on
    /// provider again.</summary>
    Kernel CreateKernel();
}

public class SemanticKernelFactory : ISemanticKernelFactory
{
    private readonly AiSettings _settings;
    private readonly ILoggerFactory _loggerFactory;
    private readonly HttpClient _ollamaHttpClient;

    public SemanticKernelFactory(IOptions<AiSettings> settings, ILoggerFactory loggerFactory)
    {
        _settings = settings.Value;
        _loggerFactory = loggerFactory;
        // Default HttpClient timeout (100s) kills long local Ollama generations and surfaces as HTTP 500.
        _ollamaHttpClient = new HttpClient
        {
            BaseAddress = new Uri(_settings.Ollama.Endpoint.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromMinutes(10)
        };
    }

    public Kernel CreateKernel()
    {
        var builder = Kernel.CreateBuilder();
        builder.Services.AddSingleton(_loggerFactory);

        switch (_settings.Provider)
        {
            case "AzureOpenAI":
                AddAzureOpenAI(builder);
                break;

            case "Ollama":
            default:
                AddOllama(builder);
                break;
        }

        return builder.Build();
    }

    private void AddOllama(IKernelBuilder builder)
    {
#pragma warning disable SKEXP0070
        builder.AddOllamaChatCompletion(
            modelId: _settings.Ollama.ChatModel,
            httpClient: _ollamaHttpClient);

        builder.AddOllamaTextEmbeddingGeneration(
            modelId: _settings.Ollama.EmbeddingModel,
            httpClient: _ollamaHttpClient);
#pragma warning restore SKEXP0070
    }

    private void AddAzureOpenAI(IKernelBuilder builder)
    {
        var cfg = _settings.AzureOpenAI;
        if (string.IsNullOrWhiteSpace(cfg.Endpoint))
        {
            throw new InvalidOperationException(
                "Ai:AzureOpenAI:Endpoint is required when Ai:Provider is \"AzureOpenAI\".");
        }

        if (cfg.UseEntraIdAuth || string.IsNullOrWhiteSpace(cfg.ApiKey))
        {
            // Production default: the App Service's managed identity, no secret to rotate.
            var credential = new DefaultAzureCredential();
            builder.AddAzureOpenAIChatCompletion(cfg.ChatDeployment, cfg.Endpoint, credential);
#pragma warning disable SKEXP0010
            builder.AddAzureOpenAITextEmbeddingGeneration(cfg.EmbeddingDeployment, cfg.Endpoint, credential);
#pragma warning restore SKEXP0010
        }
        else
        {
            // Local/dev fallback only — pull the key from user-secrets or Key Vault, never
            // check it into appsettings.json.
            builder.AddAzureOpenAIChatCompletion(cfg.ChatDeployment, cfg.Endpoint, cfg.ApiKey!);
#pragma warning disable SKEXP0010
            builder.AddAzureOpenAITextEmbeddingGeneration(cfg.EmbeddingDeployment, cfg.Endpoint, cfg.ApiKey!);
#pragma warning restore SKEXP0010
        }
    }
}
