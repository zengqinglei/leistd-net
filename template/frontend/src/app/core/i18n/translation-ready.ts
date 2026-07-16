import { Signal } from '@angular/core';
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
 * 裸键仍被缓存。改为承载词条对象（每次加载/切换是不同引用）并以 undefined 为初值，
 * 保证「首个 JSON 到达」这一次也会触发下游重算。读取处仅用于建立响应式依赖，不消费其值。
 *
 * @example
 * private readonly translationReady = translationReady(this.transloco);
 * readonly label = computed(() => { this.translationReady(); return this.transloco.translate(SOME_KEY); });
 */
export function translationReady(transloco: TranslocoService): Signal<Translation | undefined> {
  return toSignal(transloco.selectTranslation(), { initialValue: undefined });
}
