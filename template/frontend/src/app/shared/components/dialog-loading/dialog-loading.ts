//#if (IncludeLocalization)
import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { TranslocoService } from '@jsverse/transloco';
//#else
import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
//#endif
import { ProgressSpinnerModule } from 'primeng/progressspinner';

@Component({
  selector: 'app-dialog-loading',
  standalone: true,
  imports: [ProgressSpinnerModule],
  template: `
    <div class="flex min-h-64 flex-col items-center justify-center gap-4 py-10 text-center">
      <p-progressSpinner ariaLabel="loading" strokeWidth="4" styleClass="h-10 w-10" />
      <div class="text-sm text-muted-color">{{ displayText() }}</div>
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class DialogLoadingComponent {
  // 调用方可显式传入文本；未传入时回退到默认加载文案。
  readonly text = input<string>();

  //#if (IncludeLocalization)
  private readonly transloco = inject(TranslocoService);
  // 追踪活动语言：切换时该 signal 变化 → displayText 重算 → 默认文案重新翻译。
  private readonly activeLang = toSignal(this.transloco.langChanges$, { initialValue: this.transloco.getActiveLang() });

  // 未传入 text 时读 activeLang 建立依赖，语言切换后默认文案随之更新。
  readonly displayText = computed(() => {
    const override = this.text();
    if (override !== undefined) {
      return override;
    }

    this.activeLang();
    return this.transloco.translate('app.startup.loading');
  });
  //#else
  readonly displayText = computed(() => this.text() ?? 'Loading...');
  //#endif
}
