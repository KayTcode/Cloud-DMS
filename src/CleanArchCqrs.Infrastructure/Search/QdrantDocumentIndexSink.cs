using System.Security.Cryptography;
using System.Text;
using CleanArchCqrs.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
namespace CleanArchCqrs.Infrastructure.Search;

public sealed class QdrantDocumentIndexSink(IApplicationDbContext db, IEmbeddingService embeddings,
    QdrantVectorSearchService vectors, IOptions<EmbeddingOptions> options) : IDocumentIndexSink
{
    public async Task StageAsync(Guid generationId, CancellationToken ct)
    {
        var generation = await db.FileSearchGenerations.AsNoTracking().SingleAsync(g => g.Id == generationId, ct);
        if (generation.TenantId == Guid.Empty || generation.FileId == Guid.Empty || generation.ModelVersion != options.Value.ModelVersion)
            throw new InvalidDataException("INDEX_MODEL_SCOPE_CONTRACT");
        var count = await db.FileTextChunks.CountAsync(c => c.TenantId == generation.TenantId && c.GenerationId == generationId, ct);
        if (count > DocumentIndexOptions.MaxChunks) throw new InvalidDataException("INDEX_CHUNK_LIMIT");
        if (count == 0) return;
        var collection = QdrantOptions.Collection(generation.ModelVersion);
        await vectors.EnsureCollectionAsync(collection, true, ct);
        var staged = 0;
        while (staged < count)
        {
            var chunks = await db.FileTextChunks.AsNoTracking()
                .Where(c => c.TenantId == generation.TenantId && c.GenerationId == generationId && c.Ordinal >= staged)
                .OrderBy(c => c.Ordinal).Take(options.Value.BatchSize).ToListAsync(ct);
            if (chunks.Count == 0 || chunks.Where((c, i) => c.Ordinal != staged + i || c.TokenCount is < 1 or > 512).Any())
                throw new InvalidDataException("INDEX_CHUNK_SEQUENCE");
            var batch = await embeddings.EmbedPassagesAsync(chunks.Select(c => c.Text).ToArray(), ct);
            if (batch.Count != chunks.Count || batch.Any(e => e.ModelVersion != generation.ModelVersion)) throw new InvalidDataException("INDEX_EMBEDDING_CONTRACT");
            var points = chunks.Select((chunk, i) =>
            {
                EmbeddingOptions.ValidateVector(batch[i].Values);
                // Same immutable generation/ordinal always overwrites the same point on retries.
                var identity = $"{generation.TenantId:D}/{generation.FileId:D}/{generation.Id:D}/{chunk.Ordinal}";
                var id = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(identity)).AsSpan(0, 16));
                return (object)new { id = id.ToString("D"), vector = batch[i].Values, payload = new {
                    tenantId = generation.TenantId.ToString("D"), fileId = generation.FileId.ToString("D"),
                    generationId = generation.Id.ToString("D"), documentVersion = generation.DocumentVersion,
                    chunkOrdinal = chunk.Ordinal, modelVersion = generation.ModelVersion } };
            }).ToArray();
            await vectors.UpsertAsync(collection, points, ct);
            staged += chunks.Count;
        }
        // Publication/CAS belongs to the processor, never the vector store.
    }
    public Task DeleteAsync(Guid tenantId, Guid fileId, CancellationToken ct) => vectors.DeleteAsync(tenantId, fileId, null, ct);
    public Task DeleteGenerationAsync(Guid tenantId, Guid fileId, Guid generationId, CancellationToken ct) => vectors.DeleteAsync(tenantId, fileId, generationId, ct);
}
