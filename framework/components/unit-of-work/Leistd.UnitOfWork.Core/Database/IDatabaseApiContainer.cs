namespace Leistd.UnitOfWork.Core.Database;

/// <summary>
/// 数据库 API 容器接口
/// </summary>
public interface IDatabaseApiContainer
{
    /// <summary>
    /// 按稳定 key 查找数据库 API
    /// </summary>
    IDatabaseApi? FindDatabaseApi(string key);

    /// <summary>
    /// 按稳定 key 添加数据库 API；同一 key 不允许被覆盖
    /// </summary>
    void AddDatabaseApi(string key, IDatabaseApi api);
}
