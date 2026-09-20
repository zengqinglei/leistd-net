using Leistd.BackgroundJobs.EntityFrameworkCore;
using Leistd.BackgroundJobs.EntityFrameworkCore.Stores;
using Leistd.BackgroundJobs.InProcess;
using Leistd.BackgroundJobs.InProcess.Recurring;
using Leistd.BackgroundJobs.Queues;
using Leistd.BackgroundJobs.Recurring;
using Leistd.BackgroundJobs.Tests.TestDoubles;
using Leistd.TestBase.Assertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Leistd.BackgroundJobs.Tests;

/// <summary>
/// 登记面：任务名全局唯一、范围必须显式、调度器与水位存储的注册与调用顺序无关。
/// </summary>
public class RecurringJobRegistrationTests
{
    private static readonly RecurringJobSchedule Hourly = RecurringJobSchedule.Every(TimeSpan.FromHours(1));

    [Fact]
    public void A_job_is_registered_as_a_definition_and_a_transient_service()
    {
        var services = new ServiceCollection().AddRecurringJob<FailingJob>("test.failing", Hourly, RecurringJobScope.Cluster);

        var definition = Assert.Single(services.Select(d => d.ImplementationInstance).OfType<RecurringJobDefinition>());
        Assert.Equal(("test.failing", typeof(FailingJob), RecurringJobScope.Cluster), (definition.Name, definition.JobType, definition.Scope));
        services.AssertSingle<FailingJob>(ServiceLifetime.Transient);
    }

    [Fact]
    public void Registering_the_same_job_twice_is_idempotent()
    {
        ServiceCollectionAssertions.AssertIdempotent(
            services => services.AddRecurringJob<FailingJob>("test.failing", Hourly, RecurringJobScope.Cluster));
    }

    /// <summary>名字同时是集群锁与水位的键：两个任务共用一个名字会互相跳过，而且不报错。</summary>
    [Fact]
    public void Two_jobs_cannot_share_a_name()
    {
        var services = new ServiceCollection().AddRecurringJob<FailingJob>("test.shared", Hourly, RecurringJobScope.Cluster);

        Assert.Throws<InvalidOperationException>(
            () => services.AddRecurringJob<CountingJob>("test.shared", Hourly, RecurringJobScope.Cluster));
    }

    /// <summary>范围没有默认值：<c>default</c> 不是合法取值，漏选在登记时就失败。</summary>
    [Fact]
    public void The_scope_must_be_chosen_explicitly()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ServiceCollection().AddRecurringJob<FailingJob>("test.failing", Hourly, default));
    }

    [Fact]
    public void The_in_process_implementation_registers_one_scheduler_and_one_queue()
    {
        ServiceCollectionAssertions.AssertIdempotent(services => services.AddInProcessBackgroundJobs());

        var services = new ServiceCollection().AddInProcessBackgroundJobs().AddInProcessBackgroundJobs();
        Assert.Single(services, d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(RecurringJobScheduler));
        services.AssertSingle<IBackgroundTaskQueue>(ServiceLifetime.Singleton);
    }

    /// <summary>EF 水位存储总是替换进程内默认实现，与调度器注册的先后无关。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_ef_state_store_wins_regardless_of_order(bool efFirst)
    {
        var services = new ServiceCollection();
        if (efFirst)
        {
            services.AddBackgroundJobsEfCore<DbContext>();
        }

        services.AddInProcessBackgroundJobs();
        if (!efFirst)
        {
            services.AddBackgroundJobsEfCore<DbContext>();
        }

        services.AssertImplementedBy<IRecurringJobStateStore, EfCoreRecurringJobStateStore<DbContext>>();
    }
}
