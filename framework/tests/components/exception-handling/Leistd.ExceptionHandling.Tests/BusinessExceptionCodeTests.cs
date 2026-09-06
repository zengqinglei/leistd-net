using Xunit;
using Leistd.ExceptionHandling.Constants;

namespace Leistd.ExceptionHandling.Tests;

public class BusinessExceptionCodeTests
{
    [Fact]
    public void Default_code_is_the_generic_code_for_the_status()
    {
        // 不显式设码时 Code 恒有值，且等于状态码通用码——对外响应里不会缺这一项
        Assert.Equal("Error:BadRequest", new BadRequestException("x").Code);
        Assert.Equal("Error:NotFound", new NotFoundException("x").Code);
        Assert.Equal("Error:UnprocessableEntity", new UnprocessableEntityException("f", "e").Code);
        Assert.Equal("Error:UnsupportedMediaType", new UnsupportedMediaTypeException("x").Code);
    }

    [Fact]
    public void Status_code_is_declared_by_the_subclass()
    {
        // 状态码由子类声明，不再从错误码里解析出来
        Assert.Equal(400, new BadRequestException("x").StatusCode);
        Assert.Equal(404, new NotFoundException("x").StatusCode);
        Assert.Equal(415, new UnsupportedMediaTypeException("x").StatusCode);
        Assert.Equal(503, new ServiceUnavailableException("x").StatusCode);
    }

    [Fact]
    public void Every_subclass_status_has_a_generic_code()
    {
        // 通用码映射必须覆盖全部子类，否则默认码会落到 Error:InternalServer 而与状态码不符
        BusinessException[] all =
        [
            new BadRequestException("x"),
            new UnauthorizedException("x"),
            new ForbiddenException("x"),
            new NotFoundException("x"),
            new ConflictException("x"),
            new UnsupportedMediaTypeException("x"),
            new UnprocessableEntityException("f", "e"),
            new InternalServerException("x"),
            new ServiceUnavailableException("x")
        ];

        foreach (var ex in all)
        {
            Assert.Equal(GenericErrorCodes.ForStatus(ex.StatusCode), ex.Code);
        }
    }

    [Fact]
    public void WithCode_replaces_the_default()
    {
        var ex = new BadRequestException("x").WithCode("User:EmailAlreadyUsed");
        Assert.Equal("User:EmailAlreadyUsed", ex.Code);
        Assert.Equal(400, ex.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void WithCode_rejects_blank_codes(string blank)
    {
        // 空码会作为对外契约泄出，且让词条查找落到无意义的键上
        Assert.Throws<ArgumentException>(() => new BadRequestException("x").WithCode(blank));
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
    public void Code_is_independent_of_the_diagnostic_message()
    {
        // Message（英文诊断，进日志）不参与身份；Code 才是身份，也是展示词条键
        var ex = new BadRequestException("Email already in use")
            .WithCode("User:EmailAlreadyUsed")
            .WithData("Email", "a@b.com");

        Assert.Equal("Email already in use", ex.Message);
        Assert.Equal("User:EmailAlreadyUsed", ex.Code);
        Assert.Equal("a@b.com", ex.LocalizationData["Email"]);
    }
}
