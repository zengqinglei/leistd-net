# claw-web 迁入 claw-server/frontend 方案

## 1. 需求理解

### 1.1 背景

`claw-web` 是当前 mybaiying.com 的主要业务官网，承载“百应 / OpenClaw / 龙虾广场 / 智能体”的公开内容，但它是 Astro 6 静态站点，没有用户体系、Token 使用能力和后端业务闭环。

`claw-server/frontend` 是 Angular 21 + PrimeNG 21 + Tailwind CSS 4 的业务前端，已经具备登录注册、工作区、平台管理、Token 使用、主题能力和 `_mock` 开发体系。现有 `/landing` 是 AiRelay 风格产品介绍页，代码结构符合项目前端规范，但内容方向偏 Token 中转介绍。

本次迁移不是把 Astro 文件平移到 Angular，而是将 `claw-web` 的业务内容、品牌资产、公开页面和内容数据，按 `claw-server/frontend` 的项目规范重构进现有 `features/public` 模块，并与当前 landing 页面合并。合并后首页以 `claw-web` 内容为主，代码实现以 Angular 21 + PrimeNG 21 + Tailwind CSS 4 项目规范为准。

### 1.2 目标

1. 将 `claw-web` 首页与 `claw-server/frontend` 现有 landing 页面合并，内容以 `claw-web` 首页为主。
2. 保留并改造 `features/public/components/landing` 作为公开站首页承载位置，不新增脱离现有规范的 `features/site`。
3. 将 `claw-web` 的 `/about`、`/agents`、`/agents/[name]`、`/plaza`、`/plaza/claws/[name]` 迁入 `features/public`。
4. 优先使用 PrimeNG v21 默认组件、PrimeNG design token 和 Tailwind CSS v4 utility class。
5. 遵循 Angular v21 代码风格：standalone components、signals、`computed()`、`input()`、`output()`、`inject()`、OnPush、懒加载路由。
6. DTO/model/service/enum 文件命名参考现有项目风格，例如 `open-application.dto.ts`、`open-application-service.ts`。
7. 页面涉及的前端 Service 必须通过 HttpClient 调用 API；开发阶段 mock API 和 mock data 必须放在 `claw-server/frontend/_mock` 中。
8. 保留 `/auth/**`、`/workspace/**`、`/platform/**` 现有功能和权限逻辑。

### 1.3 迁移范围

| 来源 | 迁移/合并目标 | 策略 |
| --- | --- | --- |
| `claw-web/src/pages/index.astro` | `features/public/components/landing` + `features/public/widgets/*` | 与现有 landing 合并，以 claw-web 首页内容为主，保留现有 Angular 组件拆分模式 |
| `claw-web/src/pages/about.astro` | `features/public/components/about` | 新增公开页，使用 PrimeNG/Tailwind 重构 |
| `claw-web/src/pages/agents/index.astro` | `features/public/components/agents` | 新增 Agent 列表页，搜索/筛选用 Signals + PrimeNG 输入组件 |
| `claw-web/src/pages/agents/[name].astro` | `features/public/components/agent-detail` | 新增 Agent 详情页，关联 Claw 数据 |
| `claw-web/src/pages/plaza/index.astro` | `features/public/components/plaza` | 新增龙虾广场页，Claw 卡片和对比表 |
| `claw-web/src/pages/plaza/claws/[name].astro` | `features/public/components/claw-detail` | 新增 Claw 详情页，关联 Agent 数据 |
| `claw-web/src/data/*.json` | `features/public/models` + `_mock/data/public-content.ts` | DTO 放源码，开发数据放 `_mock/data`，不在组件中直接散落 JSON |
| public 内容 API | `features/public/services/public-content-service.ts` + `_mock/api/public-content.ts` | Service 通过 HttpClient 调接口，mock 透明拦截 |
| `claw-web/public/**` | `claw-server/frontend/public/**` | 迁移静态资源，保持构建输出根路径访问 |

### 1.4 非目标

1. 不保留 Astro 运行时、构建链路、iframe 或微前端。
2. 不另建 `features/site`。
3. 不把 Astro 组件按文件名一比一平迁为 Angular widgets。
4. 不将业务数据硬编码散落在组件中。
5. 不绕过 `_mock` 体系直接在 `src/app/features/public` 放 mock 数据源。
6. 不在本阶段重构后端。
7. 不在首阶段强制启用 Angular SSR/prerender。

## 2. 现状分析

### 2.1 claw-web 现状

`claw-web` 是 Astro 6 + Tailwind CSS 4 静态站点，页面和内容结构清晰。

关键文件：

