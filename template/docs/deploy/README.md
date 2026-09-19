# 部署说明

项目根 `Dockerfile` 和 `deploy/` 是当前容器部署配置的事实来源。部署前先核对实际镜像、环境变量、数据库迁移、健康检查和回滚能力，不使用文档中的假定值替代配置。

## 本地验证

```bash
docker compose -f deploy/docker-compose.yml -f deploy/docker-compose.override.yml up --build
docker compose -f deploy/docker-compose.yml -f deploy/docker-compose.override.yml down
```

命令和文件存在性应以当前项目为准。

## 生产边界

- 密钥和生产凭据由环境变量或密钥管理系统提供，不写入仓库。
- 操作记录默认**只增不减**。需要保留期时打开 `OperationRecordRetention:Enabled`（或由宿主管理员在系统设置的「审计」面板打开），
  到期记录会被搬进 `OperationRecordArchives` 表而不是删除；归档表不参与日常查询，但数据仍在库里，容量规划要把它算进去。
  配置里的开关与保留天数是基线，界面上的设置优先，归档任务每轮读取；执行时刻与批大小只在配置里。
  归档逐个进入宿主库与每个独立库，某个库失败只记错误、下一轮重试；多副本部署经分布式锁每轮只跑一份，要求配置 Redis，否则每个副本各跑一遍。
- 「只增不减」由应用契约保证。需要数据库层也保证时，把 Runtime Secret 对 `OperationRecords` 表的权限收敛到 INSERT／SELECT；
  代价是归档要从原表删除，**收敛权限与启用保留期不能同时成立**，二选一并在部署记录里写明。
- 操作记录表增长到按时间范围查询变慢时，再按时间分区（数据库层 DDL，由 `DbMigrator` 的 DDL 身份执行），不预先做。
- 模板不内置限流。对外提供登录的服务应在网关或 ASP.NET Core 限流中间件上按来源 IP 限流：
  应用内的失败登录计数按账号聚合，同一 IP 轮换账号的撞库不会触发它。
- API 启动时不执行 DDL。部署流水线先以 Migration Secret 运行一次性 `DbMigrator`，成功后再发布 API，详见 [后端迁移策略](../../backend/README.md#数据库迁移)。
- API 使用 Runtime Secret 且只持有 DML 权限；`DbMigrator` 使用独立 DDL 身份。部署前必须审查迁移，并明确备份、超时、失败恢复及新旧版本并存时的兼容性。
- **破坏性 schema 变更按 Expand → Backfill/Switch → Contract 三个有序阶段推进**，不在一次发布里完成。约束的是阶段顺序与下面两道闸门，不是发布次数——Backfill 可能是一次独立运维任务，也可能分多个批次：
  - `DbMigrator` 先于新 API 执行，此刻旧 Pod 仍在运行，因此 **Expand 阶段的迁移必须同时兼容当前生产版本和新版本**（只加不减：新增可空列、新增表、新增索引，不改语义、不删不改名）。
  - 全部目标迁移完成后，再发布新代码并完成 Backfill 或读写切换。
  - **确认旧版本实例全部退出后**，才在之后的受控发布里执行 Contract（删除旧列/旧表/兼容代码）。过渡结构只允许存在于这段窗口，不留长期历史兼容层。
  - CI 用真实数据库验证「当前生产版本 → 新版本」的升级路径，以及迁移重跑幂等。
- DedicatedDatabase 首次建库时，先对目标连接以 `ConnectionStrings__MigrationTarget` 运行每个服务的 DbMigrator，再在 Identity 创建租户。该模式只迁移当前服务的业务 schema，不会把 Identity Control schema 写入租户目标。
- 数据迁移、备份、回滚、健康检查和核心路径验证必须在执行前明确。
- 生产部署、回滚、重启、流量切换及真实数据操作必须由用户明确确认。

只有项目已经确定真实环境和运维流程时，才在本目录新增或更新文档。先参考最新同类记录，没有可参考内容时与负责人确认最小必要信息，不创建服务器配置或部署报告模板。
