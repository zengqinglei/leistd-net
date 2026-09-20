# 0.12.0 → 0.13.0 公共表面逐条对比

由两个版本的随包 XML 逐包比对得出，共 **212** 条真正删除或签名改变的成员。
生成方式见 [升级清单](upgrade-0.13.0.md) 开头；仅改了命名空间或包的搬迁不在此列，它们归纳在升级清单第 2 节。

> 0.12.0 当时 `NoWarn` 里含 CS1591，随包 XML 不完整，因此**只列删除**：
> 「新增」会混入那时就有、只是没写注释的成员。

## Leistd.UnitOfWork.Core（53）

- `E:Leistd.UnitOfWork.Core.Uow.ChildUnitOfWork.Failed`
- `E:Leistd.UnitOfWork.Core.Uow.UnitOfWork.Disposed`
- `E:Leistd.UnitOfWork.Core.Uow.UnitOfWork.Failed`
- `F:Leistd.UnitOfWork.Core.Events.UnitOfWorkPhase.AfterCompletion`
- `F:Leistd.UnitOfWork.Core.Events.UnitOfWorkPhase.AfterRollback`
- `F:Leistd.UnitOfWork.Core.Uow.UnitOfWork._eventsForLaterPhases`
- `F:Leistd.UnitOfWork.Core.Uow.UnitOfWork._pendingEvents`
- `M:Leistd.UnitOfWork.Core.Database.IDatabaseApiContainer.GetOrAddDatabaseApi(System.Func{Leistd.UnitOfWork.Core.Database.IDatabaseApi})`
- `M:Leistd.UnitOfWork.Core.Database.ITransactionApiContainer.AddTransactionApi(Leistd.UnitOfWork.Core.Database.ITransactionApi)`
- `M:Leistd.UnitOfWork.Core.Database.ITransactionApiContainer.FindTransactionApi`
- `M:Leistd.UnitOfWork.Core.DependencyInjection.AddUnitOfWork(Microsoft.Extensions.DependencyInjection.IServiceCollection,System.Action{Leistd.UnitOfWork.Core.Options.UnitOfWorkOptions})`
- `M:Leistd.UnitOfWork.Core.DependencyInjection.ShouldInterceptEventHandler(Leistd.DependencyInjection.IOnServiceRegisteredContext)`
- `M:Leistd.UnitOfWork.Core.DependencyInjection.ShouldInterceptUnitOfWork(System.Type)`
- `M:Leistd.UnitOfWork.Core.Events.UnitOfWorkEventArgs.#ctor(Leistd.UnitOfWork.Core.Uow.IUnitOfWork)`
- `M:Leistd.UnitOfWork.Core.Events.UnitOfWorkEventHandlerAttribute.#ctor(Leistd.UnitOfWork.Core.Events.UnitOfWorkPhase)`
- `M:Leistd.UnitOfWork.Core.Events.UnitOfWorkFailedEventArgs.#ctor(Leistd.UnitOfWork.Core.Uow.IUnitOfWork,System.Exception,System.Boolean)`
- `M:Leistd.UnitOfWork.Core.Interceptor.UnitOfWorkEventHandlerInterceptor.#ctor(Microsoft.Extensions.Logging.ILogger{Leistd.UnitOfWork.Core.Interceptor.UnitOfWorkEventHandlerInterceptor})`
- `M:Leistd.UnitOfWork.Core.Interceptor.UnitOfWorkInterceptor.GetUnitOfWorkAttribute(System.Reflection.MethodInfo)`
- `M:Leistd.UnitOfWork.Core.Uow.AmbientUnitOfWork.Set(Leistd.UnitOfWork.Core.Uow.IUnitOfWork)`
- `M:Leistd.UnitOfWork.Core.Uow.IAmbientUnitOfWork.Set(Leistd.UnitOfWork.Core.Uow.IUnitOfWork)`
- `M:Leistd.UnitOfWork.Core.Uow.IUnitOfWork.AddPendingEvents(System.Collections.Generic.IEnumerable{Leistd.EventBus.Core.Event.ILocalEvent})`
- `M:Leistd.UnitOfWork.Core.Uow.IUnitOfWork.Initialize(Leistd.UnitOfWork.Core.Options.UnitOfWorkOptions)`
- `M:Leistd.UnitOfWork.Core.Uow.IUnitOfWork.SetOuter(Leistd.UnitOfWork.Core.Uow.IUnitOfWork)`
- `M:Leistd.UnitOfWork.Core.Uow.IUnitOfWorkManager.BeginAsync(Leistd.UnitOfWork.Core.Options.UnitOfWorkOptions,System.Boolean)`
- `M:Leistd.UnitOfWork.Core.Uow.UnitOfWork.#ctor(System.IServiceProvider,Leistd.UnitOfWork.Core.Options.UnitOfWorkOptions)`
- `M:Leistd.UnitOfWork.Core.Uow.UnitOfWork.AddPendingEvents(System.Collections.Generic.IEnumerable{Leistd.EventBus.Core.Event.ILocalEvent})`
- `M:Leistd.UnitOfWork.Core.Uow.UnitOfWork.AddTransactionApi(Leistd.UnitOfWork.Core.Database.ITransactionApi)`
- `M:Leistd.UnitOfWork.Core.Uow.UnitOfWork.CommitTransactionsAsync`
- `M:Leistd.UnitOfWork.Core.Uow.UnitOfWork.CompleteAsync(System.Threading.CancellationToken)`
- `M:Leistd.UnitOfWork.Core.Uow.UnitOfWork.Dispose`
- `M:Leistd.UnitOfWork.Core.Uow.UnitOfWork.FindTransactionApi`
- `M:Leistd.UnitOfWork.Core.Uow.UnitOfWork.GetOrAddDatabaseApi(System.Func{Leistd.UnitOfWork.Core.Database.IDatabaseApi})`
- `M:Leistd.UnitOfWork.Core.Uow.UnitOfWork.Initialize(Leistd.UnitOfWork.Core.Options.UnitOfWorkOptions)`
- `M:Leistd.UnitOfWork.Core.Uow.UnitOfWork.OnCompletedAsync`
- `M:Leistd.UnitOfWork.Core.Uow.UnitOfWork.OnDisposed`
- `M:Leistd.UnitOfWork.Core.Uow.UnitOfWork.OnFailedAsync`
- `M:Leistd.UnitOfWork.Core.Uow.UnitOfWork.RollbackAllAsync(System.Threading.CancellationToken)`
- `M:Leistd.UnitOfWork.Core.Uow.UnitOfWork.RollbackAsync(System.Threading.CancellationToken)`
- `M:Leistd.UnitOfWork.Core.Uow.UnitOfWork.SetOuter(Leistd.UnitOfWork.Core.Uow.IUnitOfWork)`
- `M:Leistd.UnitOfWork.Core.Uow.UnitOfWork.ToString`
- `M:Leistd.UnitOfWork.Core.Uow.UnitOfWorkManager.#ctor(Leistd.UnitOfWork.Core.Options.UnitOfWorkOptions,System.IServiceProvider,Leistd.UnitOfWork.Core.Uow.IAmbientUnitOfWork,Microsoft.Extensions.Logging.ILogger{Leistd.UnitOfWork.Core.Uow.UnitOfWorkManager})`
- `M:Leistd.UnitOfWork.Core.Uow.UnitOfWorkManager.BeginAsync(Leistd.UnitOfWork.Core.Options.UnitOfWorkOptions,System.Boolean)`
- `M:Leistd.UnitOfWork.Core.Uow.UnitOfWorkManager.CreateNewUnitOfWork`
- `M:Leistd.UnitOfWork.Core.Uow.UnitOfWorkManager.GetCurrentUnitOfWork`
- `P:Leistd.UnitOfWork.Core.Events.UnitOfWorkFailedEventArgs.IsRolledback`
- `P:Leistd.UnitOfWork.Core.Uow.UnitOfWork.Id`
- `P:Leistd.UnitOfWork.Core.Uow.UnitOfWork.IsCompleted`
- `P:Leistd.UnitOfWork.Core.Uow.UnitOfWork.IsDisposed`
- `P:Leistd.UnitOfWork.Core.Uow.UnitOfWork.Options`
- `P:Leistd.UnitOfWork.Core.Uow.UnitOfWork.Outer`
- `P:Leistd.UnitOfWork.Core.Uow.UnitOfWork.ServiceProvider`
- `T:Leistd.UnitOfWork.Core.Uow.ChildUnitOfWork`
- `T:Leistd.UnitOfWork.Core.Uow.UnitOfWork`