```text
claw-web/src/pages/index.astro
claw-web/src/pages/about.astro
claw-web/src/pages/agents/index.astro
claw-web/src/pages/agents/[name].astro
claw-web/src/pages/plaza/index.astro
claw-web/src/pages/plaza/claws/[name].astro
claw-web/src/components/Header.astro
claw-web/src/components/Footer.astro
claw-web/src/components/DownloadButton.astro
claw-web/src/components/HowItWorks.astro
claw-web/src/components/SocialProof.astro
claw-web/src/components/FAQSection.astro
claw-web/src/components/AgentCard.astro
claw-web/src/components/ClawCard.astro
claw-web/src/components/ComparisonTable.astro
claw-web/src/components/SkillList.astro
claw-web/src/data/agents.json
claw-web/src/data/claws.json
claw-web/src/types/index.ts
claw-web/public/**
```

可复用内容：

1. 首页主叙事：“养好你的龙虾”“一键安装、配置、运行各种智能体”。
2. 下载入口：
   - macOS：`https://www.mybaiying.com/release/latest/BaiYing-mac-universal-latest.dmg`
   - Windows：`https://www.mybaiying.com/release/latest/BaiYing-Setup-win-x64-latest.exe`
3. 导航结构：`首页 / 龙虾广场 / 智能体 / 关于我们`。
4. Agent/Claw 数据和图标。
5. SEO 文案、OG 图、JSON-LD 思路。

需要修正的问题：

- `agents.json` 中引用了 `/images/agents/*-preview.png`，但 `claw-web/public` 当前没有这些 preview 图片。首阶段应隐藏缺失截图区域或用明确占位图，不能产生坏图。

### 2.2 claw-server/frontend landing 现状

现有 landing 位于：

```text
claw-server/frontend/src/app/features/public/components/landing/landing.ts
claw-server/frontend/src/app/features/public/components/landing/landing.html
claw-server/frontend/src/app/features/public/widgets/header/header.ts
claw-server/frontend/src/app/features/public/widgets/header/header.html
claw-server/frontend/src/app/features/public/widgets/hero/hero.ts
claw-server/frontend/src/app/features/public/widgets/hero/hero.html
claw-server/frontend/src/app/features/public/widgets/features/features.ts
claw-server/frontend/src/app/features/public/widgets/features/features.html
claw-server/frontend/src/app/features/public/widgets/highlights/highlights.ts
claw-server/frontend/src/app/features/public/widgets/highlights/highlights.html
claw-server/frontend/src/app/features/public/widgets/footer/footer.ts
claw-server/frontend/src/app/features/public/widgets/footer/footer.html
claw-server/frontend/src/app/features/public/public.routes.ts
```

现有结构特点：

1. `Landing` 页面组合多个 public widgets。
2. Header、Hero、Features、Highlights、Footer 已按特性内 widgets 拆分。
3. 使用 PrimeNG `ButtonModule`、`RippleModule`、`StyleClassModule`、`TooltipModule` 等。
4. 使用 Tailwind class 和 PrimeNG design token，如 `text-surface-*`、`bg-surface-*`、`text-primary-*`。
5. Header 复用 `LogoComponent` 和 `ThemeConfigurator`。
6. 移动端菜单使用 `signal(false)`。

需要改进：

1. 当前 Landing 文案偏 AiRelay，合并后应切换为百应主叙事。
2. Header 需要补公开站导航。
3. 内容数据不应继续内联在组件中，应进入 DTO + Service + `_mock` 体系。

### 2.3 _mock 示例规范

现有可参考示例：

```text
claw-server/frontend/src/app/features/platform/models/open-application.dto.ts
claw-server/frontend/src/app/features/platform/services/open-application-service.ts
claw-server/frontend/_mock/data/open-applications.ts
claw-server/frontend/_mock/api/open-application.ts
claw-server/frontend/_mock/index.ts
claw-server/frontend/_mock/api/README.md
```

规范要点：

1. DTO 定义在 `src/app/features/.../models/*.dto.ts`。
2. 前端 Service 定义在 `src/app/features/.../services/*-service.ts`，通过 `HttpClient` 请求 `/api/v1/...`。
3. Mock 数据源放 `_mock/data/*.ts`，可从源码 DTO 导入类型。
4. Mock 拦截规则放 `_mock/api/*.ts`，使用 `MockRequest`、`MockException` 等核心类型。
5. 新增 mock API 后必须在 `_mock/index.ts` 中导出。
6. Service 层不判断 mock 模式，mock/真实环境由拦截器和 `environment.useMock.enable` 透明切换。

本次 public 内容应参考 `open-application` 示例，而不是在 `features/public/constants` 中直接放大段 mock 数据。

### 2.4 项目规范约束

依据：

```text
claw-server/docs/standards/code_standard/frontend_develop.md
claw-server/docs/standards/code_standard/common_develop.md
```

