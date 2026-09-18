// DbException 与 SqlState 都在 System.Data.Common（BCL）：按它分流不必让应用层
// 引用任何数据库驱动，与 ConnectionStringGuard 选 DbConnectionStringBuilder 是同一条理由。
using System.Data.Common;
using CompanyName.ProjectName.Application.OperationRecords;
using CompanyName.ProjectName.Application.Permissions.Provider;
// ConnectionStringGuard 在 ...Application.TenantConnections，与本文件所在的
// ...Application.Tenants.AppServices 是平行分支而非父子，**不会自动可达**，必须显式 using。
// （同一个守卫在 TenantConnectionAppService 里不需要 using，因为那里的命名空间
// ...TenantConnections.AppServices 确实以它为父——两处情况不同，别照搬。）
// 本文件与守卫同属 !LocalIdentity 场景被裁剪的目录，因此这条 using 不必加条件守卫。
using CompanyName.ProjectName.Application.TenantConnections;
using CompanyName.ProjectName.Application.Tenants.Dtos;
using CompanyName.ProjectName.Domain.Users.Entities;
using Leistd.OperationRecords.Abstractions;
using Leistd.Data.Constants;
using Leistd.Ddd.Application.Contracts.Dtos;
using Leistd.Ddd.Domain.Repositories;
using Leistd.MultiTenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Leistd.UnitOfWork;
using Leistd.ExceptionHandling;
using Leistd.MultiTenancy.ConnectionStrings;
using Leistd.MultiTenancy.Stores;
using Leistd.MultiTenancy.Abstractions;
using Leistd.ObjectMapping.Abstractions;
using Leistd.Ddd.Application.AppService;

namespace CompanyName.ProjectName.Application.Tenants.AppServices;

