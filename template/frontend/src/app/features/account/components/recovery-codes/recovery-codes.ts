//#if (IncludeLocalization)
import { ChangeDetectionStrategy, Component, inject, input, output } from '@angular/core';
import { TranslocoService } from '@jsverse/transloco';
//#else
import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
//#endif
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCheck, lucideCopy, lucideDownload, lucideTriangleAlert } from '@ng-icons/lucide';
import { HlmButton } from '@spartan-ng/helm/button';

//#if (IncludeLocalization)
import { translationReady } from '../../../../core/i18n/translation-ready';
//#endif
import { injectCopyToClipboard } from '../../../../shared/utils/clipboard';
import { saveBlob } from '../../../../shared/utils/download-file';

/**
 * 一次性展示新生成的恢复码，并提供复制与下载。
 *
 * 明文只在这里出现一次，服务端只存摘要；"我已保存"之前不收起，免得人还没抄下就没了。
 */
@Component({
  selector: 'app-recovery-codes',
  imports: [NgIcon, HlmButton],
  providers: [provideIcons({ lucideCheck, lucideCopy, lucideDownload, lucideTriangleAlert })],
  template: `
    <div class="flex flex-col gap-4" data-testid="recovery-codes">
      <div
        class="border-border bg-muted/50 flex items-start gap-2 rounded-lg border px-3 py-2 text-sm"
      >
        <ng-icon name="lucideTriangleAlert" class="text-warning mt-0.5 shrink-0" />
        <span>{{ t('account.twoFactor.recoveryCodesWarning') }}</span>
      </div>
      <ol class="grid grid-cols-1 gap-2 font-mono text-sm sm:grid-cols-2">
        @for (code of codes(); track code) {
          <li class="bg-muted rounded-lg px-3 py-1.5 text-center select-all">{{ code }}</li>
        }
      </ol>
      <div class="flex flex-wrap gap-2">
        <button hlmBtn variant="outline" size="sm" type="button" (click)="copy()">
          <ng-icon [name]="copied() ? 'lucideCheck' : 'lucideCopy'" data-icon="inline-start" />
          {{ copied() ? t('account.twoFactor.copied') : t('account.twoFactor.copyCodes') }}
        </button>
        <button hlmBtn variant="outline" size="sm" type="button" (click)="download()">
          <ng-icon name="lucideDownload" data-icon="inline-start" />
          {{ t('account.twoFactor.downloadCodes') }}
        </button>
        <button
          hlmBtn
          size="sm"
          type="button"
          class="sm:ml-auto"
          data-testid="recovery-codes-done"
          (click)="done.emit()"
        >
          {{ t('account.twoFactor.savedCodes') }}
        </button>
      </div>
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RecoveryCodes {
  private readonly clipboard = injectCopyToClipboard();
  //#if (IncludeLocalization)
  private readonly transloco = inject(TranslocoService);
  private readonly translationReady = translationReady(this.transloco);

  protected readonly t = (key: string) => {
    this.translationReady();
    return this.transloco.translate(key);
  };
  //#else
  protected readonly t = (key: string) => ENGLISH[key] ?? key;
  //#endif

  readonly codes = input.required<string[]>();
  /** 用户确认已经保存。 */
  readonly done = output<void>();

  protected readonly copied = this.clipboard.copied;

  // 剪贴板不可用时仍可手动选中或下载
  protected copy(): void {
    void this.clipboard.copy(this.codes().join('\n'));
  }

  protected download(): void {
    saveBlob(
      new Blob([`${this.codes().join('\n')}\n`], { type: 'text/plain' }),
      'recovery-codes.txt',
    );
  }
}
//#if (!IncludeLocalization)

/** 不含本地化时的界面文案，与 `en.json` 的 `account.twoFactor` 同步。 */
const ENGLISH: Record<string, string> = {
  'account.twoFactor.recoveryCodesWarning':
    'Save these recovery codes somewhere safe. Each one signs you in once if you lose your phone. They will not be shown again.',
  'account.twoFactor.copyCodes': 'Copy',
  'account.twoFactor.copied': 'Copied',
  'account.twoFactor.downloadCodes': 'Download',
  'account.twoFactor.savedCodes': "I've saved them",
};
//#endif