迁移必须遵循：

1. 组件文件命名：`landing.ts`、`agent-card.ts`，不使用 `.component.ts`。
2. Service 文件命名：`public-content-service.ts`、`seo-service.ts`。
3. DTO 文件命名：`public-content.dto.ts`。
4. Enum/model 文件命名：`public-content.enum.ts`、必要时 `public-content.model.ts`。
5. 页面级组件放 `features/public/components`。
6. public 特性内可复用展示组件放 `features/public/widgets`。
7. API 契约或可替换为 API 的数据结构使用 `*Dto` 命名，例如 `AgentOutputDto`、`ClawToolOutputDto`。
8. 状态使用 Signals，派生数据使用 `computed()`。
9. 依赖注入使用 `inject()`。
10. 展示型组件使用 `ChangeDetectionStrategy.OnPush`。
11. 样式优先使用 PrimeNG 组件和 Tailwind CSS v4。
12. Mock 相关代码必须与业务源码分离，并存放在 `_mock` 目录下。

## 3. 核心策略

### 3.1 框架策略：Angular 完全主导，Astro 只作为内容来源

`claw-web` 不作为运行时，不嵌入，不保留构建链路。所有公开页面均以 Angular 21 standalone components 重构。

迁移方式不是：

```text
Astro 页面 -> Angular 页面平移
Astro 组件 -> Angular widget 一比一平移
```

而是：

```text
claw-web 内容/信息架构/资产
  -> 按 claw-server/frontend 规范重新建模
  -> 使用 features/public 的 component/widget/model/service 分层实现
  -> 使用 _mock/api 与 _mock/data 承载开发数据和 mock 后端
  -> 使用 PrimeNG v21 + Tailwind CSS v4 + Angular Signals 重构交互
```

### 3.2 首页策略：与现有 landing 合并，以 claw-web 为主

首页承载位置保持为：

```text
features/public/components/landing
```

合并策略：

1. `/` 和 `/landing` 都指向 `features/public/components/landing/landing`。
2. `Landing` 继续作为页面组装组件。
3. 现有 `LandingHeader`、`LandingHero`、`LandingFeatures`、`LandingHighlights`、`LandingFooter` 改造成百应官网内容。
4. claw-web 首页内容优先级高于现有 AiRelay 文案。
5. AiRelay 能力可作为“后台/API 能力”局部能力点弱化保留，不作为首页主叙事。

首页内容合并映射：

| claw-web 首页内容 | 合并到现有 landing 组件 | 实施策略 |
| --- | --- | --- |
| Header 导航 | `widgets/header` | 改为“百应”品牌和公开页导航，保留主题切换、登录按钮 |
| Hero：“养好你的龙虾” | `widgets/hero` | 替换原 AiRelay Hero 文案，保留 PrimeNG Button 和主题 token |
| DownloadButton | 新增 `widgets/download-buttons` | PrimeNG Button + Tailwind，平台高亮用 signals |
| HowItWorks | 新增 `widgets/how-it-works` | 使用 PrimeNG Card 或 Timeline 风格表达三步上手 |
| 核心特性 | `widgets/features` | 从 `PublicContentService` 获取百应特性数据 |
| Agent 场景展示 | 新增 `widgets/agent-showcase` | 从 `PublicContentService` 获取 Agent 数据 |
| SocialProof | `widgets/highlights` | 改造为用户信任/生态亮点 |
| 底部 CTA | landing 末尾区块 | 使用下载按钮和登录入口 |

### 3.3 路由策略

推荐路由：

```text
/                  -> public landing（百应首页）
/landing           -> public landing（兼容旧入口）
/about             -> public about
/agents            -> public agents
/agents/:name      -> public agent detail
/plaza             -> public plaza
/plaza/claws/:name -> public claw detail
/auth/**           -> account routes
/workspace/**      -> workspace routes + authGuard
/platform/**       -> platform routes + authGuard + roleGuard(Admin)
```

`features/public/public.routes.ts` 扩展公开页面路由，`app.routes.ts` 保持主路由编排。

### 3.4 数据、后端服务与 Mock 策略

核心决策：

1. `features/public/services/public-content-service.ts` 使用 `HttpClient` 调用 API。
2. 首阶段后端真实接口可以暂不实现，但必须提供 `_mock/api/public-content.ts` 和 `_mock/data/public-content.ts`。
3. `_mock/data/public-content.ts` 由 `claw-web/src/data/agents.json`、`claw-web/src/data/claws.json` 转换而来。
4. 不在 `features/public/constants` 中保存大段 Agent/Claw mock 数据。
5. 如果需要前端常量，只保留 API endpoint、下载链接等配置型常量，不作为 mock data 源。

