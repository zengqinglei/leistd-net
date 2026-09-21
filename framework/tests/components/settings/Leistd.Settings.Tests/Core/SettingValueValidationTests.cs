using Leistd.EventBus.Abstractions;
using Leistd.EventBus.Events;
using Leistd.ExceptionHandling;
using Leistd.Settings.Definitions;
using Leistd.Settings.Errors;
using Leistd.Settings.Management;
using Leistd.Settings.Resolution;
using Leistd.Settings.Stores;
using Leistd.Settings.Events;
using Leistd.Settings.Validation;
using Leistd.TestBase.Doubles;
using Xunit;

namespace Leistd.Settings.Tests.Core;

/// <summary>
/// 写入端按定义上的值元数据与业务校验器把关，写入成功后发布变更事件。
/// </summary>
/// <remarks>值域只靠界面约束不住脚本、旧版客户端与迁移数据；非法值一旦落库，之后每个消费方都得自己防御。</remarks>
public class SettingValueValidationTests
{
    [Theory]
    [InlineData("Security.RequireTwoFactor", "yes", SettingErrorCodes.BooleanRequired)]
    [InlineData("Security.RequireTwoFactor", "True", SettingErrorCodes.BooleanRequired)]
    [InlineData("Security.LockoutDurationMinutes", "abc", SettingErrorCodes.IntegerRequired)]
    [InlineData("Security.LockoutDurationMinutes", "0", SettingErrorCodes.ValueOutOfRange)]
    [InlineData("Security.LockoutDurationMinutes", "1441", SettingErrorCodes.ValueOutOfRange)]
    [InlineData("Logging.MinimumLevel", "debug", SettingErrorCodes.ValueNotAllowed)]
    [InlineData("Display.TimeZone", "", SettingErrorCodes.EmptyValueRejected)]
    public async Task A_value_outside_the_declared_domain_is_rejected_with_a_code(string name, string value, string code)
    {
        var (manager, store, _) = Build();

        var error = await Assert.ThrowsAsync<BadRequestException>(() => manager.SetAsync(name, value, Scope(name)));

        Assert.Equal(code, error.Code);
        Assert.Empty(store.Writes);
    }

    [Theory]
    [InlineData("Security.RequireTwoFactor", "true")]
    [InlineData("Security.LockoutDurationMinutes", "1")]
    [InlineData("Security.LockoutDurationMinutes", "1440")]
    [InlineData("Logging.MinimumLevel", "Debug")]
    public async Task A_value_inside_the_declared_domain_is_written(string name, string value)
    {
        var (manager, store, _) = Build();

        await manager.SetAsync(name, value, Scope(name));

        Assert.Equal(value, Assert.Single(store.Writes).Value);
    }

    [Fact]
    public async Task Business_validators_run_after_the_metadata_checks()
    {
        var validator = new RejectingValidator("Display.TimeZone", "Mars/Olympus");
        var (manager, store, _) = Build(validator);

        var error = await Assert.ThrowsAsync<BadRequestException>(
            () => manager.SetAsync("Display.TimeZone", "Mars/Olympus", SettingScopes.User, "u1"));

        Assert.Equal("Setting:TimeZoneInvalid", error.Code);
        Assert.Equal(SettingScopes.User, validator.Seen!.Scope);
        Assert.Equal("u1", validator.Seen.UserId);
        Assert.Empty(store.Writes);
    }

    // 清除只校验名称与层级：否则"恢复默认"会被一个已经不合法的历史值卡住
    [Fact]
    public async Task Clearing_skips_value_validation()
    {
        var validator = new RejectingValidator("Display.TimeZone", "anything");
        var (manager, store, _) = Build(validator);

        await manager.SetAsync("Display.TimeZone", null, SettingScopes.User, "u1");

        Assert.Null(validator.Seen);
        Assert.Null(Assert.Single(store.Writes).Value);
    }

    [Fact]
    public async Task A_successful_write_publishes_a_change_event_without_the_value()
    {
        var (manager, _, events) = Build();

        await manager.SetAsync("Display.TimeZone", "Asia/Tokyo", SettingScopes.User, "u1");
        await manager.SetAsync("Logging.MinimumLevel", "Debug", SettingScopes.Host);

        Assert.Collection(
            events.Published.Cast<SettingChangedEvent>(),
            e => Assert.Equal(("Display.TimeZone", SettingScopes.User, "u1"), (e.Name, e.Scope, e.UserId)),
            e => Assert.Equal(("Logging.MinimumLevel", SettingScopes.Host, (string?)null), (e.Name, e.Scope, e.UserId)));
    }

    [Fact]
    public async Task A_rejected_write_publishes_nothing()
    {
        var (manager, _, events) = Build();

        await Assert.ThrowsAsync<BadRequestException>(() => manager.SetAsync("Security.RequireTwoFactor", "yes", SettingScopes.Tenant));

        Assert.Empty(events.Published);
    }

    private static SettingScopes Scope(string name) => name switch
    {
        "Logging.MinimumLevel" => SettingScopes.Host,
        _ => SettingScopes.Tenant,
    };

    private static (ISettingManager Manager, FakeSettingStore Store, RecordingEventBus Events) Build(
        params ISettingValueValidator[] validators)
    {
        var store = new FakeSettingStore();
        var events = new RecordingEventBus();
        var definitions = new SettingDefinitionManager([new Definitions()]);
        var manager = new DefaultSettingManager(
            definitions,
            store,
            new DefaultSettingProvider(definitions, store, new FakeCurrentUser(Guid.NewGuid())),
            validators,
            events);
        return (manager, store, events);
    }

    private sealed class Definitions : ISettingDefinitionProvider
    {
        public void Define(ISettingDefinitionContext context)
        {
            context.Add("Security.RequireTwoFactor", "false").AsBoolean();
            context.Add("Security.LockoutDurationMinutes", "15").AsInteger(1, 1440);
            context.Add("Logging.MinimumLevel", "Information", SettingScopes.Host).WithAllowedValues("Debug", "Information");
            context.Add("Display.TimeZone", scopes: SettingScopes.All);
        }
    }

    private sealed class RejectingValidator(string name, string value) : ISettingValueValidator
    {
        public SettingValueValidationContext? Seen { get; private set; }

        public Task ValidateAsync(SettingValueValidationContext context, CancellationToken cancellationToken = default)
        {
            Seen = context;
            if (context.Definition.Name == name && context.Value == value)
            {
                throw new BadRequestException($"'{value}' is not valid.").WithCode("Setting:TimeZoneInvalid");
            }

            return Task.CompletedTask;
        }
    }

    internal sealed class RecordingEventBus : ILocalEventBus
    {
        public List<IEvent> Published { get; } = [];

        public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
            where TEvent : IEvent
        {
            Published.Add(@event);
            return Task.CompletedTask;
        }

        public Task PublishAsync(IEvent @event, CancellationToken cancellationToken = default)
        {
            Published.Add(@event);
            return Task.CompletedTask;
        }
    }
}