## Leistd.Authorization.Core（21）

- `M:Leistd.Authorization.DefaultPermissionChecker.#ctor(Leistd.Authorization.IPermissionSubjectProvider,Leistd.Authorization.IPermissionGrantStore)`
- `M:Leistd.Authorization.DependencyInjection.AddPermissionAuthorizationCore(Microsoft.Extensions.DependencyInjection.IServiceCollection)`
- `M:Leistd.Authorization.IPermissionDefinition.AddChild(System.String,System.String)`
- `M:Leistd.Authorization.IPermissionDefinitionContext.AddPermission(System.String,System.String)`
- `M:Leistd.Authorization.IPermissionDefinitionProvider.Define(Leistd.Authorization.IPermissionDefinitionContext)`
- `M:Leistd.Authorization.IPermissionGrantManager.GetGrantedPermissionsForRoleAsync(System.String,System.Threading.CancellationToken)`
- `M:Leistd.Authorization.IPermissionGrantManager.GetGrantedPermissionsForUserAsync(System.String,System.Threading.CancellationToken)`
- `M:Leistd.Authorization.IPermissionGrantManager.GrantToRoleAsync(System.String,System.String,System.Threading.CancellationToken)`
- `M:Leistd.Authorization.IPermissionGrantManager.GrantToUserAsync(System.String,System.String,System.Threading.CancellationToken)`
- `M:Leistd.Authorization.IPermissionGrantManager.RevokeFromRoleAsync(System.String,System.String,System.Threading.CancellationToken)`
- `M:Leistd.Authorization.IPermissionGrantManager.RevokeFromUserAsync(System.String,System.String,System.Threading.CancellationToken)`
- `M:Leistd.Authorization.IPermissionGrantStore.GetGrantedPermissionsForRoleAsync(System.String,System.Threading.CancellationToken)`
- `M:Leistd.Authorization.IPermissionGrantStore.GetGrantedPermissionsForUserAsync(System.String,System.Threading.CancellationToken)`
- `M:Leistd.Authorization.IPermissionGrantStore.IsGrantedToAnyRoleAsync(System.String,System.Collections.Generic.IReadOnlyCollection{System.String},System.Threading.CancellationToken)`
- `M:Leistd.Authorization.IPermissionGrantStore.IsGrantedToRoleAsync(System.String,System.String,System.Threading.CancellationToken)`
- `M:Leistd.Authorization.IPermissionGrantStore.IsGrantedToUserAsync(System.String,System.String,System.Threading.CancellationToken)`
- `M:Leistd.Authorization.IPermissionGrantStore.IsGrantedToUserOrRolesAsync(System.Collections.Generic.IReadOnlyCollection{System.String},System.String,System.Collections.Generic.IReadOnlyCollection{System.String},System.Threading.CancellationToken)`
- `M:Leistd.Authorization.IPermissionGroupDefinition.AddPermission(System.String,System.String)`
- `T:Leistd.Authorization.PermissionDefinition`
- `T:Leistd.Authorization.PermissionDefinitionContext`
- `T:Leistd.Authorization.PermissionGroupDefinition`

