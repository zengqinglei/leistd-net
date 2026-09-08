import { HttpInterceptorFn, HttpErrorResponse } from '@angular/common/http';
import { inject } from '@angular/core';
//#if (LocalIdentity)
import { Router } from '@angular/router';
//#endif
import { catchError, throwError } from 'rxjs';

import { SILENT_AUTH } from './http-context-tokens';
import { ApplicationHttpError } from '../errors/application-http-error';
import { entryRouteUrl, isOnAuthRoute } from '../routing/entry-route';
import { AuthService } from '../services/auth-service';
import { SessionContextService } from '../services/session-context-service';
import { TenantContextService } from '../services/tenant-context-service';

export const httpErrorInterceptor: HttpInterceptorFn = (req, next) => {
  const authService = inject(AuthService);
  const sessionContext = inject(SessionContextService);
  const tenantContext = inject(TenantContextService);
  //#if (LocalIdentity)
  const router = inject(Router);
  //#endif
  return next(req).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse)) {
        return throwError(() => error);
      }

      if (error.status === 401) {
        // 这个头说的是"会话所属租户已经没了"，与请求是否静默、当前在哪条路由上、
        // 要不要重新认证全都无关——它是一条独立的事实，清掉本地那个已失效的租户选择
        // 是唯一正确的反应。尤其不能因为"人在登录页"就跳过：登录页的启动探测正是最容易
        // 撞上它的地方（旧 Cookie 里带着已停用租户的 tenant_id），不清的话后续登录请求
        // 继续携带失效的 X-Tenant-Id，用户会一直登不进来。服务端那侧见
        // TenantSessionRecoveryMiddleware。
        //
        // 反过来普通 401 不带这个头，租户去向由 AuthService.clearAuthData() 按身份形态
        // 决定——本地身份保留登录入口的选择，OIDC 形态的租户来自令牌声明，跟着主体一起清。
        if (error.headers.get('X-Tenant-Invalid')) {
          tenantContext.clear();
        }

        // 会话清理与重新认证是另一件事，它有三个前提：
        //
        // 1) 不在认证路由上。那条流程正在建立主体，插手会把刚建立的主体清掉，而清掉之后
        //    启动流照常判成功、回调页照常跳进受保护路由、Guard 发现没有主体又发起一次
        //    授权——callback → 清主体 → workspace → authorize → callback，循环只是
        //    多绕一跳。认证流程自己知道该怎么处置 401（见 StartupService）。
        // 2) 请求不是静默的。静默表示"别为这次后台请求打断用户"，处置权在调用方。
        // 3) 仍然持有主体。没有主体就没有东西要清，重新认证也该由 Guard 或启动流在它们
        //    自己的时机发起。这同时是并发 401 的收敛点——令牌到期时一屏请求会一起 401，
        //    第一条同步清掉主体，同批后到的到这里已经没有主体：既不会重复导航把最初的
        //    落地地址覆盖掉，也不会让 OIDC 客户端并发跑两遍授权。后者是真会坏事的：
        //    authorize() 先异步读配置与发现文档才拼出授权地址，而每条流程都会重新生成
        //    并覆盖 PKCE codeVerifier，两条交叉后回调换 token 会失败。
        if (!isOnAuthRoute() && !req.context.get(SILENT_AUTH) && authService.isAuthenticated()) {
          // 主体离开要清干净：只清认证数据会把权限和设置留给下一个登录的人，
          // 表现是新用户看到上一个人的显示偏好。
          sessionContext.clear();
          //#if (LocalIdentity)
          void router.navigate(['/auth/login'], { queryParams: { returnUrl: entryRouteUrl() } });
          //#else
          // 令牌到期是这种形态的常规生命周期，不是异常：没有静默续期也没有刷新令牌，
          // 而 isAuthenticated() 只看内存里的主体，它不会自己变假。这里不重新发起认证，
          // Guard 就继续放行、旧权限旧设置继续显示、请求全部 401，用户只能自己硬刷新。
          authService.login(entryRouteUrl());
          //#endif
        }
      }

      return throwError(() => ApplicationHttpError.from(error));
    }),
  );
};
