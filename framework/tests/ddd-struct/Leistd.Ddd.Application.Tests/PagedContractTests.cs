using System.ComponentModel.DataAnnotations;
using Leistd.Ddd.Application.Contracts.Dtos;
using Xunit;

namespace Leistd.Ddd.Application.Tests;

/// <summary>
/// 分页入参与出参的对外契约：每个业务项目的列表接口都直接用它。
/// </summary>
public class PagedContractTests
{
    private static IReadOnlyList<ValidationResult> Validate(PagedRequestDto request)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true);
        return results;
    }

    [Fact]
    public void Default_request_is_valid_and_returns_a_bounded_page()
    {
        var request = new PagedRequestDto();

        Assert.Equal(0, request.Offset);
        Assert.Equal(10, request.Limit);
        Assert.Null(request.Sorting);
        Assert.Empty(Validate(request));
    }

    // 边界值必须放行：写成 Range(1, 999) 之类的差一错误只有边界用例能发现。
    [Theory]
    [InlineData(0, 1)]
    [InlineData(0, PagedRequestDto.MaximumLimit)]
    [InlineData(int.MaxValue, 10)]
    public void Boundary_values_are_accepted(int offset, int limit)
    {
        Assert.Empty(Validate(new PagedRequestDto { Offset = offset, Limit = limit }));
    }

    // 越界产生验证错误而不是被静默夹紧：夹紧会让调用方以为拿到了它请求的那一页。
    [Theory]
    [InlineData(-1, 10)]
    [InlineData(0, 0)]
    [InlineData(0, -1)]
    [InlineData(0, PagedRequestDto.MaximumLimit + 1)]
    public void Out_of_range_values_produce_validation_errors(int offset, int limit)
    {
        Assert.NotEmpty(Validate(new PagedRequestDto { Offset = offset, Limit = limit }));
    }

    // Sorting 不在此约束——可排序字段由各应用服务自行校验，这里不得抢先拒绝。
    [Theory]
    [InlineData("")]
    [InlineData("name desc")]
    [InlineData("'; drop table users --")]
    public void Sorting_is_not_validated_here(string sorting)
    {
        Assert.Empty(Validate(new PagedRequestDto { Sorting = sorting }));
    }

    [Fact]
    public void Paged_result_keeps_total_count_independent_of_page_size()
    {
        var result = new PagedResultDto<string>(1000, ["a", "b"]);

        Assert.Equal(1000, result.TotalCount);
        Assert.Equal(2, result.Items.Count);
    }

    // 空条目必须是空集合而不是 null：调用方普遍直接 foreach / .Count。
    [Fact]
    public void Null_items_become_an_empty_collection()
    {
        var result = new PagedResultDto<string>(0, (IEnumerable<string>)null!);

        Assert.NotNull(result.Items);
        Assert.Empty(result.Items);
    }

    // 从序列构造时必须当场物化：延迟求值的序列会在响应序列化阶段才执行，
    // 那时数据库上下文往往已经释放。
    [Fact]
    public void Sequence_constructor_materializes_immediately()
    {
        var evaluated = 0;
        IEnumerable<int> Lazy()
        {
            evaluated++;
            yield return 1;
        }

        var result = new PagedResultDto<int>(1, Lazy());

        Assert.Equal(1, evaluated);
        Assert.Equal([1], result.Items);
    }

    // record 的值相等语义：条目集合是引用比较，同内容不同实例不相等。
    // 写断言的人容易误以为 PagedResultDto 会逐项比较，这里把真实语义钉住。
    [Fact]
    public void Equality_compares_the_items_reference_not_the_contents()
    {
        IReadOnlyList<string> items = ["a"];

        Assert.Equal(new PagedResultDto<string>(1, items), new PagedResultDto<string>(1, items));
        Assert.NotEqual(new PagedResultDto<string>(1, ["a"]), new PagedResultDto<string>(1, ["a"]));
    }
}
