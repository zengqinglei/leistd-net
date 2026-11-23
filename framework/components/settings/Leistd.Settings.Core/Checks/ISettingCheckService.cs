using Leistd.Settings.Dtos;

namespace Leistd.Settings.Checks;

/// <summary>执行登记过的设置测试并保存结果。</summary>
public interface ISettingCheckService
{
    /// <summary>在设置页编辑的那一层执行一项测试：租户上下文为租户层，进程级设置为宿主层。</summary>
    /// <remarks>
    /// 设置未定义、对客户端不可见或没有登记测试时按 <c>Setting:NotAvailable</c> 拒绝；进程级设置在租户上下文下按
    /// <c>Setting:HostOnly</c> 拒绝。探针抛出的异常不外抛，记日志后按 <see cref="SettingCheckFailureCodes.UnexpectedError"/>
    /// 保存；取消照常抛出。授权由调用方负责（HTTP 端点按租户层写入的策略判断）。
    /// </remarks>
    /// <param name="settingName">主设置名。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<SettingCheckResultDto> RunAsync(string settingName, CancellationToken cancellationToken = default);
}
