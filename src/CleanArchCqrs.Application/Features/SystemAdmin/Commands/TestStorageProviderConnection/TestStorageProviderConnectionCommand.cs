using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Domain.Entities;
using MediatR;

namespace CleanArchCqrs.Application.Features.SystemAdmin.Commands.TestStorageProviderConnection;

public record TestStorageProviderConnectionCommand(
    StorageProviderType Type,
    string ConfigJson
) : IRequest<TestConnectionResult>;

public record TestConnectionResult(
    bool Success,
    string Message,
    long TotalCapacityBytes = 0,
    long UsedCapacityBytes = 0,
    long AvailableCapacityBytes = 0,
    long LatencyMs = 0
);

public class TestStorageProviderConnectionCommandHandler : IRequestHandler<TestStorageProviderConnectionCommand, TestConnectionResult>
{
    private readonly IStorageServiceFactory _storageServiceFactory;

    public TestStorageProviderConnectionCommandHandler(IStorageServiceFactory storageServiceFactory)
    {
        _storageServiceFactory = storageServiceFactory;
    }

    public async Task<TestConnectionResult> Handle(TestStorageProviderConnectionCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var adapter = _storageServiceFactory.CreateServiceFromRawConfig(request.Type, request.ConfigJson);
            using var adapterLease = adapter as IDisposable;
            var quota = await adapter.GetProviderQuotaAsync(cancellationToken);
            sw.Stop();

            return new TestConnectionResult(
                Success: true,
                Message: request.Type == StorageProviderType.S3
                    ? "Kết nối S3 thành công; dung lượng là hạn mức cấu hình, số byte đã dùng lấy từ DB."
                    : $"Kết nối {request.Type} thành công!",
                TotalCapacityBytes: quota.TotalCapacityBytes,
                UsedCapacityBytes: request.Type == StorageProviderType.S3 ? 0 : quota.UsedCapacityBytes,
                AvailableCapacityBytes: request.Type == StorageProviderType.S3 ? 0 : quota.AvailableCapacityBytes,
                LatencyMs: sw.ElapsedMilliseconds
            );
        }
        catch (Exception ex)
        {
            return new TestConnectionResult(
                Success: false,
                Message: $"Lỗi kiểm tra kết nối: {ex.Message}"
            );
        }
    }
}
