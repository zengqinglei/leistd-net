using Leistd.OperationRecords.Definitions;
using Leistd.OperationRecords.Models;
using Leistd.OperationRecords.Queries;
using Leistd.OperationRecords.Recording;
using Leistd.OperationRecords.Stores;

namespace Leistd.OperationRecords.Definitions;

// 聚合所有 IOperationActionDefinitionProvider 的登记结果，提供只读索引。
//
// 用行内注释而不是 ///：本类型是 internal，XML 注释会进随包 .xml，
// 把实现说明泄露成对外文档（闸门 check-doc-comment-shape 据此拦截，
// 判据见 docs/framework/development-guide.md §4.1「信息分层」）。
//
// 单例且在构造时一次性求值：定义是启动期事实，不随请求变化。
// 重复的动作码会在这里（即应用启动时）抛出，而不是等到第一次记录时才炸。
internal sealed class OperationActionDefinitionManager : IOperationActionDefinitionManager
{
    private readonly Dictionary<string, OperationActionDefinition> _byCode;
    private readonly IReadOnlyList<IOperationActionDefinition> _all;
    private readonly IReadOnlyList<string> _categories;

    public OperationActionDefinitionManager(IEnumerable<IOperationActionDefinitionProvider> providers)
    {
        var context = new OperationActionDefinitionContext();
        foreach (var provider in providers)
        {
            provider.Define(context);
        }

        var definitions = context.GetAll();
        _byCode = definitions.ToDictionary(definition => definition.Code, StringComparer.Ordinal);
        _all = [.. definitions];

        // 类别按首次出现顺序去重：界面的筛选项顺序应当跟随登记顺序，
        // 而不是字典序——后者会把"认证"排到"账号"后面，与人的心智顺序相反。
        _categories = [.. definitions.Select(definition => definition.Category).Distinct(StringComparer.Ordinal)];
    }

    public IOperationActionDefinition? GetOrNull(string code)
        => !string.IsNullOrWhiteSpace(code) && _byCode.TryGetValue(code, out var definition) ? definition : null;

    public IReadOnlyList<IOperationActionDefinition> GetAll() => _all;

    public IReadOnlyList<string> GetCategories() => _categories;
}
