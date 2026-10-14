using CleanArchCqrs.Application.Common.Authorization;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Models.Files;
using CleanArchCqrs.Application.Features.Files.Queries.SearchFiles;
using CleanArchCqrs.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
namespace CleanArchCqrs.Application.Features.Files.Queries.GetFileText;

public sealed record GetFileTextQuery(Guid FileId, int Limit = 20, string? Cursor = null)
    : IRequest<FileTextResult>, ISensitiveReadRequest;
public sealed record FileTextItem(int Ordinal, string Text, string? Location);
public sealed record FileTextResult(string Status, long DocumentVersion, IReadOnlyList<FileTextItem> Items,
    string? NextCursor = null, bool IsPartial = false, string? Reason = null);
public sealed class GetFileTextQueryHandler(IFileAccessContextResolver resolver, IFileAccessService access,
    IApplicationDbContext db, IFileTextCursorProtector protector, FileTextOptions options)
    : IRequestHandler<GetFileTextQuery, FileTextResult>
{
    public async Task<FileTextResult> Handle(GetFileTextQuery request, CancellationToken ct)
    {
        if (request.Limit is < 1 or > 100) throw new InvalidSearchRequestException();
        var context = await resolver.ResolveContextAsync(ct);
        var grant = await access.CheckAsync(context, request.FileId, PermissionNames.FileRead, ct);
        if (grant is null) return new("unavailable", 0, [], Reason: "FILE_UNAVAILABLE");
        var ordinal = 0;
        var expiry = DateTimeOffset.UtcNow.AddMinutes(5);
        FileTextCursor? cursor = null;
        if (request.Cursor is not null)
        {
            cursor = protector.Unprotect(request.Cursor);
            if (cursor.ExpiresAt <= DateTimeOffset.UtcNow || cursor.ExpiresAt > expiry
                || cursor.UserId != context.Actor.UserId || cursor.SessionId != context.Actor.SessionId
                || cursor.TenantId != context.TenantId || cursor.FileId != request.FileId
                || cursor.DocumentVersion != grant.DocumentVersion || cursor.Ordinal < 0)
                throw new InvalidSearchRequestException();
            ordinal = cursor.Ordinal; expiry = cursor.ExpiresAt;
        }
        var generation = await CurrentAsync(context, grant, ct);
        if (cursor is not null && (generation is null || cursor.GenerationId != generation.Id || cursor.ModelVersion != generation.ModelVersion))
            throw new CleanArchCqrs.Application.Common.Exceptions.AuthorizationChangedException();
        if (generation is null)
        {
            await access.AssertStillValidAsync(context, new[] { grant }, ct);
            return new("pending", grant.DocumentVersion, [], Reason: "TEXT_NOT_READY");
        }
        var chunks = await db.FileTextChunks.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.TenantId == context.TenantId && c.GenerationId == generation.Id && c.Ordinal >= ordinal)
            .OrderBy(c => c.Ordinal).Take(request.Limit + 1)
            .Select(c => new FileTextItem(c.Ordinal, c.Text, c.Location)).ToListAsync(ct);
        var more = chunks.Count > request.Limit;
        var items = chunks.Take(request.Limit).ToArray();
        await access.AssertStillValidAsync(context, new[] { grant }, ct);
        var latest = await CurrentAsync(context, grant, ct);
        if (latest?.Id != generation.Id || latest.ModelVersion != generation.ModelVersion)
            throw new CleanArchCqrs.Application.Common.Exceptions.AuthorizationChangedException();
        var next = more ? protector.Protect(new(context.Actor.UserId, context.Actor.SessionId,
            context.TenantId, request.FileId, grant.DocumentVersion, generation.Id, generation.ModelVersion,
            checked(items[^1].Ordinal + 1), expiry)) : null;
        await access.AssertStillValidAsync(context, new[] { grant }, ct);
        return new("ready", grant.DocumentVersion, items, next, generation.IsPartial, generation.FailureCode);
    }
    private Task<CleanArchCqrs.Domain.Entities.FileSearchGeneration?> CurrentAsync(FileAccessContext context,
        FileAccessGrant grant, CancellationToken ct) =>
        (from f in db.FileEntries.IgnoreQueryFilters().AsNoTracking()
         join g in db.FileSearchGenerations.IgnoreQueryFilters().AsNoTracking() on f.ActiveSearchGenerationId equals (Guid?)g.Id
         join v in db.FileContentVersions.IgnoreQueryFilters().AsNoTracking()
            on new { f.TenantId, FileId = f.Id, f.DocumentVersion } equals new { v.TenantId, v.FileId, v.DocumentVersion }
         where f.Id == grant.FileId && f.TenantId == context.TenantId && !f.IsDeleted
            && f.Lifecycle == FileLifecycle.Active && f.ScanStatus == "Clean" && f.DocumentVersion == grant.DocumentVersion
            && g.TenantId == context.TenantId && g.FileId == f.Id && g.DocumentVersion == f.DocumentVersion
            && g.ModelVersion == options.ModelVersion
            && (g.Status == FileSearchStatus.Ready || g.Status == FileSearchStatus.Partial)
            && v.ScanStatus == FileScanStatus.Clean && v.PhysicalStorageDeletedAt == null
            && v.ObjectKey == f.StorageKey && v.ProviderId == f.StorageProviderId && v.Hash == f.Hash && v.Hash != ""
         select g).SingleOrDefaultAsync(ct);
}