## Leistd.RealTime.Core（17）

- `M:Leistd.RealTime.IPresenceService.GetOnlineUserIdsAsync(System.Threading.CancellationToken)`
- `M:Leistd.RealTime.IPresenceService.IsOnlineAsync(System.String,System.Threading.CancellationToken)`
- `M:Leistd.RealTime.IRealtimeSubscriptionAuthorizer.AuthorizeAsync(Leistd.RealTime.RealtimeSubscriptionContext,System.Threading.CancellationToken)`
- `M:Leistd.RealTime.RealtimeSubscriptionContext.#ctor(System.String,System.String)`
- `P:Leistd.RealTime.RealTimeOptions.ClientTimeoutInterval`
- `P:Leistd.RealTime.RealTimeOptions.EnableDetailedErrors`
- `P:Leistd.RealTime.RealTimeOptions.EnableRedisBackplane`
- `P:Leistd.RealTime.RealTimeOptions.KeepAliveInterval`
- `P:Leistd.RealTime.RealTimeOptions.RedisConnectionString`
- `P:Leistd.RealTime.RealTimeOptions.RequireSubscriptionAuthorization`
- `P:Leistd.RealTime.RealTimeOptions.UserIdClaimTypes`
- `P:Leistd.RealTime.RealtimeSubscriptionContext.ResourceKey`
- `P:Leistd.RealTime.RealtimeSubscriptionContext.UserId`
- `T:Leistd.RealTime.AllowAllRealtimeSubscriptionAuthorizer`
- `T:Leistd.RealTime.IPresenceService`
- `T:Leistd.RealTime.IRealtimeSubscriptionAuthorizer`
- `T:Leistd.RealTime.RealtimeSubscriptionContext`

