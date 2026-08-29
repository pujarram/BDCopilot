using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using BDCopilot.Core.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BDCopilot.Infrastructure.Services;

public class BlobStorageSettings
{
    public const string SectionName = "BlobStorage";
    public string ConnectionString { get; set; } = "UseDevelopmentStorage=true";
    public string ContainerName { get; set; } = "generated-documents";

    /// <summary>How long a download link stays valid after a generator exports a file.</summary>
    public int SasExpiryMinutes { get; set; } = 60;

    /// <summary>
    /// When Azurite/Azure Blob is unavailable, write exports under this folder
    /// (relative to content root) and serve them via the API download endpoint.
    /// </summary>
    public string LocalExportFolder { get; set; } = "App_Data/exports";
}

/// <summary>
/// Caches generated Word/PowerPoint output. Prefers Azurite/Azure Blob; falls back to
/// local disk so export works in local-dev without Azurite running.
/// </summary>
public class BlobStorageService : IBlobStorageService
{
    private readonly BlobStorageSettings _settings;
    private readonly IHostEnvironment _env;
    private readonly ILogger<BlobStorageService> _logger;

    public BlobStorageService(
        IOptions<BlobStorageSettings> settings,
        IHostEnvironment env,
        ILogger<BlobStorageService> logger)
    {
        _settings = settings.Value;
        _env = env;
        _logger = logger;
    }

    public async Task<string> UploadGeneratedFileAsync(string fileName, Stream content, string contentType, CancellationToken ct = default)
    {
        try
        {
            return await UploadToAzureAsync(fileName, content, contentType, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Azure/Azurite blob upload failed — falling back to local export folder. " +
                "Start `docker compose up -d azurite` for blob emulator, or keep using local downloads.");

            if (content.CanSeek)
            {
                content.Position = 0;
            }

            return await SaveLocalAsync(fileName, content, ct);
        }
    }

    public Task<(Stream Content, string ContentType, string FileName)?> TryOpenLocalExportAsync(
        string token, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        // token format: {guid}/{sanitizedFileName}
        var safe = token.Replace('\\', '/').Trim('/');
        if (safe.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(safe))
        {
            return Task.FromResult<(Stream, string, string)?>(null);
        }

        var fullPath = Path.Combine(GetLocalRoot(), safe.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(fullPath))
        {
            return Task.FromResult<(Stream, string, string)?>(null);
        }

        var fileName = Path.GetFileName(fullPath);
        var contentType = GuessContentType(fileName);
        Stream stream = File.OpenRead(fullPath);
        return Task.FromResult<(Stream, string, string)?>((stream, contentType, fileName));
    }

    private async Task<string> UploadToAzureAsync(string fileName, Stream content, string contentType, CancellationToken ct)
    {
        var containerClient = new BlobContainerClient(_settings.ConnectionString, _settings.ContainerName);
        await containerClient.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct);

        var blobName = $"{DateTime.UtcNow:yyyy/MM/dd}/{Guid.NewGuid()}-{fileName}";
        var blobClient = containerClient.GetBlobClient(blobName);

        await blobClient.UploadAsync(content, new BlobHttpHeaders { ContentType = contentType }, cancellationToken: ct);

        if (blobClient.CanGenerateSasUri)
        {
            var sasBuilder = new BlobSasBuilder
            {
                BlobContainerName = _settings.ContainerName,
                BlobName = blobName,
                Resource = "b",
                ExpiresOn = DateTimeOffset.UtcNow.AddMinutes(_settings.SasExpiryMinutes)
            };
            sasBuilder.SetPermissions(BlobSasPermissions.Read);
            return blobClient.GenerateSasUri(sasBuilder).ToString();
        }

        return blobClient.Uri.ToString();
    }

    private async Task<string> SaveLocalAsync(string fileName, Stream content, CancellationToken ct)
    {
        var id = Guid.NewGuid().ToString("N");
        var folder = Path.Combine(GetLocalRoot(), id);
        Directory.CreateDirectory(folder);

        var safeName = Path.GetFileName(fileName);
        var fullPath = Path.Combine(folder, safeName);

        await using (var fs = File.Create(fullPath))
        {
            await content.CopyToAsync(fs, ct);
        }

        // Relative token consumed by GET /api/generate/download/{token}
        return $"/api/generate/download/{id}/{Uri.EscapeDataString(safeName)}";
    }

    private string GetLocalRoot() =>
        Path.Combine(_env.ContentRootPath, _settings.LocalExportFolder);

    private static string GuessContentType(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            ".zip" => "application/zip",
            _ => "application/octet-stream"
        };
}
