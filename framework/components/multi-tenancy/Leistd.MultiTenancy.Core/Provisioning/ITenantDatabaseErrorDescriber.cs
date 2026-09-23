using Leistd.ExceptionHandling;

namespace Leistd.MultiTenancy.Provisioning;

/// <summary>
/// 把开通时的数据库错误翻译成调用方能照着修正的业务失败。
/// </summary>
/// <remarks>
/// <para>开通失败最常见的原因是那个专属库用不了：连不上、库不存在、建好了没迁移、凭据不对。四种都是调用方能改对的，
/// 不应仅因异常出自数据库驱动就丢失这一语义。错误码由 <see cref="Leistd.MultiTenancy.Errors.MultiTenancyErrorCodes"/>
/// 定义、默认译文随组件分发，把本引擎的错误码映射到它们由宿主实现——错误码表是各数据库自己的方言，框架不替宿主选一种。</para>
/// <para><b>只翻译确定由调用方输入引起的错误</b>：认不出来就返回 <see langword="null"/>，保留原异常。
/// 库重启、连接数耗尽、序列化失败等基础设施故障不得翻译为输入错误。</para>
/// <para>翻译结果一律不回显连接串、主机与端口：错误响应会同时进入响应体、前端提示与服务端日志。</para>
/// </remarks>
/// <example>
/// <code>
/// internal sealed class PostgresTenantDatabaseErrorDescriber : ITenantDatabaseErrorDescriber
/// {
///     public BusinessException? Describe(Exception error) =&gt; /* 按 SQLSTATE 分流 */ null;
/// }
///
/// // 在组件注册之前登记，即可替换默认的“不翻译”实现
/// builder.Services.AddSingleton&lt;ITenantDatabaseErrorDescriber, PostgresTenantDatabaseErrorDescriber&gt;();
/// </code>
/// </example>
public interface ITenantDatabaseErrorDescriber
{
    /// <summary>翻译开通失败；不是调用方能改的原因时返回 <see langword="null"/>，原异常照常抛出。</summary>
    /// <param name="error">开通或启用时抛出的异常。</param>
    BusinessException? Describe(Exception error);
}

// 默认不翻译：错误码表随数据库而异，框架不预置某一种方言。宿主注册自己的实现即可替换。
internal sealed class NullTenantDatabaseErrorDescriber : ITenantDatabaseErrorDescriber
{
    public BusinessException? Describe(Exception error) => null;
}
