# 嵌套页面包屑上级

状态：已完成 · 风险档：中 · 基线：develop `b21cf8b1` · 关联：[#256](https://github.com/zengqinglei/leistd-net/pull/256)

## 1. 需求

模板面包屑固定为「首页 > 当前页」。规范认可以路由参数为来源的详情页（`frontend-lists.md`），生成项目一加列表下的详情或工作台子页就丢了父级，只能在页内补「返回…」链接，返回目标又容易与进入路径不符。下游项目已按「页面设上级、页头渲染」的方式解决，本次纳入模板，并补上多级折叠与顶栏布局。

非目标：不从路由树推导面包屑（模板路由是扁平的，列表与详情是同级路由，推导不出逻辑上下级）；模板自身不加嵌套示例页。

验收：
- Given 一级页面；Then 面包屑为「首页 > 当前页」，窄屏只有当前页。
- Given 页面设了一级上级；Then 「首页 > 上级 > 当前页」，上级为链接。
- Given 两级及以上上级；Then 「首页 > … > 最近上级 > 当前页」，省略号下拉列出更远的上级链接（离首页近的在前）。
- Given 窄屏且有上级；Then 只显示「最近上级 > 当前页」。
- Given 顶栏布局；When 进入设了上级的页面；Then 正文上方（页头外）出现面包屑；回到一级页面时消失。
- Given 离开带页头的布局；Then 标题与上级都被清空。

## 2. 方案

- `LayoutService.trail = signal<readonly LayoutCrumb[]>`，`LayoutCrumb` 为文案 + 绝对链接。设置的页面用 `DestroyRef` 清空，页头销毁时也清空。路由先销毁旧页再创建新页，页面的 effect 在其后执行，不存在清空覆盖新值的顺序问题。
- 面包屑抽成 `layout/components/page-breadcrumb`：侧栏布局放在页头（替换原内联写法），顶栏布局在 trail 非空时放在页头与正文之间、与正文同宽，不进滚动区，页面 `h-full` 的高度链不受影响。
- 折叠按 Spartan 官方 breadcrumb 示例：`hlmDropdownMenuTrigger` 按钮包 `hlm-breadcrumb-ellipsis`，下拉项用链接。阈值同 shadcn 响应式示例（显示首级 + 最近两级）。列表改单行（`flex-nowrap`），末级截断，避免页头定高下折行被裁。
- 导航地标名改走词条 `layout.breadcrumb.label`，省略号读屏文案 `layout.breadcrumb.more`。
- 规范 `frontend-navigation.md` §2 新增「嵌套页的层级由面包屑承担」条，页面内不另放返回链接。

## 3. 计划

1. 服务、组件、两种布局接入、词条。
2. 单测：`page-breadcrumb.spec.ts`（真实视口下的可见性、折叠下拉）；`default-header.spec.ts`（销毁清空上级、顶栏嵌套页）。
3. 规范与读取成本闸门。
4. 模板工作区同步代表场景，跑前端 lint、单测与构建；关本地化场景另跑一次。