## Leistd.Notifications.Core（14）

- `F:Leistd.Notifications.NotificationTypes.DataChange`
- `F:Leistd.Notifications.NotificationTypes.System`
- `F:Leistd.Notifications.NotificationTypes.Workflow`
- `M:Leistd.Notifications.INotificationPublisher.PublishToAllAsync(Leistd.Notifications.NotificationOutputDto,System.Threading.CancellationToken)`
- `M:Leistd.Notifications.INotificationPublisher.PublishToGroupAsync(System.String,Leistd.Notifications.NotificationOutputDto,System.Threading.CancellationToken)`
- `M:Leistd.Notifications.INotificationPublisher.PublishToUserAsync(System.String,Leistd.Notifications.NotificationOutputDto,System.Threading.CancellationToken)`
- `M:Leistd.Notifications.INotificationSender.SendToAllAsync(Leistd.Notifications.NotificationOutputDto,System.Threading.CancellationToken)`
- `M:Leistd.Notifications.INotificationSender.SendToGroupAsync(System.String,Leistd.Notifications.NotificationOutputDto,System.Threading.CancellationToken)`
- `M:Leistd.Notifications.INotificationSender.SendToUserAsync(System.String,Leistd.Notifications.NotificationOutputDto,System.Threading.CancellationToken)`
- `M:Leistd.Notifications.INotificationStore.GetByUserAsync(System.String,System.Int32,System.Threading.CancellationToken)`
- `M:Leistd.Notifications.INotificationStore.SaveAsync(Leistd.Notifications.NotificationOutputDto,System.String,System.Threading.CancellationToken)`
- `M:Leistd.Notifications.NotificationPublisher.#ctor(Leistd.Timing.IClock,System.Collections.Generic.IEnumerable{Leistd.Notifications.INotificationStore},System.Collections.Generic.IEnumerable{Leistd.Notifications.INotificationSender})`
- `T:Leistd.Notifications.INotificationSender`
- `T:Leistd.Notifications.NotificationTypes`

## Leistd.Ddd.Infrastructure（13）

- `F:Leistd.Ddd.Infrastructure.EventBus.LocalEventSaveChangesInterceptor._pendingByContext`
- `M:Leistd.Ddd.Infrastructure.DependencyInjection.AddDddInfrastructure(Microsoft.Extensions.DependencyInjection.IServiceCollection,System.Action{Leistd.UnitOfWork.Core.Options.UnitOfWorkOptions})`
- `M:Leistd.Ddd.Infrastructure.DependencyInjection.FindPrimaryKeyType(System.Type)`
- `M:Leistd.Ddd.Infrastructure.DependencyInjection.RegisterRepositoriesForDbContext(Microsoft.Extensions.DependencyInjection.IServiceCollection,System.Type)`
- `M:Leistd.Ddd.Infrastructure.EventBus.LocalEventSaveChangesInterceptor.ClearLocalEvents(Microsoft.EntityFrameworkCore.DbContext)`
- `M:Leistd.Ddd.Infrastructure.EventBus.LocalEventSaveChangesInterceptor.CollectInto(Microsoft.EntityFrameworkCore.DbContext)`
- `M:Leistd.Ddd.Infrastructure.EventBus.LocalEventSaveChangesInterceptor.CollectLocalEvents(Microsoft.EntityFrameworkCore.DbContext)`
- `M:Leistd.Ddd.Infrastructure.EventBus.LocalEventSaveChangesInterceptor.PublishCollected(Microsoft.EntityFrameworkCore.DbContext)`
- `M:Leistd.Ddd.Infrastructure.EventBus.LocalEventSaveChangesInterceptor.PublishCollectedAsync(Microsoft.EntityFrameworkCore.DbContext,System.Threading.CancellationToken)`
- `M:Leistd.Ddd.Infrastructure.Persistence.Extensions.ModelBuilderExtensions.ApplyGlobalFilters``1(Microsoft.EntityFrameworkCore.ModelBuilder,System.Linq.Expressions.Expression{System.Func{``0,System.Boolean}})`
- `M:Leistd.Ddd.Infrastructure.Persistence.Repositories.RepositoryExtensions.GetQueryIncludingAsync``2(Leistd.Ddd.Domain.Repositories.IRepository{``0,``1},System.Threading.CancellationToken,System.Linq.Expressions.Expression{System.Func{``0,System.Object}}[])`
- `P:Leistd.Ddd.Infrastructure.Persistence.BaseDbContext.IsSoftDeleteFilterEnabled`
- `T:Leistd.Ddd.Infrastructure.Persistence.Repositories.RepositoryExtensions`

