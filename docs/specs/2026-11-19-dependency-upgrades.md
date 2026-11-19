# 第三方依赖升级：Angular 22.3、MailKit 5、Refit 17 与图表 1.x

状态：已完成 · 风险档：高 · 关联：PR #284 合并后的全量依赖盘点

## 1. 需求

### 背景

PR #284 合并后，对框架和模板的全部第三方依赖逐个核对了官方源的最新版：NuGet 79 个、前端 npm 56 个、GitHub Actions 10 个。.NET 10 系列和 Actions 都已是最新；下面几项值得现在升级。Dependabot 从未提过大版本组的 PR，所以大版本只能靠人工盘点。

### 范围（一次完成，一个 PR）

| 编号 | 内容 | 交付面 |
| --- | --- | --- |
| U1 | 前端 Angular 整套 22.2.3 → 22.3.0（build、cdk、cli、common、compiler、compiler-cli、core、forms、platform-browser、router），连同工具链小版本：Vitest 与 `@vitest/*` 5.1、Playwright 1.65、ESLint 10.15、`angular-eslint` 22.8、`typescript-eslint` 8.73、`eslint-plugin-import-x` 4.18、Prettier 3.10、Tailwind 与 `@tailwindcss/postcss` 4.3.5、PostCSS 8.5.33、`@tanstack/angular-table` 9.3.2、`tslib` 2.9、`lint-staged` 17.6.1、`stylelint-order` 8.1.3、`stylelint-declaration-block-no-ignored-properties` 3.1 | 模板前端 |
| U2 | stylelint 17 → 18、`stylelint-config-standard` 40 → 41 | 模板前端 |
| U3 | `@tanstack/charts` 与 `@tanstack/angular-charts` 0.16 → 1.5 | 模板前端 |
| U4 | StandardWebhooks 1.0.1 → 1.3.0、Testcontainers.PostgreSql 4.16 → 4.17 | 框架、模板后端 |
| U5 | MailKit 4.18.2 → 5.0.0 | 框架 |
| U6 | Refit、Refit.HttpClientFactory、Refit.Reflection 16.4 → 17.0（按官方迁移指南补齐 17 拆出的包） | 框架、模板后端 |

**不在范围**（已评估，暂不升级）：
- .NET 11 全列：EF Core、ASP.NET Core 11 只支持 .NET 11，而 Npgsql 的 EF 驱动 11 仍是 RC，下游也都在 .NET 10 LTS 上。
- TypeScript 7：Angular 22.3 要求 TypeScript 低于 6.1。
- Extensions 11、前端 SignalR 客户端 11：与运行时保持同号。
- 只有预发布版的：OpenIddict 8、Redis 4 等。

### 约束

- **不改线上契约**：Refit 17 改了默认 JSON 行为（枚举按数字、拒绝字符串数字、`object` 读成 `JsonElement`）。服务客户端的 JSON 线格式必须与升级前一致，`ServiceClientRefitSettings` 的现有约定（如枚举按 camelCase 字符串）照旧。需要显式配置的，在 `ServiceClientRefitSettings.Create()` 里补齐。
- **公共 API 不变**：`AddRefitServiceClient`、`ServiceClientRefitSettings` 的签名和语义不变。无法保持时先停下来问。
- **前端依赖维护规则**：按 `docs/template/quality-assurance.md#前端依赖维护` 执行。两套 package 与锁文件成对更新，不用 `--force` 或 `--legacy-peer-deps`。
- **图表外观不变**：U3 迁移到 1.x API 后，工作台图表的类型、配色、坐标轴和提示行为保持不变。

### 验收标准

