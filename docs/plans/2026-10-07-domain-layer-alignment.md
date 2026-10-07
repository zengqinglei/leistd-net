# 领域分层对齐（ABP DDD 实践）

依据：ABP DDD 文档与最佳实践、`template/docs/standards/coding-backend.md`、`coding-common.md` §3.2。框架未发布正式版，接受破坏性变更。

## 决策

- **值对象**：成组变化且有自身规则的属性收成值对象，继承框架 `ValueObject`（有构造不变量，防 `with` 绕过），`private` 无参构造供 EF，变更返回新实例；EF 用 `ComplexProperty` 映射。可空值对象至少含一个必需属性（EF 10 限制）。带索引的字段（`Email`、`Provider`/`ProviderUserId`）在 EF Core 11 支持复杂类型索引前保持平铺。
  - 列名取 EF 默认（`Lockout_IsLocked` 等），不写 `HasColumnName`。
  - `User.Lockout`：`LockoutState`（`IsLocked`、`End`、`AccessFailedCount`），承载记失败、到期解除、锁定判定。
  - `User.TwoFactor`：`TwoFactorCredential?`（`Secret`、`RecoveryCodes`、`LastUsedStep`），`null` 即未启用；删除 `TwoFactorEnabled` 列（由是否为 null 派生）。
  - `User.LastLogin`：`LoginTrace?`（`Time`、`Ip`）。
  - `ExternalLoginConnection.Profile`：`ExternalProfile`（`AccountLabel`、`Email`、`AvatarUrl`、`SyncedAt`）。
  - 删除无任何流程使用的 `PhoneNumberConfirmed`/`ConfirmPhoneNumber`。
- **映射用默认约定**（复核后撤回 snake_case 与列名占位约定）：表名、列名与第三方组件表名保持 EF 与组件默认，不写 `ToTable(名)`、`HasColumnType`；
  部分唯一索引与检查约束保留（全局查询过滤器不作用于约束，ABP 选择不建库级唯一约束、并发下可能重复），SQL 列名用 `nameof` 拼。
- **应用层协作类按职责命名**：`EmailChallengeService` → `EmailChallengeStore`，`SessionSignInService` → `SessionIssuer`（参照 ABP `AccountEmailer`）。
- **框架默认仓储只登记聚合根**（ABP 默认行为）：子实体可声明 DbSet（让表名走约定），不会得到仓储；模板约定测试改由框架保证。
- **同模块应用服务不互相调用**写入规范 §3.5，共用逻辑提为应用层协作类或下沉领域层。

## 任务

1. [x] ~~P0 缺陷~~（核实不成立：调用方已关闭软删除过滤，回归测试保留作守卫）。外部登录建号改走 `UserDomainService`（D4，消除查重重复、头像经 `SetAvatar`）。
2. [x] P1 规则下沉：头像校验进实体；`UpdateManagement` 去 `isActive`；管理员改资料查重进 `UserDomainService.UpdateManagementAsync`；`RoleDomainService.DeleteAsync`（内置、仍分配校验）与 `EnsureStaticRoleAsync`（种子共用）；解绑轮换安全版本移应用层并 `UpdateAsync`；`TenantSeeder` 清理补 `UpdateAsync` 与关闭软删除过滤；实体判定方法替换应用层字段组合；`User`/`Role` 构造守卫。
3. [x] P1 同模块调用：验证码、邮箱挑战校验提为协作类；结束会话走领域服务；规范 §3.5 补规则。
4. [x] 值对象：按决策实现四个值对象，映射、Mapster、测试同步；约定测试覆盖复杂属性内的枚举。
5. [x] P2 持久化：`UserRole` 唯一索引改 `(UserId, RoleId)` 过滤未删除；关系配置合并为一处；基线迁移、快照、Designer 同步，`migrations add` 探针核对。
6. [x] P3：`CredentialValidationResult` 移出 `ValueObjects`；过时注释；`PasswordPolicy` 错误码收进 `SecurityErrorCodes`（移到 Domain）；资源形态未用的 `User.Update` 删除。
7. [x] 映射用默认约定：撤回 snake_case 与列名占位；删写死表名与类型；协作类改名；全部迁移按默认命名重建。
8. [x] 规范 §3.3 值对象约定、§6 映射约定；升级指南 `0.13.0.md`；读取成本。
9. [ ] 验证：本地 9 个场景矩阵（后端）与反证已通过、已送审提交；推送后由 CI 跑全量与 PostgreSQL 端到端。
