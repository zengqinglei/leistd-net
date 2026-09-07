//#if (!LocalIdentity)
import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { Router } from '@angular/router';

import { AuthService } from '../../services/auth-service';

/**
 * OIDC 回调落地页：跳到登录前记下的地址。
 *
 * 导航必须在这里而不是 `AuthService.initializeAuth()` 里做。那时启动流还停在 loading，
 * 而 `permissionGuard` 要等它离开 loading 才放行——导航到 `/platform` 会形成
 * 启动 → 确立主体 → 导航 → Guard 等启动 的环；即使目标是 `/workspace` 不卡住，
 * 也是在权限与设置都没就位时渲染，菜单和按钮会先按无权限闪一下。
 *
 * 本组件由 `router-outlet` 渲染，而外壳只在启动流成功后才渲染它（见 `app.ts`），
 * 所以走到构造函数时会话上下文已经就绪，直接导航即可。
 */
@Component({
  selector: 'app-oidc-callback',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: '<p class="p-6 text-sm text-muted-foreground">Completing sign in...</p>',
})
export class OidcCallback {
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);

  constructor() {
    void this.router.navigateByUrl(this.authService.takeReturnUrl());
  }
}
//#endif
