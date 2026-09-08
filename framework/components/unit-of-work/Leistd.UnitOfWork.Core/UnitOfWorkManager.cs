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

        var unitOfWork = CreateNewUnitOfWork();
        unitOfWork.Initialize(effectiveOptions);

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

    private IUnitOfWork CreateNewUnitOfWork()
    {
        var scope = serviceProvider.CreateScope();
        try
        {
            var outerUow = ambientUnitOfWork.Get();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            unitOfWork.SetOuter(outerUow);
            ambientUnitOfWork.Set(unitOfWork);

            // 新工作单元释放时恢复外层环境并回收其独立作用域。
            if (unitOfWork is DefaultUnitOfWork concreteUow)
            {
                concreteUow.Disposed += (sender, args) =>
                {
                    ambientUnitOfWork.Set(outerUow);
                    scope.Dispose();
                };
            }

            return unitOfWork;
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }
}