## Leistd.RealTime.AspNetCore.SignalR（12）

- `M:Leistd.RealTime.AspNetCore.SignalR.ClaimsSignalRUserIdProvider.#ctor(Microsoft.Extensions.Options.IOptions{Leistd.RealTime.RealTimeOptions})`
- `M:Leistd.RealTime.AspNetCore.SignalR.DependencyInjection.AddRealTimeSignalR(Microsoft.Extensions.DependencyInjection.IServiceCollection,System.Action{Leistd.RealTime.RealTimeOptions})`
- `M:Leistd.RealTime.AspNetCore.SignalR.RealTimeHub.#ctor(Leistd.Security.Users.ICurrentUser,Leistd.RealTime.IRealtimeSubscriptionAuthorizer,Microsoft.Extensions.Options.IOptions{Leistd.RealTime.RealTimeOptions},Microsoft.Extensions.Logging.ILogger{Leistd.RealTime.AspNetCore.SignalR.RealTimeHub})`
- `M:Leistd.RealTime.AspNetCore.SignalR.RealTimeHub.OnConnectedAsync`
- `M:Leistd.RealTime.AspNetCore.SignalR.RealTimeHub.OnDisconnectedAsync(System.Exception)`
- `M:Leistd.RealTime.AspNetCore.SignalR.SignalRBusinessEventPublisher.#ctor(Microsoft.AspNetCore.SignalR.IHubContext{Leistd.RealTime.AspNetCore.SignalR.RealTimeHub},Microsoft.Extensions.Logging.ILogger{Leistd.RealTime.AspNetCore.SignalR.SignalRBusinessEventPublisher})`
- `M:Leistd.RealTime.AspNetCore.SignalR.SignalRPresenceService.GetOnlineUserIdsAsync(System.Threading.CancellationToken)`
- `M:Leistd.RealTime.AspNetCore.SignalR.SignalRPresenceService.IsOnlineAsync(System.String,System.Threading.CancellationToken)`
- `M:Leistd.RealTime.AspNetCore.SignalR.SignalRPresenceService.UserConnected(System.String)`
- `M:Leistd.RealTime.AspNetCore.SignalR.SignalRPresenceService.UserDisconnected(System.String)`
- `T:Leistd.RealTime.AspNetCore.SignalR.SignalRBusinessEventPublisher`
- `T:Leistd.RealTime.AspNetCore.SignalR.SignalRPresenceService`

## Leistd.ObjectMapping.AutoMapper（10）

- `M:Leistd.ObjectMapping.AutoMapper.AutoMapperObjectMapper.#ctor(AutoMapper.IMapper)`
- `M:Leistd.ObjectMapping.AutoMapper.AutoMapperObjectMapper.GetMapper`
- `M:Leistd.ObjectMapping.AutoMapper.DependencyInjection.AddAutoMapperObjectMapper(Microsoft.Extensions.DependencyInjection.IServiceCollection,System.Action{Leistd.ObjectMapping.AutoMapper.AutoMapperOptions})`
- `M:Leistd.ObjectMapping.AutoMapper.Extensions.AutoMapperExtensions.GetAutoMapper(Leistd.ObjectMapping.Core.IObjectMapper)`
- `M:Leistd.ObjectMapping.AutoMapper.Extensions.AutoMapperExtensions.ProjectTo``2(System.Linq.IQueryable{``0},Leistd.ObjectMapping.Core.IObjectMapper)`
- `P:Leistd.ObjectMapping.AutoMapper.AutoMapperOptions.Configurators`
- `P:Leistd.ObjectMapping.AutoMapper.AutoMapperOptions.ValidateMappings`
- `T:Leistd.ObjectMapping.AutoMapper.AutoMapperObjectMapper`
- `T:Leistd.ObjectMapping.AutoMapper.AutoMapperOptions`
- `T:Leistd.ObjectMapping.AutoMapper.Extensions.AutoMapperExtensions`

