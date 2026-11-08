using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Leistd.EventBus.EntityFrameworkCore.EntityConfigurations;

// 消息头与坐标各存成一列 JSON 对象：只整体读写、不按头查询，不值得拆表。
// 比较器按内容比较，原地修改字典也能被变更跟踪发现。
internal static class MessageDictionaryConversion
{
    private static readonly ValueComparer<Dictionary<string, string>> Comparer = new(
        (left, right) => left!.Count == right!.Count && !left.Except(right).Any(),
        value => value.Aggregate(0, (hash, pair) => hash ^ HashCode.Combine(pair.Key, pair.Value)),
        value => new Dictionary<string, string>(value, value.Comparer));

    public static PropertyBuilder<Dictionary<string, string>> HasMessageDictionaryConversion(
        this PropertyBuilder<Dictionary<string, string>> property)
        => property.HasConversion(
                value => JsonSerializer.Serialize(value, JsonSerializerOptions.Default),
                json => JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonSerializerOptions.Default) ?? new Dictionary<string, string>(),
                Comparer)
            .IsRequired();
}
