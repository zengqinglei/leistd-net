import { HttpInterceptorFn } from '@angular/common/http';
import { Provider } from '@angular/core';

import { MOCK_APIS, mockInterceptor } from './interceptor';
import { shouldProvideMock } from './matching';
import { MockConfig } from './models';
import * as allApis from '../index';

/**
 * 提供 Mock 服务的核心函数。
 *
 * @param config Mock 配置对象，通常来自环境文件。
 * @returns 返回一个 Provider 数组，可直接在 app.config.ts 的 providers 中使用。
 */
export function provideMock(config: boolean | MockConfig): Provider[] {
  if (!shouldProvideMock(config)) {
    return [];
  }

  // 动态地将所有导入的 *_API 对象合并到一个 APIS 对象中
  const apis = Object.values(allApis)
    .filter((value) => typeof value === 'object' && value !== null)
    .reduce((acc, current) => ({ ...acc, ...current }), {});

  return [{ provide: MOCK_APIS, useValue: apis }];
}

/**
 * 按配置返回 Mock 拦截器，供 app.config.ts 的 withInterceptors 展开。
 */
export function mockInterceptors(config: boolean | MockConfig): HttpInterceptorFn[] {
  return shouldProvideMock(config) ? [mockInterceptor] : [];
}

/** 这个请求是否由 Mock 应答。只有本机开发构建会把 Mock 编进包里，其他构建里恒为 false。 */
export { isMockedUrl } from './matching';
