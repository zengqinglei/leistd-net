import {
  computed,
  Injector,
  provideZonelessChangeDetection,
  runInInjectionContext,
} from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Translation, TranslocoLoader, TranslocoService } from '@jsverse/transloco';
import { Subject } from 'rxjs';

import { translationReady } from './translation-ready';
//#if (IncludeLocalization)
import { provideTranslocoTesting } from './transloco.testing';
//#endif

/**
 * 可控加载器：getTranslation() 返回一个待外部 resolve 的 Subject，模拟「首个 JSON 资源延迟到达」。
 * 用于验证 translationReady 在**异步加载完成**后确实触发下游重算，而非因初值等值被 Object.is 去重吞掉。
 */
class ControllableLoader implements TranslocoLoader {
  readonly gates = new Map<string, Subject<Translation>>();

  getTranslation(lang: string) {
    const gate = new Subject<Translation>();
    this.gates.set(lang, gate);
    return gate.asObservable();
  }

  resolve(lang: string, translation: Translation) {
    const gate = this.gates.get(lang);
    gate!.next(translation);
    gate!.complete();
  }
}

describe('translationReady', () => {
  let loader: ControllableLoader;
  let transloco: TranslocoService;
  let injector: Injector;

  beforeEach(() => {
    loader = new ControllableLoader();
    // Angular v21 默认 zoneless；TestBed 显式启用以与应用一致（ComponentFixture/信号在 zoneless 下走 appRef.tick）。
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        ...provideTranslocoTesting(['en', 'zh-CN'], loader),
      ],
    });
    transloco = TestBed.inject(TranslocoService);
    injector = TestBed.inject(Injector);
  });

  it('首个 JSON 资源到达后，依赖它的 computed 重新求值（不再残留裸键）', () => {
    transloco.setActiveLang('en');

    const label = runInInjectionContext(injector, () => {
      const ready = translationReady(transloco);
      // 未加载时 translate 返回键本身（裸键）；computed 依赖 ready，加载完成后应重算得到真实文案。
      return computed(() => (ready(), transloco.translate('greeting')));
    });

    // 资源尚未到达：裸键。
    expect(label()).toBe('greeting');

    // 首个 JSON 资源到达（初始语言仍是 en——正是 Object.is 去重会吞掉的场景）。
    loader.resolve('en', { greeting: 'Hello' });

    // 关键断言：computed 已随资源就绪重算，裸键被真实文案取代。
    expect(label()).toBe('Hello');
  });
});
