import { ChangeDetectorRef, Signal, effect, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { TranslocoService, Translation } from '@jsverse/transloco';

/**
 * 返回一个「翻译就绪」跟踪信号，用于让 computed/effect 在**资源异步加载完成**与**语言切换**时都重新求值。
 *
 * 为什么不用 langChanges$：它只在语言**切换**时发射。首帧若词条 JSON 尚未加载完，
 * translate(key) 会返回键本身（裸键），而此后语言未变、langChanges$ 不再发射，
 * 依赖它的 computed/effect 不会重算 → 裸键残留（Angular zoneless 下不会自动补偿）。
 *
 * 为什么承载 Translation 对象而非活动语言字符串：selectTranslation() 会触发 load() 并在**加载完成后**
 * 发射当前语言的词条对象、且在每次语言切换后再次发射。若把信号值映射为语言字符串，
 * 首帧初值 'en' 与加载完成后再次得到的 'en' 在 toSignal 默认的 Object.is 比较下相等 → 不通知下游，
 * 裸键仍会被缓存。因此信号承载词条对象（每次加载/切换是不同引用）并以 undefined 为初值，
 * 保证「首个 JSON 到达」这一次也会触发下游重算。读取处仅用于建立响应式依赖，不消费其值。
 *
 * @example
 * private readonly translationReady = translationReady(this.transloco);
 * readonly label = computed(() => { this.translationReady(); return this.transloco.translate(SOME_KEY); });
 */
export function translationReady(transloco: TranslocoService): Signal<Translation | undefined> {
  return toSignal(transloco.selectTranslation(), { initialValue: undefined });
}

/**
 * 让本组件视图在资源就绪 / 语言切换时重绘，供模板里直接调用 `transloco.translate()` 的组件使用。
 *
 * OnPush 组件只有在「被渲染的表达式」依赖了语言时才会被标脏。靠 `| transloco` 管道或某个读了
 * {@link translationReady} 的 computed 恰好出现在模板里，是隐式契约：角色列表的管道全写在空状态块中，
 * 有数据时一个都不渲染；角色页只有搜索框、没有筛选下拉，于是整页停在旧语言。
 * 这里把语言变化显式接到 markForCheck 上，与模板怎么写无关。
 *
 * 与 {@link translationReady} 是互补关系而非替代：computed 会缓存，仍需在其中读一次
 * translationReady 才会失效重算；本函数解决的是视图不重绘。
 *
 * 必须在注入上下文中调用（字段初始化或构造函数）。
 *
 * @example
 * export class RoleTable {
 *   private readonly transloco = inject(TranslocoService);
 *   constructor() { refreshOnLanguageChange(this.transloco); }
 * }
 */
export function refreshOnLanguageChange(transloco: TranslocoService): void {
  const ready = translationReady(transloco);
  const changeDetectorRef = inject(ChangeDetectorRef);

  effect(() => {
    ready();
    changeDetectorRef.markForCheck();
  });
}
