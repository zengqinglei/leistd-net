using Leistd.UnitOfWork.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Leistd.UnitOfWork;

/// <inheritdoc/>
public class UnitOfWorkManager(
    IOptions<UnitOfWorkOptions> defaultUowOptions,
    IServiceProvider serviceProvider,
    IAmbientUnitOfWork ambientUnitOfWork,
    ILogger<UnitOfWorkManager>? logger = null) : IUnitOfWorkManager
{
    /// <inheritdoc />
    public IUnitOfWork? Current => GetCurrentUnitOfWork();

    /// <inheritdoc />
    public Task<IUnitOfWork> BeginAsync(UnitOfWorkOptions? options = null, bool requiresNew = false)
    {
        // 新建工作单元前定案本次选项，避免修改共享的默认实例。
        var effectiveOptions = options?.Clone() ?? defaultUowOptions.Value.Clone();
        var currentUow = GetCurrentUnitOfWork();
        if (currentUow != null && !requiresNew)
        {
            logger?.LogDebug("Reusing existing unit of work {UowId} (creating a child unit of work)", currentUow.Id);
            return Task.FromResult<IUnitOfWork>(new ChildUnitOfWork(currentUow));
        }

        var unitOfWork = CreateNewUnitOfWork(effectiveOptions);

        logger?.LogDebug("Created new unit of work {UowId}", unitOfWork.Id);

        return Task.FromResult(unitOfWork);
    }

    private IUnitOfWork? GetCurrentUnitOfWork()
    {
        var uow = ambientUnitOfWork.Get();

        while (uow != null && (uow.IsDisposed || uow.IsCompleted))
        {
            uow = uow.Outer;
        }

        return uow;
    }

    private IUnitOfWork CreateNewUnitOfWork(UnitOfWorkOptions effectiveOptions)
    {
        var scope = serviceProvider.CreateScope();
        try
        {
            var outerUow = ambientUnitOfWork.Get();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            // 初始化必须在这个 try 里、且在发布 ambient 之前完成：它抛错时调用方还没拿到
            // 可释放的句柄，若此前已把实例设成当前工作单元，作用域没人回收，而后续代码看到的
            // 「当前工作单元」是一个初始化失败的实例。自定义实现的 Initialize 可以抛。
            unitOfWork.SetOuter(outerUow);
            unitOfWork.Initialize(effectiveOptions);

            // 新工作单元释放时恢复外层环境并回收其独立作用域。按接口订阅而不是按具体类型：
            // 之前这里是 `if (unitOfWork is DefaultUnitOfWork)`，宿主换掉 IUnitOfWork 的实现后
            // 这个分支静默不成立——作用域再也不回收，环境工作单元也停在已释放的那个实例上。
            unitOfWork.Disposed += (sender, args) =>
            {
                ambientUnitOfWork.Set(outerUow);
                scope.Dispose();
            };

            // 全部就绪后才发布：在此之前失败的话，外层 ambient 从未被覆盖过。
            ambientUnitOfWork.Set(unitOfWork);
            return unitOfWork;
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }
}
