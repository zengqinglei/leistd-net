namespace CompanyName.ProjectName.Infrastructure.OperationRecords;

/// <summary>
/// 操作记录归档服务：把早于保留期的记录搬入归档表。
/// </summary>
/// <remarks>
/// <para><b>为什么不加在框架的 <c>IOperationRecordStore</c> 上</b>：那个契约明写
/// 「没有更新与删除……保留策略属于运维范畴，用数据库分区或归档作业处理」。
/// 给它开一个删除口子，就是凿穿它自己声明的不变量。归档因此落在模板侧，
/// 直接用本项目的 <c>DbContext</c>——框架的边界原样保留。</para>
/// <para><b>搬运而非删除</b>：数据仍在库里，只是不再参与日常查询。
/// 真要彻底销毁，那是另一个决定，需要另一次显式授权。</para>
/// </remarks>
public interface IOperationRecordArchiveService
{
    /// <summary>
    /// 把创建时间早于 <paramref name="cutoffUtc"/> 的记录分批搬入归档表。
    /// </summary>
    /// <param name="cutoffUtc">截止时刻（UTC，不含）。</param>
    /// <param name="batchSize">单批条数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>搬走的总条数。</returns>
    Task<int> ArchiveOlderThanAsync(
        DateTime cutoffUtc,
        int batchSize,
        CancellationToken cancellationToken = default);
}
