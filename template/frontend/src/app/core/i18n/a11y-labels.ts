import { inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { TranslocoService } from '@jsverse/transloco';
import { provideHlmA11yLabels } from '@spartan-ng/helm/utils';

/**
 * 把 `libs/ui` 组件内部的无障碍文案接上译文。
 *
 * 那些文案（对话框关闭按钮、侧栏折叠开关的读屏名）渲染在组件自己的模板里，页面无处传入；
 * 不接的话整页中文里只有这几处被读成英文，而且每新增一个对话框就多一处漏网。
 *
 * **单独成文件不是为了复用，是为了能被测到**：曾经这段直接写在 `app.config.ts` 里，
 * 而 `libs/ui` 一次上游升级把两个组件对令牌的读取改回了写死英文——接线还在，
 * 文案却已经不走它了，没有任何用例发现。守卫见 `a11y-labels.spec.ts`。
 */
export function provideAppA11yLabels() {
  return provideHlmA11yLabels(() => {
    const transloco = inject(TranslocoService);
    return {
      // toSignal 包住 selectTranslate 让它随语言切换重算——静态字符串会固定在启动时的语言上
      close: toSignal(transloco.selectTranslate<string>('common.close'), {
        initialValue: 'Close',
      }),
      toggleSidebar: toSignal(transloco.selectTranslate<string>('layout.sidebar.toggle'), {
        initialValue: 'Toggle Sidebar',
      }),
    };
  });
}
