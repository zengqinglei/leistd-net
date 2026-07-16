import { Signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { TranslocoService } from '@jsverse/transloco';
import { map } from 'rxjs';

/**
 * 返回一个「翻译就绪」跟踪信号，用于让 computed/effect 在**资源异步加载完成**与**语言切换**时都重新求值。
 *
 * 为什么不用 langChanges$：它只在语言**切换**时发射。首帧若词条 JSON 尚未加载完，
 * translate(key) 会返回键本身（裸键），而此后语言未变、langChanges$ 不再发射，
 * 依赖它的 computed/effect 不会重算 → 裸键残留（Angular zoneless 下不会自动补偿）。
 *
 * selectTranslation() 会触发 load() 并在**加载完成后**发射、且在每次语言切换后再次发射，
 * 正是所缺的「资源就绪」依赖。返回当前活动语言字符串，读取处仅用于建立响应式依赖。
 *
 * @example
 * private readonly translationReady = translationReady(this.transloco);
 * readonly label = computed(() => { this.translationReady(); return this.transloco.translate(SOME_KEY); });
 */
export function translationReady(transloco: TranslocoService): Signal<string> {
  return toSignal(transloco.selectTranslation().pipe(map(() => transloco.getActiveLang())), { initialValue: transloco.getActiveLang() });
}
