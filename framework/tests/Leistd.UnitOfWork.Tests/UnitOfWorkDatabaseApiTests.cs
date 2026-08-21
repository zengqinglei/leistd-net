using Leistd.UnitOfWork.Core.Database;
using Leistd.UnitOfWork.Core.Options;
using Leistd.UnitOfWork.Core.Uow;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using CoreUnitOfWork = Leistd.UnitOfWork.Core.Uow.UnitOfWork;

namespace Leistd.UnitOfWork.Tests;

public class UnitOfWorkDatabaseApiTests
{
    [Fact]
    public async Task Complete_saves_and_commits_every_registered_api()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        using var unitOfWork = new CoreUnitOfWork(provider, new UnitOfWorkOptions());
        var firstDatabase = new TrackingDatabaseApi();
        var secondDatabase = new TrackingDatabaseApi();
        var firstTransaction = new TrackingTransactionApi();
        var secondTransaction = new TrackingTransactionApi();

        unitOfWork.AddDatabaseApi("first", firstDatabase);
        unitOfWork.AddDatabaseApi("second", secondDatabase);
        unitOfWork.AddTransactionApi("first", firstTransaction);
        unitOfWork.AddTransactionApi("second", secondTransaction);

        await unitOfWork.CompleteAsync();

        Assert.Equal(1, firstDatabase.SaveCount);
        Assert.Equal(1, secondDatabase.SaveCount);
        Assert.Equal(1, firstTransaction.CommitCount);
        Assert.Equal(1, secondTransaction.CommitCount);
    }

    [Fact]
    public async Task Rollback_and_dispose_cover_every_registered_api()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var unitOfWork = new CoreUnitOfWork(provider, new UnitOfWorkOptions());
        var databases = new[] { new TrackingDatabaseApi(), new TrackingDatabaseApi() };
        var transactions = new[] { new TrackingTransactionApi(), new TrackingTransactionApi() };

        unitOfWork.AddDatabaseApi("first", databases[0]);
        unitOfWork.AddDatabaseApi("second", databases[1]);
        unitOfWork.AddTransactionApi("first", transactions[0]);
        unitOfWork.AddTransactionApi("second", transactions[1]);

        await unitOfWork.RollbackAsync();
        unitOfWork.Dispose();

        Assert.All(databases, api =>
        {
            Assert.Equal(1, api.RollbackCount);
            Assert.True(api.IsDisposed);
        });
        Assert.All(transactions, api =>
        {
            Assert.Equal(1, api.RollbackCount);
            Assert.True(api.IsDisposed);
        });
    }

    [Fact]
    public void Database_and_transaction_apis_are_addressed_by_stable_keys()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        using var unitOfWork = new CoreUnitOfWork(provider, new UnitOfWorkOptions());
        var firstDatabase = new TrackingDatabaseApi();
        var secondDatabase = new TrackingDatabaseApi();
        var firstTransaction = new TrackingTransactionApi();
        var secondTransaction = new TrackingTransactionApi();

        unitOfWork.AddDatabaseApi("first", firstDatabase);
        unitOfWork.AddDatabaseApi("second", secondDatabase);
        unitOfWork.AddTransactionApi("first", firstTransaction);
        unitOfWork.AddTransactionApi("second", secondTransaction);

        Assert.Same(firstDatabase, unitOfWork.FindDatabaseApi("first"));
        Assert.Same(secondDatabase, unitOfWork.FindDatabaseApi("second"));
        Assert.Same(firstTransaction, unitOfWork.FindTransactionApi("first"));
        Assert.Same(secondTransaction, unitOfWork.FindTransactionApi("second"));
        Assert.Throws<InvalidOperationException>(() => unitOfWork.AddDatabaseApi("first", secondDatabase));
        Assert.Throws<InvalidOperationException>(() => unitOfWork.AddTransactionApi("first", secondTransaction));
    }

    private sealed class TrackingDatabaseApi : IDatabaseApi, ISupportsSavingChanges, ISupportsRollback
    {
        public int SaveCount { get; private set; }
        public int RollbackCount { get; private set; }
        public bool IsDisposed { get; private set; }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return Task.CompletedTask;
        }

        public Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            RollbackCount++;
            return Task.CompletedTask;
        }

        public void Dispose() => IsDisposed = true;
    }

    private sealed class TrackingTransactionApi : ITransactionApi, ISupportsRollback
    {
        public int CommitCount { get; private set; }
        public int RollbackCount { get; private set; }
        public bool IsDisposed { get; private set; }

        public Task CommitAsync()
        {
            CommitCount++;
            return Task.CompletedTask;
        }

        public Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            RollbackCount++;
            return Task.CompletedTask;
        }

        public void Dispose() => IsDisposed = true;
    }
}
