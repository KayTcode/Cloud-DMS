using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Models;
using CleanArchCqrs.Application.Features.Files.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CleanArchCqrs.Application.Features.Files.Queries.GetTrashFiles;

public record GetTrashFilesQuery : IRequest<Result<TrashFileListDto>>
{
    public Guid? TenantId { get; init; }
    public Guid? DepartmentId { get; init; }
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 20;
    public string? SearchTerm { get; init; }
}

public class GetTrashFilesQueryHandler : IRequestHandler<GetTrashFilesQuery, Result<TrashFileListDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public GetTrashFilesQueryHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<Result<TrashFileListDto>> Handle(GetTrashFilesQuery request, CancellationToken cancellationToken)
    {
        var isSystemAdmin = _currentUserService.Role == "SystemAdmin";
        Guid? tenantId;

        if (isSystemAdmin)
        {
            if (request.TenantId.HasValue)
            {
                tenantId = request.TenantId.Value;
            }
            else
            {
                return Result<TrashFileListDto>.Failure("Vui lòng chỉ định Tổ chức (Tenant) để xem thùng rác.");
            }
        }
        else
        {
            tenantId = _currentUserService.TenantId;
            if (!tenantId.HasValue)
            {
                var userId = _currentUserService.UserId;
                if (userId.HasValue)
                {
                    var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId.Value, cancellationToken);
                    tenantId = user?.TenantId;
                }
            }
        }

        if (!tenantId.HasValue)
        {
            return Result<TrashFileListDto>.Failure("Không xác định được tổ chức/tenant.");
        }

        // Lọc tất cả các file có IsDeleted == true trong Tenant
        var query = _context.FileEntries
            .AsNoTracking()
            .Include(f => f.Owner)
            .Include(f => f.DeletedByUser)
            .Include(f => f.Department)
            .Include(f => f.StorageProvider)
            .Where(f => f.TenantId == tenantId.Value && f.IsDeleted);

        // Với Employee: chỉ xem file của bản thân đã chuyển vào thùng rác
        if (_currentUserService.Role == "Employee")
        {
            var callerId = _currentUserService.UserId;
            if (callerId.HasValue)
            {
                query = query.Where(f => f.OwnerId == callerId.Value || f.DeletedByUserId == callerId.Value);
            }
        }

        // Với DepartmentAdmin: xem file trong phòng ban của mình
        if (_currentUserService.Role == "DepartmentAdmin" && _currentUserService.DepartmentId.HasValue)
        {
            query = query.Where(f => f.DepartmentId == _currentUserService.DepartmentId.Value);
        }

        // Lọc thêm theo departmentId nếu request chỉ định
        if (request.DepartmentId.HasValue)
        {
            query = query.Where(f => f.DepartmentId == request.DepartmentId.Value);
        }

        // Tìm kiếm theo tên file nếu có
        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            var pattern = $"%{request.SearchTerm.Trim()}%";
            query = query.Where(f => EF.Functions.Like(f.Name, pattern));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var totalSizeBytes = await query.SumAsync(f => f.SizeBytes, cancellationToken);

        var pageNumber = request.PageNumber <= 0 ? 1 : request.PageNumber;
        var pageSize = Math.Clamp(request.PageSize <= 0 ? 20 : request.PageSize, 1, 100);

        var items = await query
            .OrderByDescending(f => f.DeletedAt ?? f.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(f => new TrashFileItemDto(
                f.Id,
                f.TenantId,
                f.OwnerId,
                f.Owner.FirstName + " " + f.Owner.LastName,
                f.FolderId,
                f.Name,
                f.ContentType,
                f.SizeBytes,
                f.CreatedAt,
                f.DeletedAt,
                f.DeletedByUserId,
                f.DeletedByUser != null ? (f.DeletedByUser.FirstName + " " + f.DeletedByUser.LastName).Trim() : null,
                f.DepartmentId,
                f.Department != null ? f.Department.Name : null,
                f.StorageProvider != null ? f.StorageProvider.Name : null
            ))
            .ToListAsync(cancellationToken);

        var response = new TrashFileListDto(
            items,
            totalCount,
            pageNumber,
            pageSize,
            totalSizeBytes
        );

        return Result<TrashFileListDto>.Success(response);
    }
}