推荐 API：

```text
GET /api/v1/public-content/agents
GET /api/v1/public-content/agents/:id
GET /api/v1/public-content/agent-scenarios
GET /api/v1/public-content/claw-tools
GET /api/v1/public-content/claw-tools/:id
GET /api/v1/public-content/claw-tools/:id/agents
GET /api/v1/public-content/landing
```

`GET /api/v1/public-content/landing` 用于首页特性、三步上手、社会认同、下载链接等内容。这样 Landing 内容也通过 Service 获取，后续可由后端 CMS/配置接口接管。

### 3.5 目录和命名策略

不新增 `features/site`。公开站属于现有 `features/public`。

命名规则：

| 类型 | 命名 | 示例 |
| --- | --- | --- |
| 页面组件 | `{page-name}.ts/html` | `agents.ts`、`agent-detail.ts` |
| Widget | `{widget-name}.ts/html` | `agent-card.ts`、`download-buttons.ts` |
| DTO | `{feature}.dto.ts` | `public-content.dto.ts` |
| Enum | `{feature}.enum.ts` | `public-content.enum.ts` |
| Service | `{feature}-service.ts` | `public-content-service.ts` |
| Mock API | `{feature}.ts` | `_mock/api/public-content.ts` |
| Mock Data | `{feature}.ts` | `_mock/data/public-content.ts` |

DTO 命名参考现有 `OpenApplicationOutputDto`、`CreateOpenApplicationInputDto` 风格：

```text
LandingContentOutputDto
LandingFeatureOutputDto
HowItWorksStepOutputDto
SocialProofOutputDto
DownloadLinkOutputDto
AgentOutputDto
AgentSkillOutputDto
AgentRequirementsOutputDto
ClawToolOutputDto
GetAgentsInputDto
GetClawToolsInputDto
```

Enum/联合类型命名：

```text
AgentDifficulty
ClawToolDifficulty
DownloadPlatform
```

Service 命名：

```text
PublicContentService
SeoService
```

### 3.6 组件与样式策略

优先级：

1. PrimeNG v21 默认组件。
2. Tailwind CSS v4 utility class。
3. PrimeNG design token，例如 `surface`、`primary`、`text-color`。
4. 少量组件内 CSS。
5. 避免新增全局 CSS。

推荐 PrimeNG 使用点：

| 场景 | PrimeNG 组件 |
| --- | --- |
| CTA 按钮 | `p-button` |
| 搜索框 | `p-inputtext` / `IconField` / `InputIcon` |
| 场景筛选 | `p-select` |
| 难度/标签 | `p-tag` |
| FAQ | `p-accordion` |
| Agent/Claw 卡片 | `p-card` 或语义 HTML + Tailwind |
| 对比表 | 优先 `p-table`，简单静态可用 table + Tailwind |
| Tooltip | `p-tooltip` |
| Ripple | `pRipple` |

迁移时不使用 `claw-web` 的 `text-text`、`bg-surface` 等自定义 Tailwind token，改为项目现有 token：

```text
text-surface-900 dark:text-surface-0
text-surface-600 dark:text-surface-400
border-surface-200 dark:border-surface-700
bg-surface-0 dark:bg-surface-900
text-primary
bg-primary-50 dark:bg-primary-950/30
```

### 3.7 Logo 与品牌策略

品牌层级：

```text
公开官网主品牌：百应
生态/工具品牌：OpenClaw / 龙虾广场 / 智能体
后台/API 能力品牌：AiRelay
```

决策：

1. Landing Header/Footer 展示“百应”。
2. 复用 `shared/components/logo` 的图形能力。
3. favicon 使用 `claw-web/public/favicon.svg`。
4. 后台 `DefaultLayout` 是否显示 AiRelay 暂不在本阶段调整。
5. `/landing` 不再是旧 AiRelay 落地页，而是与 `/` 同一个百应首页兼容入口。

### 3.8 SEO 策略

新增：

```text
src/app/shared/services/seo-service.ts
```

职责：

1. 设置 title。
2. 设置 description。
3. 设置 OG tags。
4. 管理 JSON-LD script。
5. 路由切换时避免 JSON-LD 重复。

后续可补 Angular prerender。

## 4. 调整内容树形目录结构

