import { HttpInterceptorFn, HttpErrorResponse } from '@angular/common/http';
import { Injector, inject } from '@angular/core';
//#if (LocalIdentity)
import { Router } from '@angular/router';
//#endif
//#if (IncludeLocalization)
import { TranslocoService } from '@jsverse/transloco';
//#endif
import { catchError, throwError } from 'rxjs';

import { SILENT_AUTH } from './http-context-tokens';
//#if (LocalIdentity)
import { API_ERROR_CODES } from '../errors/api-error-codes';
import { apiErrorCode, ApplicationHttpError } from '../errors/application-http-error';
//#else
import { ApplicationHttpError } from '../errors/application-http-error';
//#endif
import { EntryRouteService } from '../routing/entry-route-service';
import { AuthService } from '../services/auth-service';
import { SessionContextService } from '../services/session-context-service';
import { TenantContextService } from '../services/tenant-context-service';
import { TENANT_INVALID_HEADER } from '../services/tenant-protocol';

export const httpErrorInterceptor: HttpInterceptorFn = (req, next) => {
  const authService = inject(AuthService);
  const tenantContext = inject(TenantContextService);
  const entryRoute = inject(EntryRouteService);
  // TranslocoService 与 SessionContextService 不能在这里直接注入：前者加载词条走 HttpClient，
  // 后者构造时创建 LanguageService、它立即加载初始语言的词条，这些请求都要经过本拦截器，
  // 构造期注入就是循环依赖（NG0200，整个应用停在启动页）。出错时再按需取——那时它们早已构造完成。
  const injector = inject(Injector);
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
        // 继续携带失效的租户提示头，用户会一直登不进来。服务端那侧见多租户组件的
        // 租户会话自恢复中间件（Program 里的 UseTenantSessionRecovery）。
        //
        // 反过来普通 401 不带这个头，租户去向由 AuthService.clearAuthData() 按身份形态
        // 决定——本地身份保留登录入口的选择，OIDC 形态的租户来自令牌声明，跟着主体一起清。
        if (error.headers.get(TENANT_INVALID_HEADER)) {
          tenantContext.clear();
        }

        // 会话清理与重新认证是另一件事，它有四个前提：
        //
        // 1) 不在认证路由上。那条流程正在建立主体，插手会把刚建立的主体清掉，而清掉之后
        //    启动流照常判成功、回调页照常跳进受保护路由、Guard 发现没有主体又发起一次
        //    授权——callback → 清主体 → workspace → authorize → callback，循环只是
        //    多绕一跳。认证流程自己知道该怎么处置 401（见 StartupService）。
        // 2) 请求不是静默的。静默表示"别为这次后台请求打断用户"，处置权在调用方。
        // 3) 仍然持有主体。没有主体就没有东西要清，重新认证也该由 Guard 或启动流在它们
        //    自己的时机发起。这同时是并发 401 的收敛点——令牌到期时一屏请求会一起 401，
        //    第一条同步清掉主体，同批后到的到这里已经没有主体：既不会重复导航把最初的
        //    落地地址覆盖掉，也不会重复发起整页登录导航。
        // 4) 这个 401 说的是"会话没了"，而不是"这次操作被拒"。两种含义恰好共用一个状态码：
        //    再认证（改口令、停用两步验证、重发恢复码）连续失败触发的临时锁定属于后者，
        //    服务端明确不踢已有会话（见 User.AllowsExistingSessions）。清掉就与那条设计相反，
        //    而且会把人送到登录页——登录页在锁定期内恰恰进不去。只看状态码分不开这两者。
        //    只豁免临时锁定：管理员锁定（Auth:UserLockedOut，无截止时间）下会话本就该结束。
        //#if (LocalIdentity)
        const lockedOutButStillSignedIn =
          apiErrorCode(error.error) === API_ERROR_CODES.userTemporarilyLockedOut;
        //#else
        // 资源服务形态没有再认证入口（认证在签发方），401 只有"会话没了"一种含义
        const lockedOutButStillSignedIn = false;
        //#endif

        if (
          !lockedOutButStillSignedIn &&
          !entryRoute.isOnAuthRoute() &&
          !req.context.get(SILENT_AUTH) &&
          authService.isAuthenticated()
        ) {
          // 主体离开要清干净：只清认证数据会把权限和设置留给下一个登录的人，
          // 表现是新用户看到上一个人的显示偏好。
          injector.get(SessionContextService).clear();
          //#if (LocalIdentity)
          void router.navigate(['/auth/login'], { queryParams: { returnUrl: entryRoute.url() } });
          //#else
          // 服务端已尝试刷新；401 表示会话无法续期或已撤销，重新发起整页认证。
          authService.startLogin(entryRoute.url());
          //#endif
        }
      }
      //#if (LocalIdentity)

      // 受限会话（组织要求两步验证而本人尚未启用）调了设置之外的接口：带去设置页。
      // 路由守卫已经挡住了页面导航，这里兜住的是页面之外发出的请求（例如会话中途被改成受限）。
      if (
        error.status === 403 &&
        apiErrorCode(error.error) === API_ERROR_CODES.twoFactorSetupRequired &&
        !entryRoute.isOnAuthRoute()
      ) {
        void router.navigateByUrl('/auth/two-factor-setup');
      }
      //#endif

      //#if (IncludeLocalization)
      // 错误对象带着的是现成文字，事后不会随词条更新：词条尚未到达（首帧前与初始语言词条并行的启动请求）时
      // translate() 返回键本身，此时交给 ApplicationHttpError 用它自带的英文说法，而不是把裸键写进去。
      const transloco = injector.get(TranslocoService);
      const text = (key: string): string | undefined => {
        const value = transloco.translate(key);
        return value === key ? undefined : value;
      };
      return throwError(() =>
        ApplicationHttpError.from(
          error,
          error.status === 0 ? text('common.networkError') : undefined,
          text('common.traceId'),
        ),
      );
      //#else
      return throwError(() => ApplicationHttpError.from(error));
      //#endif
    }),
  );
};
