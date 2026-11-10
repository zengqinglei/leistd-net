#if (!IncludeOperationRecords)
using Microsoft.Extensions.Logging.Testing;

namespace CompanyName.ProjectName.IntegrationTests.Fixtures;

// 观察宿主日志管道的真实输出（经收集提供程序），不替换记录器或写入适配。
internal sealed class OperationRecordLogCapture(FakeLogCollector collector)
{
    private const string Category = "Leistd.OperationRecords";

    public FakeLogRecord[] Snapshot() => collector.GetSnapshot().Where(record => record.Category == Category).ToArray();

    public static string? Field(FakeLogRecord entry, string name) => LogRecordFields.State(entry, name);
}
#endif
