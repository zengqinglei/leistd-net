# 前端项目

本项目基于 Angular、Spartan UI 和 Tailwind CSS 构建。

有关详细的开发规范、目录结构和编码准则，请参阅项目根目录下的 [前端开发规范](../docs/standards/coding-frontend.md)。

---

## 环境准备

在开始之前，请确保您已安装以下必需工具：

- **Node.js**: 满足 Angular 22 的 `^22.22.3 || ^24.15.0 || >=26.0.0`（即 22.22.3+、24.15.0+ 或 26+，其间的奇数主版本不受支持）
- **Angular CLI**: v22+

您可以通过以下命令验证是否已成功安装：

```bash
node --version
ng version
```

---

## 快速开始

### 1. 安装依赖

在首次克隆项目后，请先安装所有必需的依赖项：

```bash
npm install
```

### 2. 启动开发服务器

先按根目录 README 启动本机后端（默认 `http://localhost:5240`），再启动前端：

```bash
npm start
```

浏览器打开 `http://localhost:4200`。`npm start` 即 `ng serve`，使用 `development` 构建配置与 `src/environments/environment.ts`，
不需要复制或创建任何环境文件。

<!--#if (OpenIddictServer)-->
开发服务器按 `proxy.conf.mjs` 把 `/api`、`/hubs`（SignalR，含 WebSocket）以及授权服务的 `/connect`、`/.well-known` 转发给本机后端：
<!--#else-->
开发服务器按 `proxy.conf.mjs` 把 `/api`、`/hubs`（SignalR，含 WebSocket）转发给本机后端：
<!--#endif-->

浏览器只和 4200 打交道，前后端同源，Cookie 与回调地址都按 4200 生成，不需要跨域配置；热更新照常可用。
后端不在默认端口时，启动前设置 `API_PROXY_TARGET`：

```bash
API_PROXY_TARGET=http://localhost:5300 npm start
```

PowerShell：

```powershell
$env:API_PROXY_TARGET = "http://localhost:5300"; npm start
```
<!--#if (RemoteTokenAuth)-->

### 3. 联调本机 Identity 服务

本服务的登录在 Identity 服务上完成。本机联调时先按 Identity 服务自己的说明启动它（默认后端 5240、前端开发服务器 4200），
本服务改用其他端口：

```bash
# 后端（在 backend 目录）
dotnet run --project src/CompanyName.ProjectName.Api --urls http://localhost:5250
# 前端（在 frontend 目录；PowerShell 写 $env:API_PROXY_TARGET = "http://localhost:5250"; npm start -- --port 4201）
API_PROXY_TARGET=http://localhost:5250 npm start -- --port 4201
```

`environment.ts` 的 `oidc.authority` 与后端 `appsettings.Development.json` 的 `Authentication:Issuer` 默认都指向
`http://localhost:4200`，即 Identity 的前端开发服务器：浏览器在那里登录，令牌的签发方也是这个地址。
在 Identity 那边还需要：

- 把本服务后端的 `Authentication:Audience` 登记进 Identity 的 `OAuth:ApiResources`（如 Identity 目录下
  `dotnet user-secrets set "OAuth:ApiResources:0" "<本服务的 Audience>" --project src/<Identity 的 Api 项目>`），
  它会成为同名 scope，前端申请它得到的访问令牌受众就是本服务；
- 本机不需要为 4201 配置跨域：浏览器从 4201 请求 Identity 的前端开发服务器，开发服务器为 localhost 来源放行；
  部署时 Identity 的 `Cors:AllowedOrigins` 要加入本服务前端的源；
- 在「开放应用」里登记本服务的前端客户端（客户端 ID 见 `environment.base.ts` 的 `oidc.clientId`）：公共客户端、强制 PKCE，
  授予 `openid`、`profile`、`email`、`roles` 与上面那个 scope，回调地址 `http://localhost:4201/auth/callback`，登出回调 `http://localhost:4201`。
<!--#endif-->

### Mock

`environment.ts` 的 `useMock` 控制 Mock：`true` 全部由 Mock 应答；对象形态按接口开关，`include` 列出的接口走 Mock（列了 `include` 时以它为准），
命中 `exclude` 的一律走真实后端。只有 `development` 构建会把 Mock 编进包里，其他构建里 `useMock` 不起作用。

---

## 环境配置

项目使用 Angular 的环境配置系统。配置文件位于 `src/environments/`：

- `environment.ts` - 本机开发（`npm start` 使用，Mock 只在这个配置下可用）
- `environment.dev.ts` - 开发环境
- `environment.test.ts` - 测试环境
- `environment.uat.ts` - UAT 环境
- `environment.prod.ts` - 生产环境

### 主要配置选项

需要按环境改动的通常只有这几项（完整类型见 `src/environments/environment.base.ts`）：

```typescript
export const environment: Environment = {
  ...environmentBase,
  production: false,
  useMock: false, // true 开启全部 Mock；也可按模块传对象
  api: {
    ...environmentBase.api,
    gateway: '', // 网关地址；留空时请求保持相对路径，由同源部署或开发代理转发
  },
};
```

经网关访问、且网关按服务名分流时，给请求带上服务名（`GATEWAY_SERVICE_NAME` 由 `src/app/core/interceptors/url-format-interceptor.ts` 导出），拦截器会把它插在网关地址与路径之间：

```typescript
http.get('/api/v1/orders', {
  context: new HttpContext().set(GATEWAY_SERVICE_NAME, 'order-service'),
});
// → {gateway}/order-service/api/v1/orders
```
<!--#if (LocalIdentity)-->

