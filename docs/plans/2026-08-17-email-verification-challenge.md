# 注册邮箱验证挑战实施计划

> 执行时使用系统化调试、测试驱动开发与 Template 交付流程；每个行为先验证失败，再实现并验证通过。

**目标：** 用一次性、按租户和用途隔离的邮箱验证挑战替换“按全局邮箱保存明文验证码”的实现。

**架构：** 发送端创建随机 `ChallengeId`，在分布式缓存中保存租户、用途、规范化邮箱、验证码慢哈希、过期时间与剩余尝试次数；分布式锁保护频控发放、尝试递减和成功消费。注册端不再接受脱离挑战的验证码，前端在邮箱变更时丢弃旧挑战。

**技术栈：** ASP.NET Core 10、`IDistributedCache`、`IDistributedLock`、PBKDF2 现有 `IPasswordHasher`、Angular 22 Signal Forms、xUnit/TestServer、Jasmine/Karma。

---

## 契约与不变量

- 发送接口返回 `challengeId`、`expiresInSeconds`和 `retryAfterSeconds`。
- 注册入参使用 `emailVerification: { challengeId, code }`；不保留旧 `emailVerificationCode` 字段。
- 挑战绑定 `TenantId/Host + Purpose + NormalizedEmail`，任一不匹配都不允许使用。
- 验证码不以明文落缓存；比较经现有 PBKDF2 哈希器完成。
- 默认最多 5 次错误尝试，耗尽即销毁；成功验证立即消费，不能重放。
- 租户或邮箱不匹配不破坏原挑战，避免跨租户拒绝服务。
- 频率限制与挑战状态分开，均按租户和邮箱隔离；邮箱以 SHA-256 摘要出现在 key 中，不泄露 PII。

## 任务 1：后端失败用例

**文件：**

- 新增 `template/backend/tests/CompanyName.ProjectName.IntegrationTests/EmailVerificationChallengeTests.cs`

- [x] 使用真实 TestServer、分布式缓存和锁，只替换图形验证码与邮件发送边界。
- [x] 证明租户 A 的挑战不能在租户 B 使用，且 B 的拒绝不会销毁 A 的挑战。
- [x] 证明挑战与邮箱绑定、错误尝试耗尽后失效、成功后不能重放。
- [x] 证明同一邮箱的发送频控按 Host/租户隔离。
- [x] 运行定点测试，确认因当前接口无挑战契约而失败。

## 任务 2：挑战核心与 HTTP 契约

**文件：**

- 新增 `template/backend/src/CompanyName.ProjectName.Application/Auth/Dtos/EmailVerificationDtos.cs`
- 修改 `template/backend/src/CompanyName.ProjectName.Application/Auth/AppServices/IEmailVerificationAppService.cs`
- 修改 `template/backend/src/CompanyName.ProjectName.Application/Auth/AppServices/EmailVerificationAppService.cs`
- 修改 `template/backend/src/CompanyName.ProjectName.Application/Auth/AppServices/AuthAppService.cs`
- 修改 `template/backend/src/CompanyName.ProjectName.Application/Auth/Dtos/RegisterInputDto.cs`
- 修改 `template/backend/src/CompanyName.ProjectName.Domain/Users/Options/UserRegistrationOptions.cs`
- 修改 `template/backend/src/CompanyName.ProjectName.Api/Controllers/AuthController.cs`
- 修改 `template/backend/src/CompanyName.ProjectName.Api/appsettings.json`

- [x] 实现挑战序列化模型、隔离 key、发送频控和邮件失败补偿。
- [x] 实现持锁验证：过期删除、错误递减、耗尽删除、成功删除。
- [x] 让注册用例只接受新挑战契约，所有失败统一对外表现为验证信息无效或过期。
- [x] 运行定点后端测试至全绿。

## 任务 3：前端与 Mock 闭环

**文件：**

- 修改 `template/frontend/src/app/features/account/models/account.dto.ts`
- 修改 `template/frontend/src/app/features/account/services/account-service.ts`
- 修改 `template/frontend/src/app/features/account/components/register/register.ts`
- 修改 `template/frontend/src/app/features/account/components/register/register.spec.ts`
- 修改 `template/frontend/_mock/api/auth.ts`

- [x] 先增加前端失败用例：发送后保存挑战 ID、注册携带嵌套挑战、邮箱变更丢弃旧挑战。
- [x] 更新 DTO 和 AccountService 响应类型。
- [x] 注册页只在当前邮箱拥有有效挑战时允许提交邮箱验证。
- [x] Mock 为挑战建立一次性内存状态，不再把发送端当成无返回值的空操作。
- [x] 运行注册组件单测、lint 和 build。

## 任务 4：生成场景验收

- [x] 生成 `default`、`tenancy` 和 `tenancy-illegal` 场景，确认条件块与新 DTO 无残留标记。
- [x] 对生成项目运行后端测试、前端 lint/build。
- [x] 核对实现与本计划的每条不变量，如实记录未执行项。
