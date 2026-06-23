# 降级策略 (Fallback Strategy)

> 当项目缺少规范文档时，Skill 的降级行为和提示机制

---

## 🎯 核心原则

### 1. 优雅降级

- ✅ **有规范**：按项目规范执行
- ✅ **无规范**：使用本 Skill 提供的技术栈模板
- ❌ **不报错停止**：继续执行，但告知用户

### 2. 提示而非阻塞

- ✅ **提示一次**：首次检测到缺失时提示
- ✅ **继续执行**：使用模板完成当前任务
- ✅ **提供指引**：告诉用户如何创建规范文档

---

## 📋 降级场景

### 场景 1：无后端开发规范

**检测**：
```markdown
尝试读取：
1. `docs/standards/code-standard/backend-develop.md`
2. `docs/standards/code-standard/` 目录

结果：都不存在
```

**降级行为**：
```markdown
1. 使用本 Skill 模板：`templates/code-standard-backend.md`
2. 提示用户：
   "⚠️ 检测到项目未配置后端开发规范，已使用通用 .NET 10 规范。
   建议创建 docs/standards/code-standard/backend-develop.md 以声明项目特定的后端规范。
   参考模板：templates/code-standard-backend.md（本 Skill 提供）"
3. 继续执行，使用默认技术栈（.NET 10 + EF Core 10）
```

---

### 场景 2：无前端开发规范

**降级行为**：
```markdown
1. 使用本 Skill 模板：`templates/code-standard-frontend.md`
2. 提示用户：
   "⚠️ 检测到项目未配置前端开发规范，已使用通用 Angular 21 规范。
   建议创建 docs/standards/code-standard/frontend-develop.md 以声明项目特定的前端规范。
   参考模板：templates/code-standard-frontend.md（本 Skill 提供）"
3. 继续执行，使用默认技术栈（Angular 21 + PrimeNG 21）
```

---

### 场景 3：无技术栈声明

**降级行为**：
```markdown
1. 使用本 Skill 综合模板：`templates/techstack-dotnet-angular.md`
2. 提示用户：
   "⚠️ 检测到项目未声明技术栈，已使用默认技术栈（.NET 10 + Angular 21）。
   如项目使用其他技术栈，请创建 docs/standards/agent-workflow.md 声明。
   参考模板：templates/techstack-dotnet-angular.md（本 Skill 提供）"
```

---

## 📊 降级提示模板

### 规范文档缺失

```markdown
⚠️ 检测到项目未配置{规范类型}规范文档，已使用通用默认规范。

建议创建 {文件路径} 以声明项目特定的：
- 技术栈版本
- 目录结构
- 命名规范
- 编码风格

参考模板：templates/{模板名称}（本 Skill 提供）
```

---

## ✅ 最佳实践

### 1. 首次提示时机

- ✅ **首次检测到缺失时**：立即提示
- ✅ **只提示一次**：避免重复打扰
- ✅ **提供解决方案**：告诉用户如何创建

### 2. 提示方式

- ✅ **友好语气**：建议而非命令
- ✅ **明确路径**：告诉用户具体文件路径
- ✅ **提供模板**：给出参考模板路径

### 3. 降级执行

- ✅ **使用模板**：确保任务能继续
- ✅ **记录日志**：在任务上下文中记录降级行为
- ✅ **可追溯**：用户后续可以补充规范文档

---

*最后更新：2026-06-07*
*维护：coding Skill*
