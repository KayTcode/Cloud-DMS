using CleanArchCqrs.Application.Common.Authorization;
using CleanArchCqrs.Application.Common.Exceptions;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Models;
using CleanArchCqrs.Application.Common.Models.Files;
using CleanArchCqrs.Application.Features.Files.DTOs;
using CleanArchCqrs.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CleanArchCqrs.Application.Features.Files.Queries.GetTrashFiles;

public record GetTrashFilesQuery : IRequest<Result<TrashFileListDto>>, ISensitiveReadRequest
{
    public Guid? TenantId { get; init; }
    public Guid? DepartmentId { get; init; }
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 20;
    public string? SearchTerm { get; init; }
}

public class GetTrashFilesQueryHandler(IApplicationDbContext db, IFileAccessContextResolver resolver,
    IAccessSnapshotStore snapshots, AccessPolicyEvaluator evaluator)
    : IRequestHandler<GetTrashFilesQuery, Result<TrashFileListDto>>
{
    public async Task<Result<TrashFileListDto>> Handle(GetTrashFilesQuery request, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        var ct = deadline.Token;
        if (request.PageNumber < 1 || request.PageSize is < 1 or > 100
            || (long)(request.PageNumber - 1) * request.PageSize > int.MaxValue || request.SearchTerm?.Length > 1000)
            return Result<TrashFileListDto>.Failure("INVALID_LIST_REQUEST");
        var context = await resolver.ResolveContextAsync(ct);
        if (request.TenantId.HasValue && request.TenantId != context.TenantId) throw new UnauthorizedAccessException();
        var before = await ReadAuthorityAsync(context, ct);
        var admin = before.Decision == AuthorizationDecision.Allow;

        var currentUser = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == context.Actor.UserId, ct);
        var userDeptId = currentUser?.DepartmentId;
        var isDeptAdmin = userDeptId.HasValue && (await db.UserRoles.AnyAsync(ur => ur.UserId == context.Actor.UserId && ur.Role.Name == "DepartmentAdmin", ct));

        var scope = db.FileEntries.IgnoreQueryFilters().AsNoTracking()
            .Include(f => f.Owner)
            .Include(f => f.DeletedByUser)
            .Include(f => f.Department)
            .Include(f => f.StorageProvider)
            .Where(f => f.TenantId == context.TenantId && f.IsDeleted && f.Lifecycle == FileLifecycle.Binned
                && (admin || (isDeptAdmin ? (f.DepartmentId == userDeptId || f.OwningDepartmentId == userDeptId) : (f.OwnerId == context.Actor.UserId || f.DeletedByUserId == context.Actor.UserId))));

        if (request.DepartmentId.HasValue) scope = scope.Where(f => f.DepartmentId == request.DepartmentId);
        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            var pattern = "%" + request.SearchTerm.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            scope = scope.Where(f => EF.Functions.Like(f.Name, pattern, "\\"));
        }

        var totalCount = await scope.CountAsync(ct);
        var totalSizeBytes = await scope.SumAsync(f => (long?)f.SizeBytes, ct) ?? 0L;

        var rows = await scope.OrderByDescending(f => f.DeletedAt ?? f.CreatedAt).ThenBy(f => f.Id)
            .Skip((request.PageNumber - 1) * request.PageSize).Take(request.PageSize + 1)
            .Select(f => new { f.Id, f.DocumentVersion, f.AuthorizationVersion, f.OwnerId,
                Item = new TrashFileItemDto(f.Id, f.TenantId, f.OwnerId, f.Owner != null ? (f.Owner.FirstName + " " + f.Owner.LastName).Trim() : "Không xác định",
                    f.FolderId, f.Name, f.ContentType, f.SizeBytes, f.CreatedAt, f.DeletedAt, f.DeletedByUserId,
                    f.DeletedByUser != null ? (f.DeletedByUser.FirstName + " " + f.DeletedByUser.LastName).Trim() : null,
                    f.DepartmentId, f.Department != null ? f.Department.Name : null,
                    f.StorageProvider != null ? f.StorageProvider.Name : null) })
            .ToArrayAsync(ct);
        var ids = rows.Select(f => f.Id).ToArray();
        var final = await scope.Where(f => ids.Contains(f.Id))
            .Select(f => new { f.Id, f.DocumentVersion, f.AuthorizationVersion, f.OwnerId }).ToArrayAsync(ct);
        if (rows.Any(f => !final.Any(v => v.Id == f.Id && v.DocumentVersion == f.DocumentVersion
            && v.AuthorizationVersion == f.AuthorizationVersion && v.OwnerId == f.OwnerId)))
            throw new AuthorizationChangedException();
        var after = await ReadAuthorityAsync(context, ct);
        if (before.Decision != after.Decision || before.Stamp is null || after.Stamp is null
            || !FileAccessService.SameStamp(before.Stamp, after.Stamp)) throw new AuthorizationChangedException();
        if (await resolver.ResolveContextAsync(ct) != context) throw new AuthorizationChangedException();
        var items = rows.Take(request.PageSize).Select(f => f.Item).ToList();
        return Result<TrashFileListDto>.Success(new(items, totalCount, request.PageNumber,
            request.PageSize, totalSizeBytes, rows.Length > request.PageSize));
    }

    private async Task<AuthorizationResult> ReadAuthorityAsync(FileAccessContext context, CancellationToken ct)
    {
        var input = await snapshots.ReadAsync(context.Actor, new("none", null), ct);
        var result = evaluator.Evaluate(input with
        {
            Resource = new(context.TenantId, context.TenantId, null, Guid.Empty, FileLifecycle.Binned, 0)
        }, PermissionNames.FileMetadataRead);
        if (result.Decision == AuthorizationDecision.Unavailable) throw new SecurityUnavailableException();
        if (result.Decision == AuthorizationDecision.Unauthenticated || input.User is not { IsActive: true }
            || input.User.UserId != context.Actor.UserId || input.User.TenantId != context.TenantId
            || input.Session?.SessionId != context.Actor.SessionId) throw new AuthorizationChangedException();
        return result;
    }
}