哈希路由用 `useHash: true`（部署在无法配置回退规则的静态宿主时用得上）。
<!--#else-->

OIDC 授权服务器地址、客户端 ID 与 scope 在 `oidc` 下配置。

**部署要求**：回调地址是无 fragment 的普通路径 `/auth/callback`，反向代理或静态宿主
必须把它与其余 SPA 深链一并回退到 `index.html`，否则授权服务器跳回来时会命中 404。
本形态不提供哈希路由——哈希路由只从 fragment 读路由，回调组件不会被渲染。
<!--#endif-->

使用特定环境：

```bash
ng serve -c <环境名称>
ng build -c <环境名称>
```

---

## 构建项目

您可以根据目标环境构建项目。构建产物将存放在 `dist/` 目录下。

```bash
ng build -c dev         # 开发环境
ng build -c test        # 测试环境
ng build -c uat         # UAT 环境
ng build -c production  # 生产环境（已优化性能）
```

### Docker 构建

项目支持两种 Docker 部署模式：

#### 1. 同域部署（默认）

前后端在同一容器内，前端使用相对路径请求后端 API：

```bash
docker build -t company-name-project-name .
```

#### 2. 前后端分离部署

前端独立部署，需要指定后端 API 地址：

```bash
# 构建时传入后端 API Gateway 地址
docker build --build-arg API_GATEWAY=https://api.example.com -t company-name-project-name .
```

**说明**：

- `API_GATEWAY` 为后端 API 网关地址
- 构建时会替换 `environment.prod.ts` 中的占位符
- 如果不传入该参数，默认使用空字符串（相对路径）
- 前后端跨站时会话 Cookie 的配置见 [部署说明](../docs/deploy/README.md)

---

## 代码质量

### 代码检查

运行代码检查：

```bash
npm run lint           # 运行所有检查
npm run lint:ts        # TypeScript/HTML 检查
npm run lint:style     # CSS 检查
npm run format         # 检查代码格式
```

自动修复问题：

```bash
npm run lint:fix       # 修复所有可自动修复的问题
npm run lint:ts:fix    # 修复 TypeScript/HTML 问题
npm run lint:style:fix # 修复 CSS 问题
npm run format:fix     # 自动格式化代码
```

### 预提交钩子

项目使用 Husky 和 lint-staged 在提交前自动运行代码检查：

- TypeScript/HTML 文件使用 ESLint 检查
- CSS 文件使用 Stylelint 检查
- 所有文件使用 Prettier 格式化

---

## 运行测试

- **单元测试**：

  ```bash
  npm test
  ```

  > 单元测试由 Angular 的 `@angular/build:unit-test` 构建器驱动 Vitest，在 Playwright 的无头
  > Chromium 里运行（浏览器模式，不是 jsdom 模拟）。新机器首次运行前安装一次浏览器：
  > `npx playwright install chromium`。
  >
  > - 单次运行：`npm test -- --watch=false`；覆盖率：`npm test -- --watch=false --coverage`
  > - 观看有头浏览器：`npm test -- --browsers=chromium`
  > - 单测使用专用构建配置 `unit-test`（`angular.json`），不做开发构建的 Mock 替换，
  >   `_mock/core/providers.ts` 保持部署形态，Mock 相关用例才测得到两种构建的差别
  > - `vitest-base.config.ts` 开启 `restoreMocks` 与 `unstubGlobals`：每条用例开始前还原
  >   `vi.spyOn` 创建的替身与 `vi.stubGlobal` 替换的全局值，这两种不必手写清理；
  >   `Object.defineProperty` 等直接修改与假计时器仍由用例自己还原
  > - 每个 spec 文件在独立的页面里运行（`isolate: true`，构建器默认为了贴近 Karma 而共用一页）。
  >   共用一页时多个文件并发执行，一个文件在途用例对全局对象打的桩（如 `Storage.prototype`）
  >   会作用到另一个文件的模块初始化上，表现为偶发的"整个文件导入失败"
  > - 用例失败时 Vitest 在 spec 旁生成 `__screenshots__/`，已被 `.gitignore` 忽略

- **端到端 (E2E) 测试**：
  ```bash
  ng e2e
  ```
  > **注意**：项目默认未集成 E2E 测试框架，您可根据需要自行添加。

---

## 代码脚手架

使用 Angular CLI 可以快速生成各种类型的文件：

```bash
ng generate component your-component-name
ng generate service your-service-name
ng generate module your-module-name
ng generate --help  # 查看更多可用选项
```

---

## 推荐 VS Code 插件

- **Angular Language Service**: Angular 支持
- **TypeScript Importer**: 自动导入
- **Tailwind CSS IntelliSense**: CSS 智能提示
- **ESLint**: 代码检查支持
- **Prettier**: 代码格式化

---

## 项目结构

```
frontend/
├── src/
│   ├── app/              # 应用组件和模块
│   ├── assets/           # 静态资源（图片、字体等）
│   ├── environments/     # 环境配置
│   ├── index.html        # 主 HTML 文件
│   ├── main.ts           # 应用入口点
│   └── styles.css        # 全局样式
├── .eslintrc.json        # ESLint 配置
├── stylelint.config.mjs  # Stylelint 配置
├── tailwind.config.js    # Tailwind CSS 配置
├── angular.json          # Angular CLI 配置
└── package.json          # 依赖和脚本
```

---

## 更多资源

- **Angular CLI**: [官方文档](https://angular.dev/tools/cli)
- **Spartan UI**: [官方文档](https://spartan.ng/)
- **Tailwind CSS**: [官方文档](https://tailwindcss.com/docs)