```text
claw-server/frontend
├── _mock
│   ├── api
│   │   └── public-content.ts                     # 新增：public 内容 HTTP mock 规则
│   ├── data
│   │   └── public-content.ts                     # 新增：Agent/Claw/Landing mock 数据源
│   └── index.ts                                  # 新增 export './api/public-content'
├── public
│   ├── favicon.svg                              # 由 claw-web 迁入
│   ├── robots.txt                               # 由 claw-web 迁入
│   ├── fonts
│   │   ├── fonts.css                            # 由 claw-web 迁入
│   │   └── PlusJakartaSans.woff2                # 由 claw-web 迁入
│   └── images
│       ├── og-default.png                       # 由 claw-web 迁入
│       ├── agents
│       │   ├── schedule-assistant.svg
│       │   ├── content-creator.svg
│       │   ├── file-organizer.svg
│       │   ├── info-collector.svg
│       │   └── xiaohongshu-monitor.svg
│       └── claws
│           ├── openclaw.svg
│           ├── hermesclaw.svg
│           └── hiclaw.svg
├── src
│   ├── styles.css                               # 仅增加字体接入，保留现有 PrimeNG/Tailwind 设置
│   └── app
│       ├── app.routes.ts                        # 根路径加载百应 landing，保留认证/后台路由
│       ├── shared
│       │   └── services
│       │       └── seo-service.ts               # 新增通用 SEO 服务
│       └── features
│           ├── public                           # 公开站模块，承载百应官网
│           │   ├── public.routes.ts             # 扩展 landing/about/agents/plaza 路由
│           │   ├── models
│           │   │   ├── public-content.dto.ts
│           │   │   └── public-content.enum.ts
│           │   ├── services
│           │   │   └── public-content-service.ts
│           │   ├── components
│           │   │   ├── landing                  # 改造现有 landing，以 claw-web 首页内容为主
│           │   │   │   ├── landing.ts
│           │   │   │   └── landing.html
│           │   │   ├── about
│           │   │   │   ├── about.ts
│           │   │   │   └── about.html
│           │   │   ├── agents
│           │   │   │   ├── agents.ts
│           │   │   │   └── agents.html
│           │   │   ├── agent-detail
│           │   │   │   ├── agent-detail.ts
│           │   │   │   └── agent-detail.html
│           │   │   ├── plaza
│           │   │   │   ├── plaza.ts
│           │   │   │   └── plaza.html
│           │   │   └── claw-detail
│           │   │       ├── claw-detail.ts
│           │   │       └── claw-detail.html
│           │   └── widgets
│           │       ├── header
│           │       ├── hero
│           │       ├── features
│           │       ├── highlights
│           │       ├── footer
│           │       ├── download-buttons
│           │       ├── how-it-works
│           │       ├── agent-showcase
│           │       ├── agent-card
│           │       ├── claw-card
│           │       ├── comparison-table
│           │       └── skill-list
│           ├── account                          # 现有认证，保留
│           ├── workspace                        # 现有工作区，保留
│           └── platform                         # 现有平台管理，保留
└── docs
    └── sprints
        └── 2026
            └── 05sp1
                └── claw-web-frontend-integration-plan.md
```

说明：不再新增 `features/public/constants/public-content.constants.ts` 作为内容数据源。下载链接如果需要常量，可放在 mock landing data 中由 API 返回，或后续由后端配置接口返回。

## 5. 核心实现代码

### 5.1 app.routes.ts 形态

```ts
export const routes: Routes = [
  {
    path: '',
    loadComponent: () => import('./features/public/components/landing/landing').then(m => m.Landing)
  },
  {
    path: 'landing',
    loadComponent: () => import('./features/public/components/landing/landing').then(m => m.Landing)
  },
  {
    path: '',
    loadChildren: () => import('./features/public/public.routes').then(r => r.PUBLIC_ROUTES)
  },
  {
    path: 'auth',
    component: EmptyLayout,
    loadChildren: () => import('./features/account/account.routes').then(r => r.AUTH_ROUTES)
  },
  {
    path: 'workspace',
    component: DefaultLayout,
    canActivate: [authGuard],
    loadChildren: () => import('./features/workspace/workspace.routes').then(r => r.WORKSPACE_ROUTES)
  },
  {
    path: 'platform',
    component: DefaultLayout,
    canActivate: [authGuard, roleGuard],
    data: { role: 'Admin' },
    loadChildren: () => import('./features/platform/platform.routes').then(r => r.PLATFORM_ROUTES)
  },
  { path: '**', redirectTo: '' }
];
```

实际实现时也可以把 `/` 和 `/landing` 一并放进 `PUBLIC_ROUTES`，但必须避免 `path: ''` 路由和子路由匹配冲突。

### 5.2 public-content.dto.ts 形态

