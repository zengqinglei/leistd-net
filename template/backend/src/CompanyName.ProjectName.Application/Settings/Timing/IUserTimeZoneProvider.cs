namespace CompanyName.ProjectName.Application.Settings.Timing;

/// <summary>
/// 当前用户的展示时区
/// </summary>
/// <remarks>
/// 时间一律以 UTC 存储，只在展示时换算。需要在服务端产出给人看的时间文本时注入本服务——
/// 导出文件、报表、通知正文里的绝对时刻都属此类；纯粹返回给前端渲染的 DTO 不需要，
/// 那些保持 UTC，由前端按同一份设置换算。
/// </remarks>
public interface IUserTimeZoneProvider
{
    /// <summary>
    /// 解析当前用户的展示时区。
    /// </summary>
    /// <remarks>
    /// 三层（用户 → 租户 → 代码默认值）都没有值，或取到的值已失效（例如时区数据库更名），
    /// 才回落到服务器时区——这是本消费端对「最终缺值」的降级选择，不是设置层的语义。
    /// 展示时间不该因为一个偏好设置坏掉而整页报错。
    /// </remarks>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<TimeZoneInfo> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 把 UTC 时刻换算到当前用户的展示时区。
    /// </summary>
    /// <param name="utc">UTC 时刻。<c>Kind</c> 不是 <see cref="DateTimeKind.Utc"/> 时按 UTC 解释。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>换算后的时刻，<c>Kind</c> 为 <see cref="DateTimeKind.Unspecified"/>——它已不是 UTC，也未必是本机本地时间。</returns>
    Task<DateTime> ToUserTimeAsync(DateTime utc, CancellationToken cancellationToken = default);

    /// <summary>
    /// 判断时区标识是否可用。
    /// </summary>
    /// <remarks>
    /// 写入设置前用它挡掉无效值。与 <see cref="GetAsync"/> 共用同一套判定，
    /// 不会出现「写得进去却解析不出来」。
    /// </remarks>
    /// <param name="timeZoneId">IANA 时区名，如 <c>Asia/Shanghai</c>。</param>
    bool IsValidId(string timeZoneId);
}
