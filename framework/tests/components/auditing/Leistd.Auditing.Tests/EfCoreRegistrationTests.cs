using Leistd.Auditing.Abstractions;
using Leistd.Auditing.EntityFrameworkCore;
using Leistd.TestBase.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Leistd.Auditing.Tests;

/// <summary>
/// <c>AddAuditingEfCore</c> 的注册面：重复调用不该多出描述符。
/// 审计属性设置器是工厂注册，重复时两条描述符构造等价、危害不大，
/// 但同一条「注册面就是契约」的规则在此家族也要成立，否则口径不一致。
/// </summary>
public class EfCoreRegistrationTests
{
    [Fact]
    public void Repeated_registration_adds_nothing()
    {
        ServiceCollectionAssertions.AssertIdempotent(services => services.AddAuditingEfCore());
    }
}