/// <summary>
/// 租户管理应用服务实现
/// </summary>
/// <remarks>
/// 写路径统一走框架 <see cref="ITenantManager"/>（归一化与唯一性校验在那里收口）；
/// 创建后立即经 <see cref="ICurrentTenant.Change"/> 进入新租户上下文执行 <see cref="ITenantSeeder"/>。
/// </remarks>
public class TenantAppService(
    ITenantManager tenantManager,
    ITenantStore tenantStore,
    ITenantNormalizer tenantNormalizer,
    ITenantConnectionConfigurationManager connectionConfigurationManager,
    ITenantSeeder tenantSeeder,
    ICurrentTenant currentTenant,
    IRepository<User, Guid> userRepository,
    IOperationRecorder operationRecorder,
    IUnitOfWorkManager unitOfWorkManager,
    IServiceScopeFactory serviceScopeFactory,
    IObjectMapper objectMapper,
    ILogger<TenantAppService> logger) : BaseAppService, ITenantAppService
{
    /// <inheritdoc />
    public async Task<PagedResultDto<TenantOutputDto>> GetPagedAsync(
        GetTenantPagedInputDto input,
        CancellationToken cancellationToken = default)
    {
        var page = await tenantManager.GetPagedAsync(input.Keyword, input.Offset, input.Limit, cancellationToken);
        return new PagedResultDto<TenantOutputDto>(page.TotalCount, page.Items.Select(ToOutputDto).ToList());
    }

    /// <inheritdoc />
    public async Task<TenantOutputDto> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var record = await tenantManager.FindAsync(id, cancellationToken)
                     ?? throw new NotFoundException($"Tenant '{id}' not found.");
        return ToOutputDto(record);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>租户先以停用态写入注册表，提交后在租户上下文中播种，完成后才激活。
    /// 注册表与租户数据库不共享事务，因此失败时以幂等补偿清除种子数据并删除注册表记录。
    /// 激活也属于补偿边界；存储直接读库，状态在提交后生效，不存在额外缓存失效步骤。</para>
    /// <para><b>分库与否在播种之前定案，这个顺序是硬的。</b>先播种再登记连接，等于把种子
    /// （含租户管理员）留在回落库里而让解析改指空库——租户建出来就是废的，且旧库里留下一份
    /// 带口令散列的孤儿账号。框架侧同样以
    /// <c>TenantConnectionChangeRequiresInactiveTenantException</c> 堵死这条路。</para>
    /// </remarks>
    public async Task<TenantOutputDto> CreateAsync(CreateTenantInputDto input, CancellationToken cancellationToken = default)
    {
        // 入参校验排在任何库操作之前：连接串填错是这条路径上最常见的一种错误，
        // 没道理先建租户、再靠补偿把它擦掉。框架的 SetAsync 只查"非空/不超长"，
        // 语法非法会一路穿到播种阶段才由数据库驱动抛出，最终以 500 结束——
        // 那是"用户填错一个字段，却收到系统级故障提示"。
        if (!string.IsNullOrWhiteSpace(input.ConnectionString))
        {
            ConnectionStringGuard.EnsureParsable(input.ConnectionString);
        }

        // 登记成功后的连接行版本，仅用于补偿时删除它；没登记连接则保持 null。
        long? connectionVersion = null;

        TenantConfiguration tenant;
        using (var controlUnitOfWork = await unitOfWorkManager.BeginAsync(requiresNew: true))
        {
            tenant = await tenantManager.CreateAsync(
                input.Name,
                input.DisplayName,
                isActive: false,
                input.Description,
                cancellationToken);

            // 带了连接串就地登记，随后的播种解析到的就是那个专属库；留空即不分库，
            // 一条连接都不登记，各服务用自己配置的库（要按服务拆库，再去连接列表里逐条登记）。
            // 与建注册表行同一个工作单元：中途失败不会留下"有租户没连接"或反过来的半截状态。
            if (!string.IsNullOrWhiteSpace(input.ConnectionString))
            {
                var connection = await connectionConfigurationManager.SetAsync(
                    tenant.Id,
                    ConnectionStringNames.Default,
                    input.ConnectionString,
                    expectedVersion: null,
                    cancellationToken);

                // **返回值必须接住。** RemoveAsync 是乐观并发接口，删除要带上读到的版本；
                // 丢掉它，补偿就没有任何办法删掉这一行，只能留下一条指向已删租户的孤儿连接。
                connectionVersion = connection.Version;
            }

            await controlUnitOfWork.CompleteAsync(cancellationToken);
        }

        TenantConfiguration activated;
        try
        {
            // 在新租户上下文内种子：角色、权限授予、租户管理员的所有行由落值拦截器自动归属该租户
            using (currentTenant.Change(tenant.Id, tenant.Name))
            using (var businessUnitOfWork = await unitOfWorkManager.BeginAsync(requiresNew: true))
            {
                await tenantSeeder.SeedAsync(input.AdminEmail, input.AdminPassword, cancellationToken);
                await businessUnitOfWork.CompleteAsync(cancellationToken);
            }

            // 激活失败与播种失败使用同一补偿路径。
            using var activationUnitOfWork = await unitOfWorkManager.BeginAsync(requiresNew: true);
            activated = await tenantManager.SetActiveAsync(tenant.Id, true, cancellationToken);
            await activationUnitOfWork.CompleteAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Tenant {TenantId} initialization failed; rolling back the tenant and seeded data", tenant.Id);
            await CompensateAsync(tenant, connectionVersion);

            // 播种失败最常见的原因是那个专属库用不了：连不上、库不存在、或建好了没迁移。
            // 三种都是调用方自己能改对的，却因为异常出自数据库驱动而一路兜底成
            // 500「系统异常，请联系管理员」——填错一个字段收到系统级故障提示，
            // 与连接串语法错误是同一类问题（见 ConnectionStringGuard）。
            if (DescribeDatabaseFailure(ex) is { } databaseFailure)
            {
                throw databaseFailure;
            }

            throw;
        }

        logger.LogInformation("Tenant {TenantName} ({TenantId}) created and initialized", tenant.Name, tenant.Id);

#if (LocalIdentity)
        // **必须落在三个 requiresNew 工作单元与 CompensateAsync 补偿路径之外的成功点。**
        // 放进任一 using 块内，"创建失败并已回滚"的情况会留下一条"创建成功"的假账——
        // 正是 RecordSucceededAsync 契约里警告的"记了但没发生"。
        // 走到这里意味着注册表、播种、激活三段都已提交。
        //
        // 守卫与 PermissionConstant.Tenants 及本服务的 DI 注册一致：租户控制面只在
        // LocalIdentity 形态存在。不能改写成字面量绕过——"App.Tenants" 是 resource
        // 场景 ForbiddenTokens 里的禁用词，那会把编译错误换成闸门失败。
        await operationRecorder.RecordSucceededAsync(
            OperationRecordActions.TenantCreated,
            OperationTarget.For(tenant.Id, tenant.DisplayName ?? tenant.Name),
            PermissionConstant.Tenants.Create,
            cancellationToken);
#endif

        return ToOutputDto(activated);
    }

    /// <summary>
    /// 把"专属库用不了"翻译成调用方能照着改的 400；不是这类原因则返回 <see langword="null"/>。
    /// </summary>
    /// <remarks>
    /// <para><b>逐层往内找。</b>驱动异常常被 EF 或工作单元包上几层，只看最外层会漏判。</para>
    /// <para><b>一律不回显连接串，也不回显主机与端口。</b>连接串里有数据库口令，
    /// 而错误响应会同时进入响应体、前端提示与服务端日志——三处都撤不回来。
    /// <c>SqlState</c> 是标准错误码、不是机密，兜底支带上它便于排查。</para>
    /// </remarks>
    private static BusinessException? DescribeDatabaseFailure(Exception error)
    {
        for (var current = error; current is not null; current = current.InnerException)
        {
            if (current is not DbException database)
            {
                continue;
            }

            // 连接阶段失败（主机不可达、端口拒绝）不是服务端返回的错误响应，SqlState 为空。
            // 实测：Npgsql.NpgsqlException 包着 SocketException(61) Connection refused。
            if (string.IsNullOrEmpty(database.SqlState))
            {
                return Describe(
                    "The tenant's dedicated database is unreachable. Check that the host and port "
                        + "are reachable and the database server is running.",
                    "Tenant:DedicatedDatabaseUnreachable");
            }

            return database.SqlState switch
            {
                // 3D000 invalid_catalog_name：连上了服务器，但那个库不存在。
                "3D000" => Describe(
                    "The database named in the connection string does not exist. Create it first, "
                        + "migrate it via ConnectionStrings:MigrationTarget, then create the tenant.",
                    "Tenant:DedicatedDatabaseMissing"),

                // 42P01 undefined_table：库在，但没迁移过，业务表还不存在。
                "42P01" => Describe(
                    "The database exists but has not been migrated; its business tables are missing. "
                        + "Run the DbMigrator against it with ConnectionStrings:MigrationTarget first.",
                    "Tenant:DedicatedDatabaseNotMigrated"),

                // 28P01 invalid_password。**本分支未经实测**：验证环境两个容器都是 trust 认证，
                // 要复现就得经手一个真实数据库口令，不值得为一条提示去碰凭据。
                "28P01" => Describe(
                    "The database rejected the credentials in the connection string.",
                    "Tenant:DedicatedDatabaseRejected"),

                _ => Describe(
                    $"The tenant's dedicated database reported error {database.SqlState}.",
                    "Tenant:DedicatedDatabaseFailed",
                    database.SqlState)
            };
        }

        return null;

        // 静态类型是 BusinessException 而不是 BadRequestException：WithCode / WithData 都返回
        // **基类** BusinessException，接不回派生类型。本仓既有写法是
        // `throw new BadRequestException(...).WithCode(...)`，直接跟在 throw 后面，
        // throw 接受任何 Exception，所以那里看不出这件事——换成赋值就会 CS0266。
        // 实例本身仍是 BadRequestException，运行时状态码照旧 400。
        //
        // **这几个分支不在「业务异常带错误码」闸门的扫描面内**，别误以为有闸门守着：
        // 那道闸门找的是 `throw new <业务异常>` 且同语句内有 `.WithCode(`，
        // 而这里是先赋值、再 `throw` 变量，两个形状都不匹配。
        // 改用这个形状是因为它换了一种更硬的保证：`code` 是 Describe 的**必填参数**，
        // 调用方在编译期就不可能不传码；五个分支共用唯一一处 WithCode，
        // 能把它丢掉的地方只有一个，且就在参数旁边。是编译器在担保，不是闸门。
        static BusinessException Describe(string message, string code, string? sqlState = null)
        {
            BusinessException failure = new BadRequestException(message);
#if (IncludeLocalization)
            failure = failure.WithCode(code);
            if (sqlState is not null)
            {
                failure = failure.WithData("SqlState", sqlState);
            }
#endif
            return failure;
        }
    }

    /// <summary>
    /// 回滚一次失败的租户创建：先清租内种子数据，再删连接登记，最后删租户注册表。
    /// </summary>
    /// <remarks>
    /// <para>三步分别使用干净的依赖注入作用域，避免复用失败 DbContext 的跟踪状态。
    /// 每步独立捕获并记录异常，且不覆盖触发补偿的原始异常；前一步失败仍继续后一步。
    /// 补偿使用独立取消令牌，不随调用方取消而中止。</para>
    /// <para><b>次序不可调换：删连接登记必须排在删租户之前。</b>
    /// <c>RemoveAsync</c> 对已删租户抛 <c>TenantNotFoundException</c>，
    /// 先删租户就再也删不掉那一行了。症状是控制库里逐次累积指向已删租户的孤儿连接行——
    /// 每失败一次多一条，不报错、界面上也看不见，只能靠查库发现。
    /// 创建路径本来是把"建注册表行"与"登记连接"放在同一个工作单元里的
    /// （见 CreateAsync 里那句"不会留下有租户没连接或反过来的半截状态"），
    /// 补偿这一侧必须守住同一条对称性。</para>
    /// </remarks>
    /// <param name="tenant">待回滚的租户</param>
    /// <param name="connectionVersion">
    /// 登记连接时读到的版本；<see langword="null"/> 表示这次创建没有登记连接（不分库），无需删除。
    /// </param>
    private async Task CompensateAsync(TenantConfiguration tenant, long? connectionVersion)
    {
        // 两个步骤各用干净作用域，避免前一步失败的跟踪状态污染后一步。
        try
        {
            using var purgeScope = serviceScopeFactory.CreateScope();
            var scopedCurrentTenant = purgeScope.ServiceProvider.GetRequiredService<ICurrentTenant>();
            var scopedSeeder = purgeScope.ServiceProvider.GetRequiredService<ITenantSeeder>();

            using (scopedCurrentTenant.Change(tenant.Id, tenant.Name))
            {
                await scopedSeeder.PurgeAsync(CancellationToken.None);
            }
        }
        catch (Exception purgeError)
        {
            logger.LogError(
                purgeError,
                "Failed to purge seed data for tenant {TenantId}; residual data requires manual inspection",
                tenant.Id);
        }

        // 删连接登记：必须在删租户之前，理由见方法注释。
        // 租户此刻仍是停用态（创建时 isActive: false，激活在播种成功之后），
        // 因此不会撞上"修改连接前必须先停用租户"那条前置条件。
        if (connectionVersion is { } version)
        {
            try
            {
                using var connectionScope = serviceScopeFactory.CreateScope();
                var scopedConnections = connectionScope.ServiceProvider
                    .GetRequiredService<ITenantConnectionConfigurationManager>();

                await scopedConnections.RemoveAsync(
                    tenant.Id,
                    ConnectionStringNames.Default,
                    version,
                    CancellationToken.None);
            }
            catch (Exception removeError)
            {
                logger.LogError(
                    removeError,
                    "Failed to remove the connection registration for tenant {TenantId}; "
                        + "an orphan row now points at a deleted tenant and needs manual cleanup",
                    tenant.Id);
            }
        }

        try
        {
            using var deleteScope = serviceScopeFactory.CreateScope();
            var scopedManager = deleteScope.ServiceProvider.GetRequiredService<ITenantManager>();

            await scopedManager.DeleteAsync(tenant.Id, CancellationToken.None);
        }
        catch (Exception deleteError)
        {
            logger.LogError(
                deleteError,
                "Failed to delete tenant {TenantId}; its name cannot be reused until manually cleaned up",
                tenant.Id);
        }
    }

    /// <inheritdoc />
    public async Task<TenantOutputDto> UpdateAsync(Guid id, UpdateTenantInputDto input, CancellationToken cancellationToken = default)
    {
        var record = await tenantManager.UpdateAsync(
            id, input.Name, input.DisplayName, input.Description, cancellationToken);

#if (LocalIdentity)
        await operationRecorder.RecordSucceededAsync(
            OperationRecordActions.TenantUpdated,
            OperationTarget.For(record.Id, record.DisplayName ?? record.Name),
            PermissionConstant.Tenants.Update,
            cancellationToken);
#endif

        return ToOutputDto(record);
    }

    /// <inheritdoc />
    /// <remarks>
    /// 启用前要求租户里至少有一个用户。这条规则本身就站得住——启用一个没有管理员的租户
    /// 毫无用途，只会成为匿名入口（注册、找回密码）的靶子；它同时挡住了控制面竞争：
    /// 另一个宿主管理员在创建流程的播种阶段抢先手动启用，会把一个还没有管理员的
    /// 半成品租户暴露出去。管理员写入之后再抢先启用则无害——租户功能上已经完整。
    /// </remarks>
    public async Task<TenantOutputDto> SetActivationAsync(
        Guid id,
        UpdateTenantActivationInputDto input,
        CancellationToken cancellationToken = default)
    {
        if (input.IsActive)
        {
            // 存在性必须先判：不存在的租户里"用户数为 0"同样成立，
            // 不先判就会把 404 讲成"这个租户还没有用户"（400）
            _ = await tenantManager.FindAsync(id, cancellationToken)
                ?? throw new NotFoundException($"Tenant '{id}' not found.");

            await EnsureTenantHasUsersAsync(id, cancellationToken);
        }

        var record = await tenantManager.SetActiveAsync(id, input.IsActive, cancellationToken);

#if (LocalIdentity)
        // 启停是 Critical：它改变的是"这一整批人还能不能进来"。
        await operationRecorder.RecordSucceededAsync(
            OperationRecordActions.TenantActivationChanged,
            OperationTarget.For(record.Id, record.DisplayName ?? record.Name),
            PermissionConstant.Tenants.Update,
            cancellationToken);
#endif

        return ToOutputDto(record);
    }

    /// <summary>
    /// 校验目标租户内已存在用户；空租户不允许被启用。调用前需已确认租户存在。
    /// </summary>
    private async Task EnsureTenantHasUsersAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        // **必须在租户上下文内新开工作单元**，不能只切 Change。本方法没有环境工作单元，仓储取到的是
        // 请求作用域里早已创建的 DbContext——授权阶段读权限授予时它就绑定到了宿主库。分库租户的用户在
        // 它自己的库里，于是计数恒为 0：已停用的分库租户再也启不回来，报出来的却是"该租户还没有任何用户"。
        // 新开的工作单元自带作用域，DbContext 按租户连接重新创建。框架现在会把这种改道直接拒掉，不再静默连错库。
        using (currentTenant.Change(tenantId))
        using (var tenantUnitOfWork = await unitOfWorkManager.BeginAsync(requiresNew: true))
        {
            var userCount = await userRepository.CountAsync(cancellationToken: cancellationToken);
            await tenantUnitOfWork.CompleteAsync(cancellationToken);

            if (userCount == 0)
            {
                throw new BadRequestException(
                    "This tenant has no users yet; activating it would let nobody in. Finish provisioning first.")
#if (IncludeLocalization)
                    .WithCode("Tenant:ActivateWithoutUsers")
#endif
                    ;
            }
        }
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
#if (LocalIdentity)
        // 名字必须在删除前取：删完再查什么都查不到，而审计要回答的正是"当时删掉的是哪一个"。
        // 取不到（并发删除）就退化为无名，不因为一个审计字段把删除本身变成 404。
        var doomed = await tenantManager.FindAsync(id, cancellationToken);
#endif

        await tenantManager.DeleteAsync(id, cancellationToken);
        logger.LogInformation("Tenant {TenantId} deleted (soft delete; business data retained)", id);
#if (LocalIdentity)

        await operationRecorder.RecordSucceededAsync(
            OperationRecordActions.TenantDeleted,
            OperationTarget.For(id, doomed is null ? null : doomed.DisplayName ?? doomed.Name),
            PermissionConstant.Tenants.Delete,
            cancellationToken);
#endif
    }

    /// <inheritdoc />
    public async Task<TenantLookupOutputDto?> FindByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var tenant = await tenantStore.FindByNameAsync(tenantNormalizer.NormalizeName(name)!, cancellationToken);
        if (tenant is null)
        {
            return null;
        }

        // 匿名探测只回选择租户所需的最小信息——该投影由 TenantLookupOutputDto 的字段集表达
        return objectMapper.Map<TenantConfiguration, TenantLookupOutputDto>(tenant);
    }

    private TenantOutputDto ToOutputDto(TenantConfiguration tenant)
        => objectMapper.Map<TenantConfiguration, TenantOutputDto>(tenant);
}
