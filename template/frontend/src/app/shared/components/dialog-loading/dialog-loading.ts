// prettier-ignore
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  //#if (IncludeLocalization)
  inject,
  //#endif
  input,
} from '@angular/core';
//#if (IncludeLocalization)
import { TranslocoService } from '@jsverse/transloco';
//#endif
import { HlmSpinner } from '@spartan-ng/helm/spinner';
//#if (IncludeLocalization)

import { translationReady } from '../../../core/i18n/translation-ready';
//#endif

@Component({
  selector: 'app-dialog-loading',
  standalone: true,
  imports: [HlmSpinner],
  template: `
    <div class="flex min-h-64 flex-col items-center justify-center gap-4 py-10 text-center">
      <hlm-spinner class="text-3xl" aria-label="loading" />
      <div class="text-sm text-muted-foreground">{{ displayText() }}</div>
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DialogLoading {
  // 调用方可显式传入文本；未传入时回退到默认加载文案。
  readonly text = input<string>();

  //#if (IncludeLocalization)
  private readonly transloco = inject(TranslocoService);
  // 追踪「翻译就绪」：资源加载完成与语言切换时该 signal 变化 → displayText 重算 → 默认文案重新翻译（含首帧，避免裸键）。
  private readonly translationReady = translationReady(this.transloco);

  // 未传入 text 时读 translationReady 建立依赖，资源就绪 / 语言切换后默认文案随之更新。
  readonly displayText = computed(() => {
    const override = this.text();
    if (override !== undefined) {
      return override;
    }

    this.translationReady();
    return this.transloco.translate('app.startup.loading');
  });
  //#else
  readonly displayText = computed(() => this.text() ?? 'Loading...');
  //#endif
}
