using BDCopilot.Core.Interfaces;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Embeddings;

namespace BDCopilot.Infrastructure.Services;

#pragma warning disable SKEXP0001 // ITextEmbeddingGenerationService is experimental in this SK version.

public class EmbeddingService : IEmbeddingService
{
    private readonly Kernel _kernel;

    public EmbeddingService(ISemanticKernelFactory kernelFactory)
    {
        _kernel = kernelFactory.CreateKernel();
    }

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        var embeddingService = _kernel.GetRequiredService<ITextEmbeddingGenerationService>();
        var embedding = await embeddingService.GenerateEmbeddingAsync(text, cancellationToken: ct);
        return embedding.ToArray();
    }
}

#pragma warning restore SKEXP0001
