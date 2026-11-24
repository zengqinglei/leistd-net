import { ChangeDetectionStrategy, Component, input } from '@angular/core';
//#if (IncludeLocalization)
import { TranslocoDirective } from '@jsverse/transloco';
//#endif
import { HlmSkeleton } from '@spartan-ng/helm/skeleton';
//#if (!IncludeLocalization)

import { englishText } from '../../utils/english-text';
//#endif

@Component({
  selector: 'app-dialog-loading',
  standalone: true,
  // prettier-ignore
  imports: [
    HlmSkeleton,
    //#if (IncludeLocalization)
    TranslocoDirective,
    //#endif
  ],
  templateUrl: './dialog-loading.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  // 宿主占满弹窗内容区或正文的剩余高度，骨架超出部分裁掉：固定高度的弹窗加载中不出滚动条
  host: { class: 'flex min-h-0 flex-1 flex-col overflow-hidden' },
})
export class DialogLoading {
  // 调用方可显式传入文本；未传入时回退到默认加载文案。
  readonly text = input<string>();
  //#if (!IncludeLocalization)
  protected readonly t = englishText(ENGLISH);
  //#endif
}
//#if (!IncludeLocalization)

/** 不含本地化时的界面文案，与 `en.json` 同步。 */
const ENGLISH: Record<string, string> = {
  'app.startup.loading': 'Loading...',
};
//#endif
