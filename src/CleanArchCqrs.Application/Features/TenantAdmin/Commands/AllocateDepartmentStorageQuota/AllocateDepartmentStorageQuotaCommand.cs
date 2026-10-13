using System.Data;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Models;
using static CleanArchCqrs.Application.Common.Utils.ByteFormatter;
using CleanArchCqrs.Application.Features.TenantAdmin.DTOs;
using CleanArchCqrs.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CleanArchCqrs.Application.Features.TenantAdmin.Commands.AllocateDepartmentStorageQuota;

public record AllocateDepartmentStorageQuotaCommand(
    Guid TenantId,
    Guid DepartmentId,
    long QuotaBytes
) : IRequest<Result<DepartmentStorageQuotaDto>>;

public class AllocateDepartmentStorageQuotaCommandHandler : IRequestHandler<AllocateDepartmentStorageQuotaCommand, Result<DepartmentStorageQuotaDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public AllocateDepartmentStorageQuotaCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<Result<DepartmentStorageQuotaDto>> Handle(
        AllocateDepartmentStorageQuotaCommand request,
        CancellationToken cancellationToken)
    {
        if (request.QuotaBytes < 0)
        {
            return Result<DepartmentStorageQuotaDto>.Failure("Hạn mức dung lượng không được là số âm.");
        }

        var strategy = _context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);

            try
            {
                var tenant = await _context.Tenants
                    .FirstOrDefaultAsync(t => t.Id == request.TenantId, cancellationToken);

                if (tenant == null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return Result<DepartmentStorageQuotaDto>.Failure("Không tìm thấy thông tin Tổ chức (Tenant).");
                }

                var department = await _context.Departments
                    .FirstOrDefaultAsync(d => d.Id == request.DepartmentId && d.TenantId == request.TenantId, cancellationToken);

                if (department == null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return Result<DepartmentStorageQuotaDto>.Failure("Không tìm thấy Phòng ban tương ứng trong tổ chức.");
                }

                // 1. Kiểm tra: Không được hạ quota thấp hơn dung lượng phòng ban đã thực tế sử dụng
                if (request.QuotaBytes < department.StorageUsedBytes)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return Result<DepartmentStorageQuotaDto>.Failure(
                        $"Không thể hạ hạn mức phòng ban xuống thấp hơn dung lượng phòng ban đã sử dụng ({FormatBytes(department.StorageUsedBytes)}).");
                }

                // 2. Kiểm tra: Không được hạ quota thấp hơn tổng Quota đã chia cho các User trong phòng ban
                var totalAllocatedToUsers = await _context.UserStorageQuotas
                    .Where(q => q.User.DepartmentId == department.Id)
                    .SumAsync(q => q.QuotaBytes, cancellationToken);

                if (request.QuotaBytes < totalAllocatedToUsers)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return Result<DepartmentStorageQuotaDto>.Failure(
                        $"Không thể hạ hạn mức phòng ban xuống thấp hơn tổng hạn mức đã cấp cho các thành viên trong phòng ({FormatBytes(totalAllocatedToUsers)}). Hãy thu hồi bớt hạn mức của thành viên trước.");
                }

                // 3. Kiểm tra: Tổng hạn mức của tất cả phòng ban không được vượt quá hạn mức Tenant
                var otherDepartmentsQuota = await _context.Departments
                    .Where(d => d.TenantId == tenant.Id && d.Id != department.Id)
                    .SumAsync(d => d.QuotaBytes, cancellationToken);

                var availableInTenant = Math.Max(0, tenant.StorageQuotaBytes - otherDepartmentsQuota);

                if (request.QuotaBytes > availableInTenant)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return Result<DepartmentStorageQuotaDto>.Failure(
                        $"Vượt quá hạn mức lưu trữ khả dụng của Tổ chức! Tổ chức chỉ còn trống {FormatBytes(availableInTenant)} để cấp thêm cho phòng ban này.");
                }

                long oldQuota = department.StorageQuotaBytes;
                department.StorageQuotaBytes = request.QuotaBytes;

                // Ghi AuditLog
                var auditLog = new AuditLog
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenant.Id,
                    UserId = _currentUserService.UserId,
                    Action = "AllocateDepartmentQuota",
                    EntityName = "Department",
                    EntityId = department.Id,
                    Details = $"Cập nhật hạn mức lưu trữ phòng ban '{department.Name}' từ {FormatBytes(oldQuota)} thành {FormatBytes(request.QuotaBytes)}.",
                    CreatedAt = DateTime.UtcNow
                };
                _context.AuditLogs.Add(auditLog);

                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                var dto = new DepartmentStorageQuotaDto
                {
                    DepartmentId = department.Id,
                    TenantId = department.TenantId,
                    DepartmentName = department.Name,
                    DepartmentCode = department.Code,
                    StorageQuotaBytes = department.StorageQuotaBytes,
                    StorageUsedBytes = department.StorageUsedBytes,
                    TotalAllocatedToUsersBytes = totalAllocatedToUsers
                };

                return Result<DepartmentStorageQuotaDto>.Success(dto);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(cancellationToken);
                _context.ChangeTracker.Clear();
                return Result<DepartmentStorageQuotaDto>.Failure(ex.Message);
            }
        });
    }
}
