namespace CleanArchCqrs.Application.Common.Interfaces;

public interface IDocumentIndexSink
{
    // Read staged SQL generation/chunks, upsert idempotently. Return only once all vectors are durable.
    // Payload must include tenant, file, document version and generation; readers compare with SQL authority.
    Task StageAsync(Guid generationId, CancellationToken ct);
    Task DeleteAsync(Guid tenantId, Guid fileId, CancellationToken ct);
    Task DeleteGenerationAsync(Guid tenantId, Guid fileId, Guid generationId, CancellationToken ct);
}
