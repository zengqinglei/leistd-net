using Leistd.Exception.Core;
using Xunit;

namespace Leistd.Exception.Tests;

public class BusinessExceptionCodeTests
{
    [Fact]
    public void Default_code_is_prefix_plus_00()
    {
        Assert.Equal(40000, new BadRequestException("x").Code);
        Assert.Equal(40400, new NotFoundException("x").Code);
        Assert.Equal(42200, new UnprocessableEntityException("f", "e").Code);
    }

    [Fact]
    public void WithCode_string_appends_suffix()
    {
        var ex = new BadRequestException("x").WithCode("46");
        Assert.Equal(40046, ex.Code);
    }

    [Fact]
    public void WithData_accumulates_localization_arguments()
    {
        var ex = new BadRequestException("Order:StockInsufficient")
            .WithData("Sku", "A1")
            .WithData("Qty", 3);

        Assert.Equal("A1", ex.LocalizationData["Sku"]);
        Assert.Equal(3, ex.LocalizationData["Qty"]);
    }

    [Fact]
    public void LocalizationKey_is_null_by_default()
    {
        // 三分离：未显式设置展示键时为 null，处理器回落 Message
        Assert.Null(new BadRequestException("diagnostic message").LocalizationKey);
    }

    [Fact]
    public void WithLocalization_sets_display_key_independently_of_message_and_code()
    {
        // Message（诊断）/ Code（机器契约）/ LocalizationKey（展示）三者互不耦合
        var ex = new BadRequestException("Email already in use")
            .WithCode("02")
            .WithLocalization("User:EmailAlreadyUsed")
            .WithData("Email", "a@b.com");

        Assert.Equal("Email already in use", ex.Message);
        Assert.Equal(40002, ex.Code);
        Assert.Equal("User:EmailAlreadyUsed", ex.LocalizationKey);
        Assert.Equal("a@b.com", ex.LocalizationData["Email"]);
    }
}
