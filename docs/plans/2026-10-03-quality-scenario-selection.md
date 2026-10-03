# 按开发场景选择验证：统一实施计划

状态：实现完成，本地回归与真实候选验收进行中。用户要求原剩余两项合并开发、统一审查；首批代码评审 M1 一并修复。任务全部完成后将结论保留在稳定规范/报告并删除本计划，Git 保留历史。

## 基线、范围与决策

原交付基线为 develop `549d106d`，首批待交付候选 `44f5aa55`，MR #36 仍为草稿，未合并。历史完整 PR 514 秒/1760 runner 秒；首批最终 484 秒/1718 runner 秒与内部文档 61 秒/62 runner 秒均为单次观测。≤510 秒中位目标未稳定验收。92.4% 只指早前本地 68 包消费入口，不是全流程或本轮收益。

1. 修复 M1：内部文档判定移入独立启动的 framework-pack，取消冗余规划 runner；打包作业 docs-only 也必须成功，下游显式判断范围，解除 pack 等另一 runner 的关键依赖。
2. Template：同候选输入计划绑定 SHA/档位。仅支持的前端源码、仅 backend/src/tests C# 分别选其阶段；跨层、配置、依赖、生成逻辑、未知或无效 base 保留完整阶段。场景依据现有 template.json 文件排除条件求值，纳入旧/新输入的生产场景与默认/全特性代表，不复制场景清单。完整静态及真实 PG/OIDC 保留。
3. Framework：模板源码变化不重跑输入未变的框架测试，但仍打包并核对全部包内容/完整性。Framework 局部 C# 只裁剪空隔离消费的 restore/build，按候选 nuspec 反向传递依赖选消费者；框架全量测试、模板完整阶段及 PG/OIDC 保留。共享、包配置、显式跨项目编译及未知输入消费者全量。用户最新要求恢复实施此项，取代此前从路线图移除的决定；不按 ProjectReference 裁剪运行时责任。
4. 回执/汇总：独立预期计划验证准确场景、阶段、SHA、档位和作业 success/skipped；省略阶段写 not-applicable，拒绝伪造跳过、少场景、错 SHA 和缺输出；默认人工/full/复用保持完整。
5. 业务项目沿用已落地 testing.md 的小 bug、新模块、跨模块等阶段规则；不运行仓库模板矩阵，不编造未测的业务项目加速率。

去重/裁剪按责任、缺陷检测、输入闭包与净 DAG 成本验收，不套用相同工作量重排的高噪声 runner 门槛。所有运行耗时与取消/失败成本披露；单次不得宣称稳定 SLA。局部消费者只减少空消费项目，不以该构建结果冒称真实 API/DI 行为已验证。

## 实施与验收清单

- [x] ci.yml 中 scope/计划迁入 pack，动态作业显式条件，保留必过汇总和候选一致性。
- [x] 同候选规划器、人工显式计划入口、未知/脏树全量；唯一场景定义与模板条件复用。
- [x] 模板阶段/场景裁剪与独立预期回执；默认完整/full 拒绝局部证明。
- [x] 全包内容与缺失依赖核验，隔离消费候选 nuspec 闭包。
- [x] 仓库质量规范和 Framework Skill 同步，场景清单不复制。
- [ ] 完整 workflow 夹具与聚合负例、实际生成对照、源码预检变异、包内容/闭包负例全部通过。
- [ ] 真实普通代码 PR、前端/后端/Framework/内部文档 PR 与失败负例；绑定候选的 full，适用容器回执。
- [ ] 场景前后工作量/实测耗时、准备和新增计划成本，更新报告及 MR。
- [ ] 统一提交/推送，Claude 只读代码审查；主动监控，修复必须项并复验。
- [ ] 稳定文档/报告收敛，任务完成删除本计划。本轮未授权合并新 MR，不合并。

## 验证入口与证据

- pwsh scripts/check-all.ps1：现有完整静态责任不削减。
- python scripts/test-workflow-change-scope.py：真实 YAML 步骤、完整差异/rename/浅克隆、必要作业失败和计划身份。
- python scripts/test-quality-validation-plan.py：真实 Git 输入/保守回退、六种产品前后生成与省略输入哈希对照，错误场景/阶段/模式/SHA 回执拒绝。只有模板引擎随机 UserSecretsId 值归一化，XML 结构仍核对。
- python scripts/test-template-source-preflight.py：三类实际源码缺陷、人工入口与静态接替、清单缺项及失败传播。
- python scripts/test-package-consumer-selection.py --benchmark：真实候选包闭包、未选包 XML/漏包/缺依赖/未知种子、交替三轮消费耗时。
- actionlint 与官方 Skill 校验；真实 PR 日志/回执，不用 dispatch 冒充 PR。

回归入口只在相应机制维护时运行，不加入日常静态闸门。原始记录在 .tmp/quality-unified-20261003/，长期证据进入 docs/reports/2026-10-03-quality-scenario-selection.md 和 MR #36。真实验证基准仅扩展 PR 目标分支触发，其他作业/条件/命令保持候选一致；临时分支/PR 清理，不进入交付分支。
