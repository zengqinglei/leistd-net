import {
  booleanAttribute,
  ChangeDetectionStrategy,
  Component,
  computed,
  input,
} from '@angular/core';
//#if (IncludeLocalization)
import { TranslocoDirective } from '@jsverse/transloco';
//#endif
import { HlmSkeleton } from '@spartan-ng/helm/skeleton';
//#if (!IncludeLocalization)

import { englishText } from '../../utils/english-text';
//#endif

/** 列表区域首次加载的行骨架：占住最终行的位置与高度（可带勾选框，标题加次要文字两行）。 */
@Component({
  selector: 'app-list-loading',
  standalone: true,
  // prettier-ignore
  imports: [
    HlmSkeleton,
    //#if (IncludeLocalization)
    TranslocoDirective,
    //#endif
  ],
  templateUrl: './list-loading.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'block' },
})
export class ListLoading {
  readonly rows = input(3);
  readonly checkbox = input(false, { transform: booleanAttribute });
  protected readonly rowIndexes = computed(() => Array.from({ length: this.rows() }, (_, i) => i));
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