## Leistd.DependencyInjection（5）

- `M:Leistd.DependencyInjection.OnServiceRegisteredContext.#ctor(System.Type,System.Type)`
- `M:Leistd.DependencyInjection.ServiceCollectionRegistrationExtensions.GetOrCreateRegistrationActionList(Microsoft.Extensions.DependencyInjection.IServiceCollection)`
- `M:Leistd.DependencyInjection.ServiceCollectionRegistrationExtensions.OnServiceRegistered(Microsoft.Extensions.DependencyInjection.IServiceCollection,System.Action{Leistd.DependencyInjection.IOnServiceRegisteredContext})`
- `M:Leistd.DependencyInjection.ServiceRegistrationCallbackFactory.OnRegistrationProcessed(Microsoft.Extensions.DependencyInjection.IServiceCollection,Microsoft.Extensions.DependencyInjection.ServiceDescriptor,Leistd.DependencyInjection.IOnServiceRegisteredContext)`
- `M:Leistd.DependencyInjection.ServiceRegistrationCallbackFactory.ProcessServiceRegistrations(Microsoft.Extensions.DependencyInjection.IServiceCollection,Leistd.DependencyInjection.ServiceRegistrationActionList)`

## Leistd.Notifications.AspNetCore.SignalR（5）

- `M:Leistd.Notifications.AspNetCore.SignalR.DependencyInjection.AddNotificationSignalRTransport(Microsoft.Extensions.DependencyInjection.IServiceCollection,System.Action{Leistd.RealTime.RealTimeOptions})`
- `M:Leistd.Notifications.AspNetCore.SignalR.DependencyInjection.AddNotificationsSignalR(Microsoft.Extensions.DependencyInjection.IServiceCollection,System.Action{Leistd.RealTime.RealTimeOptions})`
- `M:Leistd.Notifications.AspNetCore.SignalR.NotificationHub.#ctor(Leistd.Security.Users.ICurrentUser,Microsoft.Extensions.Logging.ILogger{Leistd.Notifications.AspNetCore.SignalR.NotificationHub})`
- `M:Leistd.Notifications.AspNetCore.SignalR.SignalRNotificationSender.#ctor(Microsoft.AspNetCore.SignalR.IHubContext{Leistd.Notifications.AspNetCore.SignalR.NotificationHub},Microsoft.Extensions.Logging.ILogger{Leistd.Notifications.AspNetCore.SignalR.SignalRNotificationSender})`
- `T:Leistd.Notifications.AspNetCore.SignalR.SignalRNotificationSender`

## Leistd.Authorization.EntityFrameworkCore（4）

- `M:Leistd.Authorization.EntityFrameworkCore.EfCorePermissionGrantManager`1.#ctor(`0,Leistd.Authorization.IPermissionGrantStore)`
- `M:Leistd.Authorization.EntityFrameworkCore.EfCorePermissionGrantStore`1.#ctor(`0)`
- `M:Leistd.Authorization.EntityFrameworkCore.PermissionGrantRecord.ForRole(System.String,System.Guid)`
- `M:Leistd.Authorization.EntityFrameworkCore.PermissionGrantRecord.ForUser(System.String,System.Guid)`

## Leistd.Core（4）

- `M:Leistd.Timing.ClockExtensions.GetLocalMidnightInUtc(Leistd.Timing.IClock)`
- `M:Leistd.Timing.ClockExtensions.GetLocalUtcOffsetHours(Leistd.Timing.IClock)`
- `P:Leistd.Timing.IClock.Kind`
- `P:Leistd.Timing.UtcClockProvider.Kind`

## Leistd.Ddd.Domain（4）

