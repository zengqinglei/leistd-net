import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TranslocoLoader, TranslocoService } from '@jsverse/transloco';
import { BrnDialogRef } from '@spartan-ng/brain/dialog';
import { HlmDialogContent } from '@spartan-ng/helm/dialog';
import { HlmSidebarTrigger } from '@spartan-ng/helm/sidebar';
import { firstValueFrom, of } from 'rxjs';

import { provideAppA11yLabels } from './a11y-labels';
import { provideTranslocoTesting } from './transloco.testing';

/**
 * `libs/ui` 组件内部的读屏文案必须跟着语言走。
 *
 * 回归点：接线（`provideAppA11yLabels`）与消费（组件模板读令牌）是两处，
 * 一次 spartan 升级只改掉了后者——两个组件把读屏名改回写死英文，接线还在、却没人读它。
 * 界面上看不出来（`sr-only` 不可见），所以判据只能是"渲染出的读屏名不是英文默认值"。
 */
const translations: Record<string, Record<string, string>> = {
  en: { 'common.close': 'Close', 'layout.sidebar.toggle': 'Toggle Sidebar' },
  'zh-CN': { 'common.close': '关闭', 'layout.sidebar.toggle': '折叠/展开侧边栏' },
};

const loader: TranslocoLoader = { getTranslation: (lang: string) => of(translations[lang] ?? {}) };

@Component({
  imports: [HlmSidebarTrigger],
  // aria-label 与真实用法一致（见 default-header.html）：按钮的可见内容由指令模板渲染，
  // 模板检查看不见，宿主必须自己给一个可访问名
  template: `<button hlmSidebarTrigger aria-label="Toggle sidebar"></button>`,
})
class SidebarTriggerHost {}

@Component({
  imports: [HlmDialogContent],
  template: `<hlm-dialog-content />`,
})
class DialogContentHost {}

describe('libs/ui 的读屏文案', () => {
  function configure(): void {
    TestBed.configureTestingModule({
      providers: [
        ...provideTranslocoTesting(['en', 'zh-CN'], loader),
        provideAppA11yLabels(),
        // HlmDialogContent 只用 state()，不需要真实的对话框栈
        { provide: BrnDialogRef, useValue: { state: () => 'open' } },
      ],
    });
  }

  function screenReaderTextOf(host: typeof SidebarTriggerHost | typeof DialogContentHost): string {
    const fixture = TestBed.createComponent(host);
    fixture.detectChanges();
    const label = fixture.nativeElement.querySelector('.sr-only') as HTMLElement | null;
    expect(label).withContext('没有渲染出 sr-only 读屏文案').not.toBeNull();
    return label!.textContent!.trim();
  }

  it('侧栏折叠开关读出当前语言的名字，而不是写死的英文', async () => {
    configure();
    const transloco = TestBed.inject(TranslocoService);
    // 先把词条装好再切语言：令牌工厂在组件创建时才跑，那时 selectTranslate 必须能立刻给出值，
    // 否则信号停在 initialValue（英文），用例测到的就不是接线而是回落
    await firstValueFrom(transloco.load('zh-CN'));
    transloco.setActiveLang('zh-CN');

    const text = screenReaderTextOf(SidebarTriggerHost);

    expect(text).toBe('折叠/展开侧边栏');
    expect(text).not.toBe('Toggle Sidebar');
  });

  it('对话框关闭按钮读出当前语言的名字，而不是写死的英文', async () => {
    configure();
    const transloco = TestBed.inject(TranslocoService);
    // 先把词条装好再切语言：令牌工厂在组件创建时才跑，那时 selectTranslate 必须能立刻给出值，
    // 否则信号停在 initialValue（英文），用例测到的就不是接线而是回落
    await firstValueFrom(transloco.load('zh-CN'));
    transloco.setActiveLang('zh-CN');

    const text = screenReaderTextOf(DialogContentHost);

    expect(text).toBe('关闭');
    expect(text).not.toBe('Close');
  });
});
