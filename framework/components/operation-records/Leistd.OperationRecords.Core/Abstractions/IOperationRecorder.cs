namespace Leistd.OperationRecords.Abstractions;

/// <summary>
/// 在关键操作成功或被拒之后显式调用的审计记录器。
/// </summary>
/// <remarks>
/// <para><b>刻意是显式调用，不做成拦截器自动记录。</b>"哪些操作值得留痕"是业务判断：
/// 自动记录要么漏掉没有统一切面的路径，要么把查询也记进去，让真正重要的几十条淹没在几万条里。
/// </para>
/// <para><b>框架只定义它自己会读的字符串。</b>动作码与授权依据框架都只是原样存下去、
/// 从不解释，因此两者都由业务项目定义（如 <c>identity.user.created</c>、
/// <c>AuthenticatedSelf</c>），框架不提供枚举也不提供常量——它穷举不了业务词汇，
/// 硬定一套只会逼着业务去凑。反过来，模拟登录的 claim 名框架要<b>读</b>，读取方与签发方必须一致，
/// 因此由 <c>OperationRecordOptions</c> 提供，默认值指向 <c>CustomClaimTypes</c>——
/// 全框架的自定义 claim 名在那里统一归口，签发方换了名字由宿主改配置，组件不写死。</para>
/// <para>动作码与授权依据两个字符串<b>必填且不得为空白</b>：落库一个空串之后，
/// "这次操作不需要授权依据"与"调用方漏传了"就再也分不开，而审计表里一个分不清含义的列
/// 等于没有这一列。<b>目标不在此列</b>——<see cref="OperationTarget"/> 自己把空白吸收成
/// <see cref="OperationTarget.None"/>，空白标识在类型层面就构造不出来，
/// 校验点从记录器前移到了值对象。</para>
/// <para><b>两个方法的签名刻意不对称：成功路径没有失败原因参数。</b>
/// 成功不存在"为什么没成"；给它一个永远传 <see cref="OperationFailure.None"/> 的参数，
/// 只会诱导调用方往里塞与结果矛盾的内容。</para>
/// </remarks>
public interface IOperationRecorder
{
    /// <summary>记录一次成功的操作。</summary>
    /// <remarks>
    /// <b>落在调用方所处的事务边界里</b>：有环境工作单元就跟随它，业务回滚则记录一并回滚——
    /// 成功记录必须与它描述的那次变更同生共死，否则会留下"记了但没发生"的假账。
    /// 写入失败照常上抛，让业务一起失败。
    /// </remarks>
    /// <param name="action">业务动作码，与被拒路径使用的值逐字一致。</param>
    /// <param name="target">
    /// 操作目标：标识与名字快照捆绑传递；无目标（如创建类动作）传
    /// <see cref="OperationTarget.None"/>。
    /// </param>
    /// <param name="authorizationBasis">授权依据，由业务定义：通常是权限名；不由权限把守的操作传业务自己的标记。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task RecordSucceededAsync(
        string action,
        OperationTarget target,
        string authorizationBasis,
        CancellationToken cancellationToken = default);

    /// <summary>记录一次被拒绝或失败的操作。</summary>
    /// <remarks>
    /// <para><b>写失败只记日志、不抛出</b>：被拒的请求本来就要以 403/400 结束，
    /// 不能因为这一条审计没记下来变成 500，把真正的拒绝原因盖掉。取消照常向上传播。</para>
    /// <para><b>本方法不开独立事务。</b>授权阶段的拒绝没有环境工作单元，写入即时生效；
    /// 而业务在工作单元内部拒绝并抛出时，这条记录会随回滚一起消失——要留住它，
    /// 由宿主在工作单元之外调用。组件不替宿主开第二个事务：那会与外层未提交的写入互相加锁，
    /// 而只有宿主知道自己的锁分布。</para>
    /// </remarks>
    /// <param name="action">业务动作码，与成功路径使用的值逐字一致。</param>
    /// <param name="target">操作目标；无法解析时传 <see cref="OperationTarget.None"/>。</param>
    /// <param name="authorizationBasis">授权依据：被检查的权限名，或非权限体系的标记。</param>
    /// <param name="failure">
    /// 失败原因。可枚举的业务拒绝走错误码，技术异常走
    /// <see cref="OperationFailure.FromDetail"/>——后者必须显式传入可公开的说明，
    /// 不要传原始异常文本，理由见 <see cref="OperationFailure"/>。
    /// </param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task RecordFailedAsync(
        string action,
        OperationTarget target,
        string authorizationBasis,
        OperationFailure failure = default,
        CancellationToken cancellationToken = default);
}
