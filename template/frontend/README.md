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

### 2. 创建本机调试配置

`npm start`（`ng serve -c debug`）加载 `src/environments/environment.debug.ts`。这个文件**不在版本库里**——每个人的后端地址和联调模式都不一样，跟踪它只会让大家互相覆盖。首次克隆后从 `environment.dev.ts` 复制一份：

```bash
cp src/environments/environment.dev.ts src/environments/environment.debug.ts
```

之后随便改，改动不会被提交。**没有这个文件 `npm start` 会直接失败**，这一步不能跳过。

### 3. 填写 api.gateway 并选择联调模式

**关键是 `api.gateway` 怎么填，取决于你用哪种前后端联调模式**：

#### 模式一：SPA 同源访问（推荐）

后端开启内置 SPA 代理（`SpaProxy`），浏览器**只访问后端地址**，前端请求由后端代理转发到 Angular dev server。前后端同源，**没有跨域、cookie 正常**。

```typescript
export const environment = {
  ...environmentBase,
  useMock: {
    enable: false,
    delay: 500,
    exclude: '',
    include: '',
  },
  api: {
    ...environmentBase.api,
    gateway: '', // 留空 = 同源相对路径，请求经后端 SPA 代理
  },
};
```

> 配套：后端需开启 `SpaProxy.Enabled=true`、`SpaProxy.Target=http://localhost:4200`，详见 [后端 README · 前后端联调](../backend/README.md)。
> 访问方式：浏览器打开**后端地址**（如 `http://localhost:5240/`），不是 4200。

#### 模式二：CORS 分离访问

浏览器直接访问前端 dev server（4200），API 跨域打到后端。需后端开启 CORS 放行本地端口。

```typescript
export const environment = {
  ...environmentBase,
  useMock: { enable: false, delay: 500, exclude: '', include: '' },
  api: {
    ...environmentBase.api,
    gateway: 'http://localhost:5240', // 后端完整地址，跨域访问
  },
};
```

> 配套：后端需设 `Cors.AllowAnyLocalhost=true`（开发用）。访问方式：浏览器打开 `http://localhost:4200/`。
> 注意：跨域携带 cookie 对 SameSite/Secure 要求更严，若登录后 cookie 不生效，优先改用模式一。

### 4. 启动开发服务器

本项目已预置了多套环境配置，您可以根据需要启动对应的开发服务器。

- **启动本地调试环境**（默认使用 `environment.debug.ts` 配置）：

  ```bash
  npm start
  ```

- **启动其他环境**：
  ```bash
  ng serve -c dev     # 开发环境
  ng serve -c test    # 测试环境
  ng serve -c uat     # UAT 环境
  ng serve -c production  # 生产环境
  ```

服务器启动后：**模式一**（SPA 同源）在浏览器打开后端地址 `http://localhost:5240/`；**模式二**（CORS 分离）打开 `http://localhost:4200/`。应用支持热重载，任何对源文件的修改都会自动刷新页面。

---

## 环境配置

项目使用 Angular 的环境配置系统。配置文件位于 `src/environments/`：

- `environment.debug.ts` - 本地调试（`npm start` 默认使用）
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
    ...environmentBase.api, // authService、envService 等其余必填项沿用基础配置
    gateway: '', // 网关地址；留空表示各服务地址即完整地址
    appService: { url: 'http://localhost:5240' },
  },
};
```

> `api` 是必填对象，直接重写会漏掉其中的其他必填项——展开 `...environmentBase.api` 再覆盖要改的那几个。
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
