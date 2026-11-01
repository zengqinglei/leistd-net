import {
  ChangeDetectionStrategy,
  Component,
  computed,
  Directive,
  inject,
  input,
  TemplateRef,
  viewChild,
} from '@angular/core';
//#if (IncludeLocalization)
import { TranslocoDirective } from '@jsverse/transloco';
//#endif
import { HlmSkeleton } from '@spartan-ng/helm/skeleton';
import { HlmTableImports } from '@spartan-ng/helm/table';
//#if (!IncludeLocalization)

import { englishText } from '../../utils/english-text';
//#endif

/** 保留数据行的加载容器；骨架模板由调用方插入原 tbody，保持合法表格结构。 */
@Component({
  selector: 'app-table-loading',
  exportAs: 'appTableLoading',
  // prettier-ignore
  imports: [
    HlmSkeleton,
    ...HlmTableImports,
    //#if (IncludeLocalization)
    TranslocoDirective,
    //#endif
  ],
  host: { class: 'block', '[attr.aria-busy]': 'loading()' },
  templateUrl: './table-loading.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TableLoading {
  readonly loading = input(false);
  readonly hasRows = input(false);
  readonly columns = input.required<number>();
  /** 与各表真实行等高；五行足以表达列表形态，不占满整个视口。 */
  readonly rowHeight = input.required<number>();
  /** 窄屏可折行的表格可指定两行正文高度；未指定时沿用常规行高。 */
  readonly compactRowHeight = input<number>();
  readonly initialLoading = computed(() => this.loading() && !this.hasRows());
  readonly reloading = computed(() => this.loading() && this.hasRows());
  readonly skeleton = viewChild<TemplateRef<unknown>>('skeleton');
  protected readonly skeletonRows = [0, 1, 2, 3, 4];
  protected readonly skeletonColumns = computed(() => Array.from({ length: this.columns() }));
  //#if (!IncludeLocalization)
  protected readonly t = englishText(ENGLISH);
  //#endif
}

/** inert 同时阻止键盘与指针；读屏状态在容器内、行区外，不随行区一起隐藏。 */
@Directive({
  selector: 'tbody[appTableLoadingBody]',
  host: {
    '[attr.inert]': 'state.reloading() ? "" : null',
    '[class.opacity-50]': 'state.reloading()',
  },
})
export class TableLoadingBody {
  protected readonly state = inject(TableLoading);
}
//#if (!IncludeLocalization)

/** 不含本地化时的界面文案，与 `en.json` 同步。 */
const ENGLISH: Record<string, string> = {
  'app.startup.loading': 'Loading...',
};
//#endif