```ts
export interface DownloadLinkOutputDto {
  platform: DownloadPlatform;
  label: string;
  url: string;
}

export interface LandingFeatureOutputDto {
  title: string;
  description: string;
  icon: string;
  severity?: 'primary' | 'success' | 'info' | 'warn';
}

export interface HowItWorksStepOutputDto {
  title: string;
  description: string;
  icon: string;
}

export interface LandingContentOutputDto {
  title: string;
  subtitle: string;
  badgeText: string;
  downloadLinks: DownloadLinkOutputDto[];
  features: LandingFeatureOutputDto[];
  steps: HowItWorksStepOutputDto[];
  socialProofs: SocialProofOutputDto[];
}

export interface AgentOutputDto {
  id: string;
  name: string;
  tagline: string;
  description: string;
  icon: string;
  scenario: string;
  clawTool: string;
  difficulty: AgentDifficulty;
  skills: AgentSkillOutputDto[];
  requirements: AgentRequirementsOutputDto;
  screenshots: string[];
}

export interface GetAgentsInputDto {
  keyword?: string;
  scenario?: string;
  difficulty?: AgentDifficulty;
}
```

### 5.3 PublicContentService 形态

```ts
@Injectable({ providedIn: 'root' })
export class PublicContentService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api/v1/public-content';

  getLandingContent(): Observable<LandingContentOutputDto> {
    return this.http.get<LandingContentOutputDto>(`${this.baseUrl}/landing`);
  }

  getAgents(input?: GetAgentsInputDto): Observable<AgentOutputDto[]> {
    let params = new HttpParams();
    if (input?.keyword) params = params.set('keyword', input.keyword);
    if (input?.scenario) params = params.set('scenario', input.scenario);
    if (input?.difficulty) params = params.set('difficulty', input.difficulty);
    return this.http.get<AgentOutputDto[]>(`${this.baseUrl}/agents`, { params });
  }

  getAgent(id: string): Observable<AgentOutputDto> {
    return this.http.get<AgentOutputDto>(`${this.baseUrl}/agents/${id}`);
  }

  getClawTools(input?: GetClawToolsInputDto): Observable<ClawToolOutputDto[]> {
    // 按 open-application-service.ts 的 HttpParams 风格实现
    return this.http.get<ClawToolOutputDto[]>(`${this.baseUrl}/claw-tools`);
  }
}
```

### 5.4 _mock/data/public-content.ts 形态

```ts
import {
  AgentOutputDto,
  ClawToolOutputDto,
  LandingContentOutputDto
} from '../../src/app/features/public/models/public-content.dto';

export const MOCK_LANDING_CONTENT: LandingContentOutputDto = {
  title: '养好你的龙虾',
  subtitle: '一键安装、配置、运行各种智能体。不需要命令行，不需要懂技术，打开就能用。',
  badgeText: '简单三步，开启你的智能体之旅',
  downloadLinks: [
    {
      platform: 'mac',
      label: '下载 macOS 版',
      url: 'https://www.mybaiying.com/release/latest/BaiYing-mac-universal-latest.dmg'
    },
    {
      platform: 'windows',
      label: '下载 Windows 版',
      url: 'https://www.mybaiying.com/release/latest/BaiYing-Setup-win-x64-latest.exe'
    }
  ],
  features: [],
  steps: [],
  socialProofs: []
};

export const MOCK_AGENTS: AgentOutputDto[] = [];
export const MOCK_CLAW_TOOLS: ClawToolOutputDto[] = [];
```

实际实施时将 `claw-web/src/data/agents.json` 和 `claw-web/src/data/claws.json` 完整转换到这里。

### 5.5 _mock/api/public-content.ts 形态

```ts
import { MockException, MockRequest } from '../core/models';
import { MOCK_AGENTS, MOCK_CLAW_TOOLS, MOCK_LANDING_CONTENT } from '../data/public-content';

function getQueryValue(value: unknown) {
  const normalized = Array.isArray(value) ? value[0] : value;
  return normalized === undefined || normalized === null || normalized === '' ? undefined : String(normalized);
}

function getAgents(req: MockRequest) {
  const keyword = getQueryValue(req.queryParams['keyword'])?.trim().toLowerCase();
  const scenario = getQueryValue(req.queryParams['scenario']);
  const difficulty = getQueryValue(req.queryParams['difficulty']);

  return MOCK_AGENTS.filter(agent => {
    const matchesKeyword = !keyword ||
      agent.name.toLowerCase().includes(keyword) ||
      agent.tagline.toLowerCase().includes(keyword);
    const matchesScenario = !scenario || agent.scenario === scenario;
    const matchesDifficulty = !difficulty || agent.difficulty === difficulty;
    return matchesKeyword && matchesScenario && matchesDifficulty;
  });
}

function getAgent(req: MockRequest) {
  const agent = MOCK_AGENTS.find(item => item.id === req.params['id']);
  if (!agent) throw new MockException(404, 'Agent 不存在');
  return agent;
}

export const PUBLIC_CONTENT_API = {
  'GET /api/v1/public-content/landing': () => MOCK_LANDING_CONTENT,
  'GET /api/v1/public-content/agents': (req: MockRequest) => getAgents(req),
  'GET /api/v1/public-content/agents/:id': (req: MockRequest) => getAgent(req),
  'GET /api/v1/public-content/agent-scenarios': () => [...new Set(MOCK_AGENTS.map(agent => agent.scenario))],
  'GET /api/v1/public-content/claw-tools': () => MOCK_CLAW_TOOLS
};
```

