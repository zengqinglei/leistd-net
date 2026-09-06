namespace Leistd.UnitOfWork.Database;

/// <summary>
/// 标记由工作单元协调的数据库资源。
/// </summary>
/// <remarks>
/// 刻意<b>不要求</b> <see cref="System.IDisposable"/>，与 <see cref="ITransactionApi"/> 相反：
/// 工作单元只释放它亲手创建的东西，而它亲手创建的一定是事务。
/// 需要按工作单元生命周期释放、又没有事务语义的资源，注册到 <c>IUnitOfWork.ServiceProvider</c> 即可。
/// </remarks>
public interface IDatabaseApi
{
}
