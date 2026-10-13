using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Approval.Tests;

public sealed class SqlCommandCounter : DbCommandInterceptor
{
    public List<string> Commands { get; } = [];
    public void Reset() => Commands.Clear();

    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command,
        CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Commands.Add(command.CommandText);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Commands.Add(command.CommandText);
        return ValueTask.FromResult(result);
    }
}
