using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Reflection;
using Leistd.MultiTenancy;
using Leistd.UnitOfWork.Core.Uow;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Leistd.UnitOfWork.EfCore.Database;

/// <summary>
/// 在工作单元内按 DbContext 类型复用实例，并在创建前异步解析租户连接。
/// </summary>
public class DbContextProvider<TDbContext>(
    IUnitOfWorkManager unitOfWorkManager,
    IServiceProvider serviceProvider) : IDbContextProvider<TDbContext>
    where TDbContext : DbContext
{
    private static readonly string ConnectionStringName =
        typeof(TDbContext).GetCustomAttribute<TenantConnectionStringNameAttribute>()?.Name ?? "Default";
    private static readonly string DatabaseApiKey = $"EfCoreDbContext:{typeof(TDbContext).FullName}";

    /// <inheritdoc />
    public async Task<TDbContext> GetDbContextAsync(CancellationToken cancellationToken = default)
    {
        var unitOfWork = unitOfWorkManager.Current;
        if (unitOfWork is null)
        {
            return await CreateDbContextAsync(serviceProvider, existingConnection: null, cancellationToken);
        }

        var uowServiceProvider = GetServiceProvider(unitOfWork);
        var binding = uowServiceProvider.GetRequiredService<UnitOfWorkConnectionBinding>();
        var tenantId = uowServiceProvider.GetService<ICurrentTenant>()?.Id;
        binding.EnsureTenant(tenantId);

        if (unitOfWork.FindDatabaseApi(DatabaseApiKey) is EfCoreDatabaseApi<TDbContext> existingApi)
        {
            return existingApi.DbContext;
        }

        var resolvedConnectionString = await ResolveConnectionStringAsync(uowServiceProvider, cancellationToken);
        var targetKey = resolvedConnectionString is null ? binding.TargetKey : CreateTargetKey(resolvedConnectionString);
        var activeTransaction = targetKey is null
            ? null
            : unitOfWork.FindTransactionApi($"EfCoreTransaction:{targetKey}") as EfCoreTransactionApi;
        var dbContext = await CreateDbContextAsync(
            uowServiceProvider,
            activeTransaction?.DbContextTransaction.GetDbTransaction().Connection,
            cancellationToken,
            resolvedConnectionString);

        var actualTargetKey = dbContext.Database.IsRelational()
            ? CreateTargetKey(dbContext.Database.GetConnectionString())
            : $"NonRelational:{typeof(TDbContext).FullName}:{dbContext.Database.ProviderName}";
        binding.Bind(tenantId, actualTargetKey);
        targetKey ??= actualTargetKey;

        if (!string.Equals(targetKey, actualTargetKey, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The DbContext configuration did not use the resolved physical database target.");
        }

        var transactionKey = $"EfCoreTransaction:{targetKey}";
        activeTransaction ??= unitOfWork.FindTransactionApi(transactionKey) as EfCoreTransactionApi;

        ApplyTimeout(dbContext, unitOfWork);

        if (unitOfWork.Options.IsTransactional)
        {
            await EnlistOrBeginTransactionAsync(
                unitOfWork,
                transactionKey,
                dbContext,
                activeTransaction,
                cancellationToken);
        }

        unitOfWork.AddDatabaseApi(DatabaseApiKey, new EfCoreDatabaseApi<TDbContext>(dbContext));
        return dbContext;
    }

    private static async Task EnlistOrBeginTransactionAsync(
        IUnitOfWork unitOfWork,
        string transactionKey,
        TDbContext dbContext,
        EfCoreTransactionApi? activeTransaction,
        CancellationToken cancellationToken)
    {
        if (activeTransaction is null)
        {
            var transaction = unitOfWork.Options.IsolationLevel.HasValue
                ? await dbContext.Database.BeginTransactionAsync(unitOfWork.Options.IsolationLevel.Value, cancellationToken)
                : await dbContext.Database.BeginTransactionAsync(cancellationToken);

            unitOfWork.AddTransactionApi(transactionKey, new EfCoreTransactionApi(transaction, dbContext));
            return;
        }

        if (!dbContext.Database.IsRelational())
        {
            throw new InvalidOperationException(
                "Multiple transactional DbContext types require a relational provider that can share a connection.");
        }

        await dbContext.Database.UseTransactionAsync(
            activeTransaction.DbContextTransaction.GetDbTransaction(),
            cancellationToken);
        activeTransaction.AttendedDbContexts.Add(dbContext);
    }

    private static async Task<TDbContext> CreateDbContextAsync(
        IServiceProvider provider,
        DbConnection? existingConnection,
        CancellationToken cancellationToken,
        string? resolvedConnectionString = null)
    {
        var connectionString = resolvedConnectionString ??
            await ResolveConnectionStringAsync(provider, cancellationToken);

        if (connectionString is null && existingConnection is null)
        {
            return provider.GetRequiredService<TDbContext>();
        }

        using (DbContextCreationContext.Change(connectionString ?? existingConnection!.ConnectionString, existingConnection))
        {
            return provider.GetRequiredService<TDbContext>();
        }
    }

    private static async Task<string?> ResolveConnectionStringAsync(
        IServiceProvider provider,
        CancellationToken cancellationToken)
    {
        var resolver = provider.GetService<ITenantConnectionStringResolver>();
        if (resolver is null)
        {
            return null;
        }

        var connectionString = await resolver.ResolveAsync(ConnectionStringName, cancellationToken);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("The tenant connection string resolver returned an empty value.");
        }

        return connectionString;
    }

    private static string CreateTargetKey(string? connectionString)
    {
        if (connectionString is null)
        {
            return $"ConfiguredDbContext:{typeof(TDbContext).FullName}";
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(connectionString));
        return Convert.ToHexString(bytes);
    }

    private static void ApplyTimeout(TDbContext dbContext, IUnitOfWork unitOfWork)
    {
        if (unitOfWork.Options.Timeout.HasValue &&
            dbContext.Database.IsRelational() &&
            !dbContext.Database.GetCommandTimeout().HasValue)
        {
            dbContext.Database.SetCommandTimeout((int)unitOfWork.Options.Timeout.Value.TotalSeconds);
        }
    }

    private static IServiceProvider GetServiceProvider(IUnitOfWork unitOfWork)
    {
        if (unitOfWork is Core.Uow.UnitOfWork concreteUnitOfWork)
        {
            return concreteUnitOfWork.ServiceProvider;
        }

        throw new InvalidOperationException(
            $"Cannot get ServiceProvider from unit of work type '{unitOfWork.GetType().FullName}'.");
    }
}
