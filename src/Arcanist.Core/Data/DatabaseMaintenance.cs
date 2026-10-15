using System.Data.Common;
using Arcanist.Core.Api;
using Arcanist.Core.Application;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Arcanist.Core.Data;

/// <summary>What the panel shows while the server is in maintenance, to anyone (it reveals nothing else).</summary>
/// <param name="Percent">0–100, or null while it cannot tell.</param>
public sealed record MaintenanceState(bool Active, string? Phase, int? Percent);

/// <summary>
/// Pauses the server's writes while its database is copied to another one. Background work and requests run as
/// units of work (<see cref="WorkAsync"/>): when maintenance starts, new units wait until it ends, and the ones
/// running are let finish. Once they have (<see cref="IsFrozen"/>), any write to the database is refused, as a
/// safety net, except the migrator's own.
/// </summary>
public sealed class DatabaseMaintenance
{
    private readonly Lock _lock = new();
    private int _running;
    private bool _active;
    private bool _frozen;
    private TaskCompletionSource _resumed = Completed();
    private TaskCompletionSource? _drained;

    public bool IsActive => Volatile.Read(ref _active);

    /// <summary>Maintenance is on and no unit of work is running: the database must not change.</summary>
    public bool IsFrozen => Volatile.Read(ref _frozen);

    /// <summary>Set by the migrator, for <see cref="State"/>.</summary>
    public string? Phase { get; set; }

    public int? Percent { get; set; }

    public MaintenanceState State => new(IsActive, IsActive ? Phase : null, IsActive ? Percent : null);

    /// <summary>
    /// Waits while maintenance is on, then counts the caller's work until disposed. Work that waits for other work
    /// while maintenance starts makes <see cref="EnterAsync"/> time out, rather than deadlock.
    /// </summary>
    public async ValueTask<IDisposable> WorkAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            Task resumed;
            lock (_lock)
            {
                if (!_active)
                {
                    _running++;
                    return new Unit(this);
                }

                resumed = _resumed.Task;
            }

            await resumed.WaitAsync(cancellationToken);
        }
    }

    /// <summary>Starts maintenance and waits for the running work to finish. False (and no maintenance) when it does not in time.</summary>
    public async Task<bool> EnterAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        TaskCompletionSource drained;
        lock (_lock)
        {
            if (_active)
                throw new InvalidOperationException("Maintenance is already on.");
            _active = true;
            _resumed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _drained = drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if (_running == 0)
                drained.TrySetResult();
        }

        try
        {
            await drained.Task.WaitAsync(timeout, cancellationToken);
            Volatile.Write(ref _frozen, true);
            return true;
        }
        catch (Exception e) when (e is TimeoutException or OperationCanceledException)
        {
            Leave();
            if (e is OperationCanceledException)
                throw;
            return false;
        }
    }

    public void Leave()
    {
        lock (_lock)
        {
            Volatile.Write(ref _frozen, false);
            _active = false;
            Phase = null;
            Percent = null;
            _resumed.TrySetResult();
        }
    }

    private void Done()
    {
        lock (_lock)
        {
            _running--;
            if (_running == 0 && _active)
                _drained?.TrySetResult();
        }
    }

    private static TaskCompletionSource Completed()
    {
        var done = new TaskCompletionSource();
        done.SetResult();
        return done;
    }

    private sealed class Unit(DatabaseMaintenance owner) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                owner.Done();
        }
    }
}

/// <summary>Refuses writes while the database is frozen for maintenance. Contexts of the migrator do not have it.</summary>
internal sealed class MaintenanceInterceptor(DatabaseMaintenance maintenance) : DbCommandInterceptor
{
    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Check(command);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Check(command);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        Check(command);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Check(command);
        return ValueTask.FromResult(result);
    }

    private void Check(DbCommand command)
    {
        if (maintenance.IsFrozen && !IsRead(command.CommandText))
            throw new ArcanistException.Unavailable(ErrorCodes.Maintenance);
    }

    private static bool IsRead(string sql)
    {
        var text = sql.AsSpan().TrimStart();
        return text.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
               || text.StartsWith("PRAGMA", StringComparison.OrdinalIgnoreCase)
               || text.StartsWith("EXPLAIN", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// While the server is in maintenance, answers 503 <c>maintenance</c> to everything but the panel's files,
/// <c>/health</c> and the maintenance state. Otherwise, runs each request as a unit of work, so maintenance waits
/// for requests that may write; streams and WebSockets, which only read and last long, are left out.
/// </summary>
internal sealed class MaintenanceMiddleware(RequestDelegate next)
{
    public const string StatePath = "/rest/v1/maintenance";

    public async Task InvokeAsync(HttpContext context, DatabaseMaintenance maintenance)
    {
        var path = context.Request.Path;
        var allowed = Allowed(context.Request);
        if (maintenance.IsActive && !allowed)
        {
            context.Response.Headers.RetryAfter = "30";
            await Problems.Create(StatusCodes.Status503ServiceUnavailable, ErrorCodes.Maintenance).ExecuteAsync(context);
            return;
        }

        // What is allowed during maintenance must not wait for it either.
        if (allowed || context.WebSockets.IsWebSocketRequest || P2P.P2PGateMiddleware.IsStream(context.Request)
            || path.StartsWithSegments("/ext/dlna") || path.StartsWithSegments("/rest/v1/artwork"))
        {
            await next(context);
            return;
        }

        using (await maintenance.WorkAsync(context.RequestAborted))
            await next(context);
    }

    private static bool Allowed(HttpRequest request) =>
        request.Path.StartsWithSegments("/admin")
        || request.Path.Equals("/health", StringComparison.OrdinalIgnoreCase)
        || (HttpMethods.IsGet(request.Method) && request.Path.Equals(StatePath, StringComparison.OrdinalIgnoreCase));
}
