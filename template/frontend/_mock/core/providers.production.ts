import { HttpInterceptorFn } from '@angular/common/http';
import { Provider } from '@angular/core';

import { MockConfig } from './models';

// 生产构建经 angular.json 的 fileReplacements 替换 providers.ts：
// 不引用拦截器与 Mock 数据，二者不进生产包。签名须与 providers.ts 中 app.config.ts 用到的部分一致。

export function provideMock(_config: boolean | MockConfig): Provider[] {
  return [];
}

export function mockInterceptors(_config: boolean | MockConfig): HttpInterceptorFn[] {
  return [];
}
