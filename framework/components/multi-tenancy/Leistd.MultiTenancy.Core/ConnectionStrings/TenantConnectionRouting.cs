namespace Leistd.MultiTenancy.ConnectionStrings;

// "本进程注册了租户连接路由"这件事本身，由两个解析入口各放一份。
//
// 逐库枚举要先知道有没有独立库可列，而这件事没有别的地方能读到：
//   - IConnectionStringResolver 不行。它的契约写明"与多租户无关"，随包文档还示范了
//     按配置取连接串的单库实现；拿它当判据，单库项目注册自己的解析器就会被误判成分库。
//   - ITenantDatabaseDirectory 也不行。它是控制库的存储，只承担控制面、自己不分库的
//     服务照样有它。
// 两个都试过，两次都是代理变量。所以由注册方直接把结论放进容器。
//
// internal：标准的本地/远端入口都在框架内，宿主不需要这个类型。真要支持第三方自带
// 完整路由实现时再开一个窄注册入口，不为不存在的需求先加公共 API。
internal sealed class TenantConnectionRouting
{
    internal static TenantConnectionRouting Instance { get; } = new();

    private TenantConnectionRouting()
    {
    }
}
