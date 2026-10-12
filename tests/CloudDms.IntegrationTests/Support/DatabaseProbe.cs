using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CloudDms.IntegrationTests.Support;

public sealed class DatabaseProbe : DbCommandInterceptor
{
    private long _permissionReads;

    public long PermissionReads => Interlocked.Read(ref _permissionReads);

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        CountPermissionRead(command);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        CountPermissionRead(command);
        return ValueTask.FromResult(result);
    }

    private void CountPermissionRead(DbCommand command)
    {
        if (command.CommandText.Contains("UserRoles", StringComparison.OrdinalIgnoreCase)
            || command.CommandText.Contains("RolePermissions", StringComparison.OrdinalIgnoreCase)
            || command.CommandText.Contains("FileShares", StringComparison.OrdinalIgnoreCase))
        {
            Interlocked.Increment(ref _permissionReads);
        }
    }
}
