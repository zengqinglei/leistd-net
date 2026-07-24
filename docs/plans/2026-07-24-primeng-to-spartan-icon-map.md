# primeicons → lucide 图标映射表（迁移工作参照）

> [PrimeNG → Spartan 迁移](./2026-07-24-primeng-to-spartan-migration.md)阶段 2–4 的图标替换依据。模板源全量用到 35 个 primeicons，逐个映射到 `@ng-icons/lucide` 等价名。所有 lucide 名已对 `@ng-icons/lucide@34` 实际导出核验存在（github 除外，见末尾）。
>
> 用法：`<ng-icon name="lucideXxx" />` + 组件级 `providers: [provideIcons({ lucideXxx })]`。

| primeicons (`pi pi-*`) | lucide (`@ng-icons/lucide`) | 用途参考 |
| --- | --- | --- |
| angle-down | lucideChevronDown | 下拉指示 |
| ban | lucideBan | 禁用/停用 |
| bars | lucideMenu | 汉堡菜单 |
| bell | lucideBell | 通知 |
| bolt | lucideZap | 快捷/强调 |
| check | lucideCheck | 勾选 |
| check-circle | lucideCircleCheck | 成功态 |
| cog | lucideSettings | 设置 |
| copy | lucideCopy | 复制 |
| database | lucideDatabase | 数据 |
| desktop | lucideMonitor | 系统主题 |
| exclamation-triangle | lucideTriangleAlert | 警告/确认 |
| globe | lucideGlobe | 语言/区域 |
| home | lucideHouse | 首页 |
| inbox | lucideInbox | 收件箱/空态 |
| info-circle | lucideInfo | 信息 |
| key | lucideKey | 密钥/密码 |
| lock | lucideLock | 锁定/安全 |
| moon | lucideMoon | 暗色模式 |
| palette | lucidePalette | 主题配色 |
| pencil | lucidePencil | 编辑 |
| plus | lucidePlus | 新增 |
| refresh | lucideRefreshCw | 刷新/重试 |
| search | lucideSearch | 搜索 |
| shield | lucideShield | 权限/安全 |
| sign-out | lucideLogOut | 登出 |
| sitemap | lucideNetwork | 组织结构 |
| sun | lucideSun | 亮色模式 |
| times | lucideX | 关闭/清除 |
| trash | lucideTrash2 | 删除 |
| user-edit | lucideUserPen | 编辑用户 |
| user-plus | lucideUserPlus | 添加用户 |
| users | lucideUsers | 用户列表 |
| verified | lucideBadgeCheck | 已验证 |

## 待决：github（1 处，login.html 外部登录按钮）

lucide **已下架 GitHub 等品牌 logo**，`@ng-icons/lucide@34` 无 `lucideGithub`。该图标仅用于 `--include-external-login` 场景的 GitHub OAuth 登录按钮。候选处置：

1. **自定义 SVG 图标**（推荐，保留品牌辨识度）：用 `provideIcons({ github: '<svg…>' })` 注入 GitHub 官方 SVG 路径，`<ng-icon name="github" />`。
2. 用通用替代（`lucideGithub` → `lucideCode` / `lucideExternalLink`）——损失品牌辨识度。
3. 用其他含品牌图标的 ng-icons 集（如 `@ng-icons/simple-icons` 含 `simpleGithub`），但需额外依赖。

> 决策待定，实施到 login 页（阶段 4 第 2 批）时确认。
