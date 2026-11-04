import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import {
  NavigationCancel,
  NavigationCancellationCode,
  NavigationEnd,
  NavigationError,
  NavigationSkipped,
  NavigationStart,
  Router,
} from '@angular/router';

/** 路由与项目过渡共用的等待提示；每份占用独立释放，短操作统一延迟显示以免闪烁。 */
@Injectable({ providedIn: 'root' })
export class NavigationProgressService {
  private pending = 0;
  private timer: ReturnType<typeof setTimeout> | undefined;
  private readonly _visible = signal(false);
  readonly visible = this._visible.asReadonly();
  private readonly contentHosts = signal(0);
  /** 当前布局已在内容区顶部挂载进度条；没有布局的页面由根组件在视口顶部兜底。 */
  readonly hasContentHost = computed(() => this.contentHosts() > 0);
  private releaseRoute: (() => void) | undefined;

  constructor() {
    inject(Router)
      .events.pipe(takeUntilDestroyed())
      .subscribe((event) => {
        if (event instanceof NavigationStart) {
          this.releaseRoute ??= this.begin();
        } else if (
          event instanceof NavigationEnd ||
          event instanceof NavigationError ||
          event instanceof NavigationSkipped ||
          // 被取代或重定向的导航先发取消、紧接着才发新导航的开始；在这里释放会让进度条断开并重新计时。
          (event instanceof NavigationCancel &&
            event.code !== NavigationCancellationCode.SupersededByNewNavigation &&
            event.code !== NavigationCancellationCode.Redirect)
        ) {
          this.releaseRoute?.();
          this.releaseRoute = undefined;
        }
      });
    inject(DestroyRef).onDestroy(() => {
      clearTimeout(this.timer);
      this._visible.set(false);
    });
  }

  /** 开始一份过渡；调用方在成功、失败或取消的收尾中调用返回的释放函数。 */
  begin(): () => void {
    this.pending++;
    if (this.pending === 1) {
      this.timer = setTimeout(() => {
        this.timer = undefined;
        this._visible.set(true);
      }, 300);
    }

    let released = false;
    return () => {
      if (released) return;
      released = true;
      if (--this.pending === 0) {
        clearTimeout(this.timer);
        this.timer = undefined;
        this._visible.set(false);
      }
    };
  }

  /** 登记一处内容区宿主，返回注销函数。 */
  attachContentHost(): () => void {
    this.contentHosts.update((count) => count + 1);
    return () => this.contentHosts.update((count) => count - 1);
  }
}