| 编号 | Given / When / Then |
| --- | --- |
| AC1 前端 | Given 默认与本地化两套前端；When 安装、生产依赖审计、lint、构建、完整 `ng test`；Then 全部通过。`quality-assurance.md` 里 vite 那一行公告按实际审计结果更新（已清除就删掉这一行）。 |
| AC2 图表 | Given 工作台；When 渲染图表；Then 现有图表 spec 通过，浏览器截图与升级前一致。 |
| AC3 邮件 | Given SMTP 发信；When 跑邮件组件的全部测试（含协议测试）；Then 通过，且没有引入 `MimeKit.Cryptography`。 |
| AC4 Refit | Given 服务客户端；When 跑框架全部测试和模板矩阵代表场景；Then 通过。`RefitSettingsSerializationTests` 覆盖到 Refit 17 改了默认值的每一项（枚举、字符串数字、`object`），断言线格式与升级前相同。 |
| AC5 一致 | Given 框架与模板共用的包；When 比对两份 `Directory.Packages.props`；Then 版本一致，生成项目还原不报 NU1605。 |
| AC6 文档 | Given `framework/docs/components/service-client.md` 等提到 Refit 引用方式的文档；Then 与 Refit 17 的包拆分一致。 |

## 2. 方案

按官方迁移指南逐项升级，不顺带重构。

- **Refit 17**：依据官方 [v17 迁移指南](https://github.com/reactiveui/refit/blob/main/docs/breaking-changes.md#v17xx)，确认框架当前用的 `AddRefitClient` 属于生成客户端还是反射客户端，再选对应的包和注册入口。JSON 默认值的变化在 `ServiceClientRefitSettings.Create()` 里显式配置回原有行为。
- **MailKit 5**：依据 MailKit 的 `PortingGuide.md`。框架只用 SMTP 发信，预期只有命名空间调整。
- **图表 1.x**：依据 TanStack Charts 的 1.0 迁移说明，只改调用方式。
- **发版**：合并到 develop 后，框架的 `Directory.Packages.props` 变化会触发新的 beta；框架包对 Refit 和 MailKit 的依赖下限随之提高。下游通知由协调方另写。

## 3. 计划

| 任务 | 产物 | 验证 |
| --- | --- | --- |
| U1–U3 前端 | 两套 package 与锁文件、受影响的源码与 spec、`quality-assurance.md` 公告表 | 两套前端：安装、审计、lint、构建、完整 `ng test`（细指针与触屏两组），图表截图对比 |
| U4–U6 后端 | 两份 `Directory.Packages.props`、服务客户端与邮件组件的适配、序列化测试、相关文档 | 框架全部测试、包内容与隔离消费、模板矩阵代表场景（含有服务客户端与 Webhooks 的形态） |

## 4. 结果

U1–U6 在同一个 PR 交付。Angular 22.3.0 保留 Oxc 发布源码使用的 WASM runtime 1.2.5，以两套清单中同一条定向覆盖解决 peer 冲突。严格安装和实际 WASM 解析通过。两套生产依赖审计通过，Vite 8.3.8 无旧公告，已更新前端依赖维护表。三种生成形态的完整 lint、生产构建及浏览器测试均通过。

| 生成形态 | Test Files | 细指针 | 触屏 | 浏览器用例 | Node 配置用例 |
| --- | --- | ---: | ---: | ---: | ---: |
| 全功能，多语言开启 | `Test Files  181 passed (181)` | 1,730 | 374 | 2,104 | 19 |
| 全功能，多语言关闭 | `Test Files  175 passed (175)` | 1,673 | 364 | 2,037 | 19 |
| `identity-workspace` | `Test Files  189 passed (189)` | 1,758 | 382 | 2,140 | 19 |

- 工作台图表的 SVG 路径、坐标轴、配色、线宽和悬停提示在升级前后相同。有头浏览器截图保留了相同数据和视口，只有少量栅格化差异。
- 框架 32 个测试项目、2,933 个用例通过，零失败、零跳过。82 个候选包通过包内容与隔离消费检查。生成的 `resource-api-webhooks` 完成候选源还原、Release 构建和 361 个后端用例，无 NU1605。
- 邮件组件 54 例通过，含 SMTP 协议。依赖图为 MailKit 5.0.0 和 MimeKit.Core 5.0.0，无 MimeKit.Cryptography。
- 服务客户端 216 例通过，含 11 个序列化用例，证明 camelCase 枚举、字符串数字和 `object` 读作 `JsonElement` 的契约保持一致。注册签名沿用原契约，Refit 17 拆分的用法已同步到框架随包文档和模板。
- 两份中央依赖清单的 18 个共有包版本一致。Webhooks 组件 170 例通过。
