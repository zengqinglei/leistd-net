# 前端开发规范模板

> Angular 21 + PrimeNG 21 + Tailwind CSS 4 前端项目开发规范

---

## 1. 核心技术栈

- **前端框架**: Angular v21+
- **UI 组件库**: PrimeNG v21+
- **原子化 CSS**: Tailwind CSS v4+
- **开发语言**: TypeScript 5.8+
- **状态管理**: Angular Signals

---

## 2. 目录结构

```
frontend/
├── _mock/                                    # Mock 服务（完全独立于源码）
│   ├── api/                                  # API Mock 处理器
│   │   └── *.ts
│   ├── data/                                 # 纯粹的模拟数据源
│   │   └── *.ts
│   └── index.ts                              # Mock 服务启动入口
├── src/
│   └── app/
│       ├── core/                             # 核心逻辑（非 UI，应用级单例服务）
│       │   ├── guards/                       # 路由守卫
│       │   ├── interceptors/                 # HTTP 拦截器
│       │   └── services/                     # 应用级核心服务
│       ├── features/                         # 业务功能模块（按业务领域划分）
│       │   └── {module-name}/
│       │       ├── components/               # 页面级"智能"组件
│       │       │   └── {page-name}/
│       │       │       ├── {page-name}.html
│       │       │       ├── {page-name}.css
│       │       │       └── {page-name}.ts
│       │       ├── widgets/                  # 特性内可复用的"哑"组件
│       │       ├── services/                 # 业务服务
│       │       └── models/                   # 数据模型
│       ├── shared/                           # 全局共享资源
│       │   ├── components/                   # 全局可复用组件
│       │   ├── directives/                   # 全局指令
│       │   └── pipes/                        # 全局管道
│       └── layout/                           # 应用布局
│           ├── components/                   # 布局共享组件
│           └── services/                     # 布局服务
└── environments/                             # 环境配置
```

---

## 3. 编码规范

### 3.1 依赖注入

**必须使用 `inject()` 函数**：
```typescript
// ✅ 正确
export class UserService {
  private http = inject(HttpClient);
  private userService = inject(UserService);
}

// ❌ 错误：构造函数注入
export class UserService {
  constructor(
    private http: HttpClient,
    private userService: UserService
  ) {}
}
```

### 3.2 命名约定

| 类型 | 命名规范 | 示例 |
|------|---------|------|
| **Component** | `{name}.ts` | `user-profile.ts` |
| **Service** | `{name}-service.ts` | `user-service.ts` |
| **Directive** | `{name}.ts` | `highlight.ts` |
| **Pipe** | `{name}-pipe.ts` | `format-date-pipe.ts` |
| **Guard** | `{name}-guard.ts` | `auth-guard.ts` |

### 3.3 状态管理

**优先使用 Angular Signals**：
```typescript
export class UserComponent {
  // 状态
  private users = signal<User[]>([]);
  private loading = signal(false);

  // 计算状态
  readonly userCount = computed(() => this.users().length);

  // 更新状态
  loadUsers() {
    this.loading.set(true);
    this.userService.getList().subscribe(users => {
      this.users.set(users);
      this.loading.set(false);
    });
  }
}
```

---

## 4. 组件与样式规范

### 4.1 组件库优先

- ✅ **必须**首先在 PrimeNG 官方文档中寻找现成组件
- ✅ 尽量使用 PrimeNG v21 组件以及默认风格
- ✅ 仅在无法满足需求时才可创建自定义组件

### 4.2 样式方案

- ✅ **必须**优先使用 Tailwind CSS v4 的原子类进行布局和微调
- ✅ 自定义样式使用 Tailwind CSS v4
- ✅ 任何自定义样式都必须与 PrimeNG 的主题风格保持一致

### 4.3 组件设计

- **单一职责 (SRP)**: 每个组件应只关注一个功能点
- **单向数据流**: 遵循 `[input]` 向下，`(output)` 向上
- **变更检测**: 展示型组件使用 `ChangeDetectionStrategy.OnPush`

**示例**：
```typescript
@Component({
  selector: 'app-user-list',
  templateUrl: './user-list.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UserList {
  @Input() users: User[] = [];
  @Output() delete = new EventEmitter<number>();
}
```

---

## 5. 开发原则

### 5.1 Mock 开发

- ✅ 在开发阶段，**应**为所有后端 API 提供 Mock 实现
- ✅ Mock 相关代码**必须**与业务源码分离，并存放在 `_mock` 目录下
- ✅ Mock API 结构必须与真实后端接口一致

**Mock API 示例**：
```typescript
// frontend/_mock/api/user-api.ts
export function setupUserApi(mock: MockServer) {
  mock.onGet('/api/users').reply((config) => {
    const users = getMockUsers();
    return [200, paginate(users, config.params)];
  });

  mock.onPost('/api/users').reply((config) => {
    const user = JSON.parse(config.data);
    return [201, { ...user, id: generateId() }];
  });
}
```

### 5.2 测试

- ✅ `service`、`pipe` 和包含复杂业务逻辑的函数**必须**有单元测试覆盖
- ✅ 核心的共享组件和业务流程**应**编写组件测试

---

## 6. 共享资源

### 6.1 公共组件位置

前端公共组件在目录 `src/app/shared` 中：

- **分页相关 DTO**: 分页请求和响应的数据模型
- **Logo 组件**: 应用 Logo 组件
- **平台图标**: 各平台的图标组件
- **主题配置**: 主题相关的配置和服务
- **公共常量**: 全局共享的常量定义

### 6.2 使用原则

- ✅ 只有被多个不相关特性使用的组件才应放入 `shared`
- ✅ 特性内部复用的组件应放在特性的 `widgets` 目录下
- ✅ 保持 `shared` 目录的纯粹性和通用性

---

## 7. 规范检查清单

### ✅ 编码规范检查

- [ ] 使用 `inject()` 进行依赖注入
- [ ] 文件命名符合规范
- [ ] 所有 API 数据使用类型定义
- [ ] 遵循 Angular v21 最佳风格指南

### ✅ 组件规范检查

- [ ] 优先使用 PrimeNG v21 组件
- [ ] 自定义样式使用 Tailwind CSS v4
- [ ] 组件遵循单一职责原则
- [ ] 展示型组件使用 OnPush 策略

### ✅ Mock 规范检查

- [ ] Mock 代码与源码分离（`_mock` 目录）
- [ ] Mock API 结构与真实接口一致
- [ ] Mock 数据独立存放

---

*模板版本：v1.0.0*
*最后更新：2026-06-07*
*维护：coding Skill*

