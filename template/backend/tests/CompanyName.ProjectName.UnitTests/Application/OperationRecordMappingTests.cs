using CompanyName.ProjectName.Application;
using CompanyName.ProjectName.Application.OperationRecords.Dtos;
using CompanyName.ProjectName.Application.OperationRecords.Mappings;
#if (LocalIdentity)
using CompanyName.ProjectName.Application.OperationRecords.Provider;
#endif
using Leistd.ObjectMapping.Abstractions;
using Leistd.OperationRecords.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace CompanyName.ProjectName.UnitTests.Application;

/// <summary>
/// 操作记录的输出映射：字段级裁剪与"操作人即目标"的判定。
/// </summary>
/// <remarks>
/// 技术详情与链路标识只给宿主读者。裁剪必须在服务端的映射里完成——交给界面"不显示"
/// 等于数据已经下发；而这两个字段在列表与导出里都走这一个映射，漏裁一处就两处都漏。
/// </remarks>
public sealed class OperationRecordMappingTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly IObjectMapper _mapper;

    public OperationRecordMappingTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplicationServices();
        _provider = services.BuildServiceProvider();
        _mapper = _provider.GetRequiredService<IObjectMapper>();
    }

    public void Dispose() => _provider.Dispose();

    [Fact]
    public void Host_only_fields_are_blank_for_tenant_readers()
    {
        var output = Map(FailedRecord(), includeHostOnlyFields: false);

        Assert.Null(output.FailureDetail);
        Assert.Null(output.CorrelationId);
        Assert.Equal("Failed", output.Outcome);
        Assert.Equal("Setting:Invalid", output.FailureCode);
    }

    [Fact]
    public void Host_only_fields_are_kept_for_host_readers()
    {
        var output = Map(FailedRecord(), includeHostOnlyFields: true);

        Assert.Equal("stack trace", output.FailureDetail);
        Assert.Equal("corr-1", output.CorrelationId);
    }

    [Fact]
    public void Missing_reader_flag_is_treated_as_a_tenant_reader()
    {
        var output = _mapper.Map<OperationRecordInfo, OperationRecordOutputDto>(FailedRecord());

        Assert.Null(output.FailureDetail);
        Assert.Null(output.CorrelationId);
    }

#if (LocalIdentity)
    [Fact]
    public void Self_acting_success_without_actor_marks_the_actor_as_the_target()
    {
        var output = Map(
            new OperationRecordInfo
            {
                Action = OperationRecordActions.AuthLoginSucceeded,
                TargetId = "user-1",
                AuthorizationBasis = "anonymous",
                Outcome = OperationRecordOutcome.Succeeded,
            },
            includeHostOnlyFields: false);

        Assert.True(output.ActorIsTarget);
    }

#endif
    [Fact]
    public void Other_actions_do_not_mark_the_actor_as_the_target()
    {
        var output = Map(FailedRecord(), includeHostOnlyFields: true);

        Assert.False(output.ActorIsTarget);
    }

    private OperationRecordOutputDto Map(OperationRecordInfo record, bool includeHostOnlyFields) =>
        _mapper.Map<OperationRecordInfo, OperationRecordOutputDto>(
            record,
            new Dictionary<string, object> { [OperationRecordProfile.IncludeHostOnlyFieldsKey] = includeHostOnlyFields });

    private static OperationRecordInfo FailedRecord() => new()
    {
        Action = "setting.changed",
        TargetId = "Host/Logging.MinimumLevel",
        AuthorizationBasis = "Settings",
        Outcome = OperationRecordOutcome.Failed,
        FailureCode = "Setting:Invalid",
        FailureDetail = "stack trace",
        CorrelationId = "corr-1",
    };
}
