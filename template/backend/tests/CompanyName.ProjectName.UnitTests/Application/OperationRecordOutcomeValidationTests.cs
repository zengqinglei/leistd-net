using System.ComponentModel.DataAnnotations;
using CompanyName.ProjectName.Application.OperationRecords.Dtos;

namespace CompanyName.ProjectName.UnitTests.Application;

/// <summary>
/// 结果筛选值只认成员名称，分页与导出两个入口一致。
/// </summary>
/// <remarks>
/// 数字与逗号组合若被当作合法值，请求会成功却查不到记录——看起来是"没有这类记录"，
/// 实际是筛选条件无效。按完整的 DataAnnotations 校验走一遍，与接口入口的行为一致。
/// </remarks>
public sealed class OperationRecordOutcomeValidationTests
{
    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("2")]
    [InlineData("Succeeded,Failed")]
    [InlineData("failed")]
    [InlineData("Unknown")]
    public void Both_entry_points_reject_anything_but_a_member_name(string value)
    {
        Assert.NotEmpty(OutcomeErrors(new GetOperationRecordPagedInputDto { Outcome = value }));
        Assert.NotEmpty(OutcomeErrors(new ExportOperationRecordsInputDto { Outcome = value }));
    }

    [Theory]
    [InlineData("Succeeded")]
    [InlineData("Failed")]
    [InlineData(null)]
    public void Both_entry_points_accept_a_member_name_or_no_filter(string? value)
    {
        Assert.Empty(OutcomeErrors(new GetOperationRecordPagedInputDto { Outcome = value }));
        Assert.Empty(OutcomeErrors(new ExportOperationRecordsInputDto { Outcome = value }));
    }

    private static List<ValidationResult> OutcomeErrors(object input)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(input, new ValidationContext(input), results, validateAllProperties: true);
        return [.. results.Where(result => result.MemberNames.Contains("Outcome"))];
    }
}