- `M:Leistd.Ddd.Domain.DataFilters.DataFilter`1.EnsureInitialized`
- `M:Leistd.Ddd.Domain.DataFilters.DataFilter`1.SetIsEnabled(System.Boolean)`
- `M:Leistd.Ddd.Domain.Entities.Entity.AddLocalEvent(Leistd.EventBus.Core.Event.ILocalEvent)`
- `T:Leistd.Ddd.Domain.DataFilters.DataFilter`1.FilterState`

## Leistd.Exception.Core（4）

- `M:Leistd.Exception.Core.UnprocessableEntityException.#ctor(System.Collections.Generic.Dictionary{System.String,System.String[]},System.String,System.Exception)`
- `M:Leistd.Exception.Core.UnprocessableEntityException.AddError(System.String,System.String)`
- `M:Leistd.Exception.Core.UnprocessableEntityException.AddErrors(System.String,System.String[])`
- `M:Leistd.Exception.Core.UnprocessableEntityException.WithErrors(System.Collections.Generic.Dictionary{System.String,System.String[]})`

## Leistd.Lock.Redis（4）

- `M:Leistd.Lock.Redis.DependencyInjection.AddRedisDistributedLock(Microsoft.Extensions.DependencyInjection.IServiceCollection,System.String)`
- `M:Leistd.Lock.Redis.RedisDistributedLock.#ctor(StackExchange.Redis.IConnectionMultiplexer,Microsoft.Extensions.Logging.ILogger{Leistd.Lock.Redis.RedisDistributedLock})`
- `M:Leistd.Lock.Redis.RedisLockHandle.#ctor(System.String,System.String,Leistd.Lock.Redis.RedisDistributedLock)`
- `T:Leistd.Lock.Redis.RedisLockHandle`

## Leistd.Notifications.EntityFrameworkCore（4）

- `M:Leistd.Notifications.EntityFrameworkCore.EfCoreNotificationStore`1.#ctor(`0,Leistd.Timing.IClock)`
- `M:Leistd.Notifications.EntityFrameworkCore.EfCoreNotificationStore`1.GetByUserAsync(System.String,System.Int32,System.Threading.CancellationToken)`
- `M:Leistd.Notifications.EntityFrameworkCore.EfCoreNotificationStore`1.SaveAsync(Leistd.Notifications.NotificationOutputDto,System.String,System.Threading.CancellationToken)`
- `M:Leistd.Notifications.EntityFrameworkCore.NotificationRecord.FromDto(Leistd.Notifications.NotificationOutputDto,System.String)`

## Leistd.Security.Core（4）

- `P:Leistd.Security.Clients.CurrentClient.ApiKeyId`
- `P:Leistd.Security.Clients.CurrentClient.CreatorId`
- `P:Leistd.Security.Clients.ICurrentClient.ApiKeyId`
- `P:Leistd.Security.Clients.ICurrentClient.CreatorId`

## Leistd.Tracing.Core（4）

- `M:Leistd.Tracing.Core.Options.CorrelationIdOptions.GetHttpHeaderNames`
- `P:Leistd.Tracing.Core.Options.CorrelationIdOptions.Enable`
- `P:Leistd.Tracing.Core.Options.CorrelationIdOptions.HttpHeaderName`
- `P:Leistd.Tracing.Core.Options.CorrelationIdOptions.SetResponseHeader`

## Leistd.Ddd.Application.Contracts（3）

- `M:Leistd.Ddd.Application.Contracts.Dtos.PagedResultDto`1.#ctor(System.Int64,System.Collections.Generic.IReadOnlyList{`0})`
- `T:Leistd.Ddd.Application.Contracts.Dtos.PagedRequestDto`
- `T:Leistd.Ddd.Application.Contracts.Dtos.PagedResultDto`1`

## Leistd.EventBus.Local（3）

- `M:Leistd.EventBus.Local.EventBus.LocalEventBus.#ctor(Microsoft.Extensions.DependencyInjection.IServiceScopeFactory)`
- `M:Leistd.EventBus.Local.EventBus.LocalEventBus.PublishAsync(Leistd.EventBus.Core.Event.IEvent,System.Threading.CancellationToken)`
- `T:Leistd.EventBus.Local.Wrapper.EventHandlerWrapper`

## Leistd.Response.AspNetCore（3）

- `M:Leistd.Response.AspNetCore.DependencyInjection.AddResponseWrapper(Microsoft.Extensions.DependencyInjection.IServiceCollection)`
- `M:Leistd.Response.AspNetCore.Extensions.ControllerExtensions.FailResult(Microsoft.AspNetCore.Mvc.ControllerBase,System.Int32,System.String)`
- `M:Leistd.Response.AspNetCore.Extensions.ControllerExtensions.FailResultWithErrors(Microsoft.AspNetCore.Mvc.ControllerBase,System.Int32,System.String,System.Collections.Generic.List{System.Collections.Generic.Dictionary{System.String,System.String}})`

