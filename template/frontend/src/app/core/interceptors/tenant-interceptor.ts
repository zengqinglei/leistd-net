//#if (TenancyEnabled)
import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';

import { TenantContextService } from '../services/tenant-context-service';

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

  // 租户探测端点（登录前选择、启动校验）是宿主级匿名查询，不附租户头——
  // 否则"校验本地租户是否仍可用"的请求会带着失效租户头先被 403 拒绝，形成死锁。
  if (req.url.startsWith('/api/v1/tenants/by-name/')) {
    return next(req);
  }

  const tenant = inject(TenantContextService).current();
  if (!tenant) {
    return next(req);
  }

  return next(req.clone({ setHeaders: { 'X-Tenant-Id': tenant.id } }));
};
//#endif
