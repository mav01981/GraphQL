using HotChocolate.Execution;
using HotChocolate.Language;
using HotChocolate.PersistedOperations;
using Ledger.GraphQL.Api.Graph;
using Microsoft.Extensions.Options;

namespace Ledger.GraphQL.Api.Persistence;

/// <summary>A published allowlist entry, logged at startup so a reviewer can copy the hash.</summary>
public sealed record PublishedOperation(string Name, string DocumentId, string File);

/// <summary>
/// Publishes the committed operation documents (<c>operations/*.graphql</c>) into the
/// persisted-operation store at startup — the "trusted documents" half of ADR-003.
/// <para>
/// In production the API is configured with <c>OnlyAllowPersistedOperations = true</c> and the
/// persisted-operation pipeline rejects anything that is not in this store, so the client sends a
/// document id instead of a query text. Because the id is the SHA-256 of the document, a client cannot
/// smuggle a different query in under a known hash.
/// </para>
/// </summary>
public sealed class PersistedOperationAllowlist(
    IOperationDocumentStorage storage,
    IOptions<GraphQLOptions> graphqlOptions,
    IHostEnvironment environment,
    ILogger<PersistedOperationAllowlist> logger)
{
    private static readonly Sha256DocumentHashProvider HashProvider = new();

    public async Task<IReadOnlyList<PublishedOperation>> PublishAsync(CancellationToken cancellationToken)
    {
        var settings = graphqlOptions.Value.PersistedOperations;
        var documentsDirectory = System.IO.Path.Combine(environment.ContentRootPath, settings.DocumentsDirectory);

        if (!Directory.Exists(documentsDirectory))
        {
            logger.LogWarning("Persisted operation directory {Directory} does not exist; allowlist empty", documentsDirectory);
            return [];
        }

        Directory.CreateDirectory(System.IO.Path.Combine(environment.ContentRootPath, settings.StorageDirectory));

        var published = new List<PublishedOperation>();

        foreach (var file in Directory.EnumerateFiles(documentsDirectory, "*.graphql", SearchOption.TopDirectoryOnly).OrderBy(f => f))
        {
            var source = await File.ReadAllTextAsync(file, cancellationToken);
            var document = new OperationDocumentSourceText(source);
            var documentId = new OperationDocumentId(HashProvider.ComputeHash(document.AsSpan()).Value);

            await storage.SaveAsync(documentId, document, cancellationToken);

            var entry = new PublishedOperation(
                System.IO.Path.GetFileNameWithoutExtension(file),
                documentId.Value,
                System.IO.Path.GetRelativePath(environment.ContentRootPath, file));

            published.Add(entry);
        }

        foreach (var entry in published)
        {
            logger.LogInformation("Persisted operation '{Name}' → id {DocumentId} ({File})", entry.Name, entry.DocumentId, entry.File);
        }

        logger.LogInformation("Published {Count} persisted operation(s); onlyAllowPersistedOperations={Only}", published.Count, settings.OnlyAllowPersistedOperations);

        return published;
    }
}