## Leistd.UnitOfWork.EfCore（3）

- `M:Leistd.UnitOfWork.EfCore.Database.DbContextProvider`1.#ctor(Leistd.UnitOfWork.Core.Uow.IUnitOfWorkManager,System.IServiceProvider)`
- `M:Leistd.UnitOfWork.EfCore.Database.DbContextProvider`1.GetServiceProvider(Leistd.UnitOfWork.Core.Uow.IUnitOfWork)`
- `T:Leistd.UnitOfWork.EfCore.Database.DbContextExtensions`

## Leistd.Authorization.AspNetCore（2）

- `M:Leistd.Authorization.AspNetCore.PermissionAuthorizationHandler.#ctor(Leistd.Authorization.IPermissionChecker)`
- `P:Leistd.Authorization.AspNetCore.PermissionRequirement.PermissionName`

## Leistd.Ddd.Application（2）

- `M:Leistd.Ddd.Application.Extensions.ObjectMapperExtensions.MapPagedResult``2(Leistd.ObjectMapping.Core.IObjectMapper,Leistd.Ddd.Application.Contracts.Dtos.PagedResultDto{``0})`
- `T:Leistd.Ddd.Application.Services.IApplicationService`

## Leistd.DependencyInjection.DynamicProxy（2）

- `M:Leistd.DependencyInjection.DynamicProxy.DynamicProxyRegistrationExtensions.AddInterceptor(Leistd.DependencyInjection.IOnServiceRegisteredContext,System.Type)`
- `M:Leistd.DependencyInjection.DynamicProxy.DynamicProxyRegistrationExtensions.GetInterceptorTypes(Leistd.DependencyInjection.IOnServiceRegisteredContext)`

## Leistd.Exception.AspNetCore（2）

- `P:Leistd.Exception.AspNetCore.Options.GlobalExceptionOptions.Enable`
- `P:Leistd.Exception.AspNetCore.Options.GlobalExceptionOptions.IsShowDetails`

## Leistd.Lock.Memory（2）

- `M:Leistd.Lock.Memory.MemoryLockHandle.#ctor(System.String,Leistd.Lock.Memory.MemoryLocalLock)`
- `T:Leistd.Lock.Memory.MemoryLockHandle`

## Leistd.ObjectMapping.Mapster（2）

- `M:Leistd.ObjectMapping.Mapster.DependencyInjection.AddMapsterObjectMapper(Microsoft.Extensions.DependencyInjection.IServiceCollection,System.Action{Leistd.ObjectMapping.Mapster.MapsterOptions})`
- `M:Leistd.ObjectMapping.Mapster.DependencyInjection.AddProfiles(Leistd.ObjectMapping.Mapster.MapsterOptions,System.Reflection.Assembly[])`

## Leistd.Auditing.Core（1）

- `M:Leistd.Auditing.DependencyInjection.AddAuditingCore(Microsoft.Extensions.DependencyInjection.IServiceCollection)`

## Leistd.Auditing.EntityFrameworkCore（1）

- `M:Leistd.Auditing.EntityFrameworkCore.AuditSaveChangesInterceptor.UpdateAuditFields(Microsoft.EntityFrameworkCore.DbContext)`

## Leistd.EventBus.Core（1）

- `M:Leistd.EventBus.Core.EventBus.IEventBus.PublishAsync(Leistd.EventBus.Core.Event.IEvent,System.Threading.CancellationToken)`

## Leistd.Lock.Core（1）

- `M:Leistd.Lock.Core.ILock.UnlockAsync(System.String,System.Threading.CancellationToken)`

## Leistd.ObjectMapping.Core（1）

- `M:Leistd.ObjectMapping.Core.Extensions.ObjectMapperExtensions.MapList``2(Leistd.ObjectMapping.Core.IObjectMapper,System.Collections.Generic.IEnumerable{``0})`

## Leistd.Security.AspNetCore（1）

- `M:Leistd.Security.AspNetCore.DependencyInjection.UseSecurity(Microsoft.AspNetCore.Builder.IApplicationBuilder)`
