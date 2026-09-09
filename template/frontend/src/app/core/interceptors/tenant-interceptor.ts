//#if (LocalIdentity)
import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';

import { TenantContextService } from '../services/tenant-context-service';

const TENANT_PROBE_PATHS = ['/api/v1/tenants/by-name/', '/api/v1/tenants/by-host'] as const;

/**
 * 已选租户时为所有 /api/ 请求附加 X-Tenant-Id 头。
 *
 * 已登录用户的租户由服务端 cookie claim 定案，此头只影响匿名请求
 * （典型是登录：决定凭据在哪个租户内校验）。
 */
export const tenantInterceptor: HttpInterceptorFn = (req, next) => {
  if (!req.url.startsWith('/api/')) {
    return next(req);
  }

  // 租户探测端点（登录前手选、按域名定案、启动校验）是宿主级匿名查询，一律不附租户头。
  //
  // 附上去就成死锁：本地记着的租户被停用或删除后，"这个租户还能用吗"这一问本身带着那个
  // 失效租户头，先被租户解析拒掉；而登录页在探测失败时会锁住租户区不许改（那是对的，
  // 因为此时并不知道域名会怎么解析），于是重试仍带同一个头、仍失败，谁也清不掉它。
  // 这两个端点只该受请求主机名与路径参数影响，不该受待校验的本地状态影响。
  if (TENANT_PROBE_PATHS.some((path) => req.url.startsWith(path))) {
    return next(req);
  }

  const tenant = inject(TenantContextService).current();
  if (!tenant) {
    return next(req);
  }

  return next(req.clone({ setHeaders: { 'X-Tenant-Id': tenant.id } }));
};
//#endif