并在 `_mock/index.ts` 中追加：

```ts
export * from './api/public-content';
```

### 5.6 Agents 页面状态形态

```ts
@Component({
  selector: 'app-agents',
  imports: [InputTextModule, SelectModule, AgentCard],
  templateUrl: './agents.html',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class Agents {
  private readonly publicContentService = inject(PublicContentService);

  protected readonly keyword = signal('');
  protected readonly selectedScenario = signal<string | null>(null);
  protected readonly agents = signal<AgentOutputDto[]>([]);

  loadAgents(): void {
    this.publicContentService
      .getAgents({ keyword: this.keyword(), scenario: this.selectedScenario() ?? undefined })
      .subscribe(agents => this.agents.set(agents));
  }
}
```

实际实现可增加 `toSignal()` 或 RxJS 防抖，但需与项目现有页面风格保持一致。

## 6. 实施计划

### 6.1 阶段一：修订方案文档

1. 将原“新增 `features/site`”方案修订为“改造 `features/public`”。
2. 明确首页与现有 landing 合并，以 `claw-web` 首页内容为主。
3. 明确 DTO/model/service/enum 命名和目录规范。
4. 明确后端服务、mock API、mock data 的位置和参考示例。

验证：

- 文档不再建议新增 `features/site`。
- 文档明确 `features/public/components/landing` 是首页承载点。
- 文档明确 `_mock/api/public-content.ts`、`_mock/data/public-content.ts`、`_mock/index.ts`。

### 6.2 阶段二：改造 public 路由

1. 修改 `app.routes.ts`，让 `/` 直接加载百应 landing。
2. 保留 `/landing` 兼容入口，也加载同一个 Landing。
3. 扩展 `features/public/public.routes.ts`，加入 about、agents、agent-detail、plaza、claw-detail。
4. 保留 `/auth`、`/workspace`、`/platform` 现有路由。

验证：

- `/` 和 `/landing` 打开同一首页。
- `/about`、`/agents`、`/plaza` 路由可命中。
- `/auth/login` 不受影响。

### 6.3 阶段三：迁移静态资源

1. 复制 `claw-web/public/favicon.svg`。
2. 复制 `robots.txt`、`fonts`、`images/og-default.png`。
3. 复制 `images/agents/*.svg`、`images/claws/*.svg`。
4. 保持资源在 `public`，不 import 到 TS bundle。

验证：

- `/favicon.svg` 可访问。
- `/images/agents/schedule-assistant.svg` 可访问。
- `/images/claws/openclaw.svg` 可访问。

### 6.4 阶段四：建立 public DTO、Service 和 Mock

1. 新增 `features/public/models/public-content.dto.ts`。
2. 新增 `features/public/models/public-content.enum.ts`。
3. 新增 `features/public/services/public-content-service.ts`，通过 HttpClient 请求 `/api/v1/public-content/*`。
4. 新增 `_mock/data/public-content.ts`，承载 `claw-web` 转换后的 landing、agents、claw-tools mock 数据。
5. 新增 `_mock/api/public-content.ts`，参考 `_mock/api/open-application.ts` 实现筛选、详情和 404。
6. 修改 `_mock/index.ts` 导出 `./api/public-content`。

验证：

- DTO/enum/service 命名符合项目规范。
- Mock 数据不在 `src/app/features/public` 中。
- Service 不判断 mock 模式，真实/mock 由拦截器透明切换。

### 6.5 阶段五：合并改造 landing 首页

1. 改造 `LandingHeader`：品牌改“百应”，增加公开站导航，保留登录和主题切换。
2. 改造 `LandingHero`：主文案来自 `PublicContentService.getLandingContent()`。
3. 改造 `LandingFeatures`：使用 API 返回的百应核心特性数据。
4. 新增 `HowItWorks` widget。
5. 新增 `AgentShowcase` widget，从 `PublicContentService.getAgents()` 获取数据。
6. 改造 `LandingHighlights` 为社会认同/生态亮点。
7. 改造 `LandingFooter` 为百应 Footer。

验证：

- 首页内容以 `claw-web` 为主。
- PrimeNG Button、Tag、Card 等组件优先使用。
- 样式仍使用 `surface`、`primary` token 和 Tailwind v4。
- Mock 模式下首页内容来自 `_mock/data/public-content.ts`。

