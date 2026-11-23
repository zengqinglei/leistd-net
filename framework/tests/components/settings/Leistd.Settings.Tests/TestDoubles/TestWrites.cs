using Leistd.Settings.Stores;

namespace Leistd.Settings.Tests.TestDoubles;

// 直接调用存储写入时的修改信息：时间固定、无主体，断言不关心写入人的用例都用它
internal static class TestWrites
{
    public static SettingModification Anonymous { get; } = new(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), null, null);
}
