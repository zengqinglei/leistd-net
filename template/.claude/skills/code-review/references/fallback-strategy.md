# 降级策略 (Fallback Strategy)

> 当项目缺少代码规范配置时的降级行为

---

## 🎯 核心原则

### 1. 优雅降级

- ✅ **有配置**：按项目配置检查
- ✅ **无配置**：使用通用检查工具和默认规则
- ❌ **不报错停止**：继续执行，但告知用户

### 2. 提示而非阻塞

- ✅ **提示一次**：首次检测到缺失时提示
- ✅ **继续执行**：使用默认规则完成检查
- ✅ **提供指引**：告诉用户如何创建规范配置

---

## 📋 降级场景

### 场景 1：无 ESLint 配置（前端）

**检测**：
```markdown
尝试读取：
1. .eslintrc.js
2. .eslintrc.json
3. eslint.config.js

结果：都不存在
```

**降级行为**：
```markdown
1. 使用 ESLint 默认规则
2. 提示用户：
   "⚠️ 检测到项目未配置 ESLint，已使用默认规则检查。
   建议创建 .eslintrc.js 以声明项目特定的代码规范。
   参考模板：templates/eslintrc-template.js（本 Skill 提供）"
3. 继续执行，使用默认规则检查
```

---

### 场景 2：无 .editorconfig（通用）

**降级行为**：
```markdown
1. 使用默认编辑器配置
2. 提示用户：
   "⚠️ 检测到项目未配置 .editorconfig，已使用默认配置。
   建议创建 .editorconfig 声明项目的编码风格。
   参考模板：templates/editorconfig-template（本 Skill 提供）"
```

---

### 场景 3：无后端规范检查（.NET）

**降级行为**：
```markdown
1. 使用 dotnet format 默认规则
2. 提示用户：
   "⚠️ 检测到项目未配置 .editorconfig，已使用 .NET 默认规范。
   建议创建 .editorconfig 声明项目的 C# 代码规范。
   参考模板：templates/editorconfig-template（本 Skill 提供）"
```

---

## 📊 降级提示模板

### 前端规范配置缺失

```markdown
⚠️ 检测到项目未配置 ESLint，已使用默认规则检查。

建议创建 .eslintrc.js 以声明项目特定的：
- 解析器选项
- 环境变量
- 规则配置
- 插件扩展

参考模板：templates/eslintrc-template.js（本 Skill 提供）
```

### 通用规范配置缺失

```markdown
⚠️ 检测到项目未配置 .editorconfig，已使用默认配置。

建议创建 .editorconfig 声明项目的：
- 缩进风格（空格/Tab）
- 缩进大小
- 字符编码
- 换行符风格

参考模板：templates/editorconfig-template（本 Skill 提供）
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

- ✅ **使用默认规则**：确保检查能继续
- ✅ **记录日志**：在检查报告中记录降级行为
- ✅ **可追溯**：用户后续可以补充配置文件

---

*最后更新：2026-06-07*
*维护：code-review Skill*


