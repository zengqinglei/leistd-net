import { isPlatformBrowser } from '@angular/common';
import { DestroyRef, Injectable, PLATFORM_ID, inject, signal } from '@angular/core';

/**
 * 布局服务
 * 管理应用布局的状态，包括侧边栏的可见性和折叠状态
 * 支持响应式布局，在小屏幕设备上自动折叠侧边栏
 */
@Injectable({
  providedIn: 'root',
})
export class LayoutService {
  private readonly mobileBreakpoint = 768;
  private readonly autoCollapseBreakpoint = 1400;

  /**
   * 控制侧边栏的可见性
   * 主要用于移动端的显示/隐藏
   */
  sidebarVisible = signal<boolean>(true);

  /**
   * 当前是否处于移动端侧边栏模式
   * true: 侧边栏以抽屉/覆盖层形式展示
   * false: 侧边栏以内联形式展示
   */
  isMobileSidebarMode = signal<boolean>(false);

  /**
   * 控制侧边栏的折叠状态
   * true: 折叠（仅显示图标）
   * false: 展开（显示完整内容）
   *
   * 默认折叠：首屏仅显示图标，节省空间。
   */
  sidebarCollapsed = signal<boolean>(true);

  /**
   * 页面标题
   */
  // 初始占位值（各页在 ngOnInit/effect 里会立即覆盖为本页标题）
  title = signal<string>('Overview');

  private readonly platformId = inject(PLATFORM_ID);
  private readonly destroyRef = inject(DestroyRef);
  private resizeHandler?: () => void;
  private wasAboveAutoCollapseBreakpoint = true;
  constructor() {
    this.initResponsive();

    // 注册清理函数，在服务销毁时移除监听器
    this.destroyRef.onDestroy(() => {
      this.cleanup();
    });
  }

  /**
   * 初始化响应式布局
   * 在桌面宽度进入窄屏区间时自动折叠侧边栏，但放大时不自动展开。
   */
  private initResponsive(): void {
    if (isPlatformBrowser(this.platformId)) {
      this.applyResponsiveSidebarState(window.innerWidth, true);

      this.resizeHandler = () => {
        this.applyResponsiveSidebarState(window.innerWidth);
      };

      window.addEventListener('resize', this.resizeHandler);
    }
  }

  private applyResponsiveSidebarState(width: number, initialize = false): void {
    const isMobileSidebarMode = width < this.mobileBreakpoint;
    const isBelowOrEqualAutoCollapseBreakpoint = width <= this.autoCollapseBreakpoint;

    this.isMobileSidebarMode.set(isMobileSidebarMode);

    if (initialize) {
      if (isBelowOrEqualAutoCollapseBreakpoint || isMobileSidebarMode) {
        this.sidebarCollapsed.set(true);
      }

      this.wasAboveAutoCollapseBreakpoint = !isBelowOrEqualAutoCollapseBreakpoint;
      return;
    }

    if (
      isMobileSidebarMode ||
      (this.wasAboveAutoCollapseBreakpoint && isBelowOrEqualAutoCollapseBreakpoint)
    ) {
      this.sidebarCollapsed.set(true);
    }

    this.wasAboveAutoCollapseBreakpoint = !isBelowOrEqualAutoCollapseBreakpoint;
  }

  /**
   * 清理资源
   * 移除 MediaQuery 监听器，防止内存泄漏
   */
  private cleanup(): void {
    if (isPlatformBrowser(this.platformId) && this.resizeHandler) {
      window.removeEventListener('resize', this.resizeHandler);
    }
  }

  /**
   * 切换侧边栏折叠状态
   * 用于用户手动展开/折叠侧边栏
   */
  toggleSidebarCollapse(): void {
    this.sidebarCollapsed.update((collapsed) => !collapsed);
  }

  /**
   * 切换侧边栏可见性
   * 主要用于移动端的侧边栏显示/隐藏
   */
  toggleSidebar(): void {
    this.sidebarVisible.update((visible) => !visible);
  }
}