### 6.6 阶段六：迁移公开子页面

1. 新增 About 页面，FAQ 使用 PrimeNG Accordion。
2. 新增 Agents 页面，搜索使用 PrimeNG InputText，筛选使用 PrimeNG Select，数据来自 `PublicContentService`。
3. 新增 AgentDetail 页面，数据来自 `/api/v1/public-content/agents/:id`。
4. 新增 Plaza 页面，Claw 卡片和对比表数据来自 `PublicContentService`。
5. 新增 ClawDetail 页面，关联 Agent 数据来自 public-content API。

验证：

- `/about` 内容完整。
- `/agents` 搜索筛选正常。
- `/agents/schedule-assistant` 可打开。
- `/plaza` 和 `/plaza/claws/openclaw` 可打开。

### 6.7 阶段七：SEO 和全局样式

1. 新增 `shared/services/seo-service.ts`。
2. 各公开页面调用 SEO 服务。
3. `styles.css` 只接入字体，不复制 `claw-web` 全局样式。

验证：

- title、description、OG、JSON-LD 正常。
- 后台 PrimeNG 样式不受影响。

### 6.8 阶段八：回归验证

执行：

```bash
cd claw-server/frontend
npm run lint
npm run build
npm start
```

浏览器验证：

```text
/
/landing
/about
/agents
/agents/schedule-assistant
/plaza
/plaza/claws/openclaw
/auth/login
/workspace
/platform
```

## 7. 风险点与验证方式

### 7.1 Mock 数据位置不符合规范

风险：为了迁移方便，把 Agent/Claw 数据放入 `src/app/features/public/constants`，导致 mock 数据和业务源码耦合。

处理：

- Agent/Claw/Landing 开发数据统一放 `_mock/data/public-content.ts`。
- HTTP mock 规则统一放 `_mock/api/public-content.ts`。
- `PublicContentService` 只使用 HttpClient 调 API。

验证：

- `src/app/features/public` 不包含大段 Agent/Claw mock 数据。
- `_mock/index.ts` 导出 public-content mock。
- Mock 模式下公开页面可正常加载。

### 7.2 首页合并导致旧 AiRelay landing 消失

风险：`/landing` 原先是 AiRelay 落地页，合并后会变成百应首页。

处理：

- 这是本次产品方向调整的预期结果。
- 如仍需保留旧 AiRelay 内容，可后续单独迁到 `/relay` 或后台能力介绍页，不作为本次默认方案。

验证：

- `/` 和 `/landing` 均显示百应首页。

### 7.3 迁移变成 Astro 平迁

风险：直接把 Astro 组件一比一搬成 Angular 文件，导致目录、命名、组件职责不符合项目规范。

处理：

- 所有公开内容归入 `features/public`。
- DTO/Service/Enum 命名参考现有 `OpenApplicationOutputDto`、`open-application-service.ts` 等风格。
- Mock 参考 `_mock/api/open-application.ts`、`_mock/data/open-applications.ts`。

验证：

- 不新增 `features/site`。
- 不出现 `.component.ts`。
- 不出现组件直接 import `src/data/*.json`。

### 7.4 PrimeNG 使用不足或样式体系割裂

风险：页面只使用 Tailwind 复刻静态页，和项目 PrimeNG 体验割裂。

处理：

- CTA 使用 `p-button`。
- 搜索使用 `p-inputtext`。
- 筛选使用 `p-select`。
- 标签/难度使用 `p-tag`。
- FAQ 使用 `p-accordion`。
- 对比表需要交互时使用 `p-table`。

验证：

- 页面中的交互控件优先来自 PrimeNG。
- 颜色使用 `surface`/`primary` token。

### 7.5 动态详情页刷新 404

风险：生产服务器刷新 `/agents/:name` 或 `/plaza/claws/:name` 返回 404。

处理：

- 部署服务器配置 SPA fallback 到 `index.html`。
- 后续需要 SEO 时启用 Angular prerender。

验证：

- 生产环境直接刷新 `/agents/schedule-assistant`。
- 生产环境直接刷新 `/plaza/claws/openclaw`。

### 7.6 Preview 图片缺失

风险：Agent 详情页引用不存在的 preview 图片。

处理：

- 首阶段隐藏缺失 screenshots 区域。
- 后续补真实 preview 图或占位图。

验证：

- 详情页无破损图片。

## 8. 待确认问题

当前没有阻塞设计的问题。

建议实施前确认两个产品取舍：

1. 旧 AiRelay landing 是否需要另存为 `/relay`；如果需要，应作为单独页面保留，不影响 `/` 和 `/landing` 合并为百应首页。
2. Agent preview 图片是否要补齐；如果不补齐，首阶段按本方案隐藏 screenshots 区域。
