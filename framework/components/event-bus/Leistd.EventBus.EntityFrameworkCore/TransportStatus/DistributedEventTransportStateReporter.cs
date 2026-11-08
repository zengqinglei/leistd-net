using Leistd.EventBus.Distributed.Options;
using Leistd.EventBus.Distributed.Transport;
using Leistd.EventBus.EntityFrameworkCore.Entities;
using Leistd.MultiTenancy.Context;
using Leistd.UnitOfWork.EntityFrameworkCore.Database;
using Leistd.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Leistd.EventBus.EntityFrameworkCore.TransportStatus;

internal sealed class DistributedEventTransportStateReporter<TDbContext>(
    IServiceScopeFactory scopes,
    IServiceProviderIsService services,
    IOptionsMonitor<DistributedEventOptions> options,
    TimeProvider clock,
    ILogger<DistributedEventTransportStateReporter<TDbContext>> logger,
    IHostEnvironment? environment = null) : BackgroundService where TDbContext : DbContext
{
    private readonly string _instance = $"{environment?.ApplicationName ?? AppDomain.CurrentDomain.FriendlyName}@{Environment.MachineName}:{Environment.ProcessId}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!services.IsService(typeof(IDistributedEventTransport)) || !services.IsService(typeof(HealthCheckService))) return;
        var interval = TimeSpan.FromSeconds(15);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                interval = options.CurrentValue.TransportStatus.ReportInterval;
                await ReportAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "The distributed event transport status could not be reported; the next interval will retry.");
            }
            try { await Task.Delay(interval, clock, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    internal async Task ReportAsync(CancellationToken ct = default)
    {
        if (!services.IsService(typeof(IDistributedEventTransport)) || !services.IsService(typeof(HealthCheckService))) return;
        await using var scope = scopes.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        using var change = provider.GetRequiredService<ICurrentTenant>().Change(null);
        var report = await provider.GetRequiredService<HealthCheckService>()
            .CheckHealthAsync(registration => registration.Tags.Contains("event-bus"), ct);
        if (report.Entries.Count == 0) return;
        var transport = provider.GetRequiredService<IDistributedEventTransport>();
        var instance = options.CurrentValue.TransportStatus.Instance;
        if (string.IsNullOrEmpty(instance)) instance = _instance;
        using var unit = provider.GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);
        var db = await provider.GetRequiredService<IDbContextProvider<TDbContext>>().GetDbContextAsync(ct);
        var row = await db.Set<DistributedEventTransportState>().SingleOrDefaultAsync(x => x.Instance == instance, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        if (row is null)
        {
            row = new() { Instance = instance, Status = report.Status, StatusChangedAt = now };
            db.Add(row);
        }
        else if (row.Status != report.Status)
        {
            row.Status = report.Status;
            row.StatusChangedAt = now;
        }
        row.TransportName = transport.Name;
        row.ReportedAt = now;
        await unit.CompleteAsync(ct);
    }
}
