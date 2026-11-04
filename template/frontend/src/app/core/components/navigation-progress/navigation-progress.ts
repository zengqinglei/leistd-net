import { ChangeDetectionStrategy, Component, computed, effect, inject, input } from '@angular/core';
//#if (IncludeLocalization)
import { TranslocoDirective } from '@jsverse/transloco';
//#endif
import { HlmProgressImports } from '@spartan-ng/helm/progress';

//#if (!IncludeLocalization)
import { englishText } from '../../../shared/utils/english-text';
//#endif
import { NavigationProgressService } from '../../services/navigation-progress-service';

/**
 * 布局把它放在顶栏与内容之间（`placement="content"`），等待提示出现在内容区顶部；
 * 根组件的实例只在当前没有布局（登录、404 等）时显示在视口顶部。页面自己的首屏骨架仍由页面负责。
 */
@Component({
  selector: 'app-navigation-progress',
  // prettier-ignore
  imports: [
    ...HlmProgressImports,
    //#if (IncludeLocalization)
    TranslocoDirective,
    //#endif
  ],
  templateUrl: './navigation-progress.html',
  // 宿主零高度：内容区里叠在内容顶部，不推移布局；视口兜底固定在顶部，层级高于顶栏。
  host: {
    '[class]':
      "placement() === 'content' ? 'relative z-10 block' : 'fixed inset-x-0 top-0 z-50 block'",
  },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class NavigationProgress {
  readonly placement = input<'viewport' | 'content'>('viewport');
  private readonly progress = inject(NavigationProgressService);
  protected readonly shown = computed(
    () =>
      this.progress.visible() &&
      (this.placement() === 'content' || !this.progress.hasContentHost()),
  );
  //#if (!IncludeLocalization)
  protected readonly t = englishText(ENGLISH);
  //#endif

  constructor() {
    effect((onCleanup) => {
      if (this.placement() === 'content') onCleanup(this.progress.attachContentHost());
    });
  }
}
//#if (!IncludeLocalization)

const ENGLISH = {
  'app.navigation.loading': 'Loading...',
} as const;
//#endif
