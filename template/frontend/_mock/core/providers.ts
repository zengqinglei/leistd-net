import { HttpInterceptorFn } from '@angular/common/http';
import { Provider } from '@angular/core';

import { MockConfig } from './models';

// 默认不带 Mock：只有本机开发构建经 angular.json 的 fileReplacements 换成 providers.mock.ts。
// 反过来写（默认带、部署构建逐个替换掉）时，新增一个部署环境漏配一条替换，Mock 数据就会静默进包。
// 导出须与 providers.mock.ts 一致。

export function provideMock(_config: boolean | MockConfig): Provider[] {
  return [];
}

export function mockInterceptors(_config: boolean | MockConfig): HttpInterceptorFn[] {
  return [];
}

export function isMockedUrl(_config: boolean | MockConfig, _url: string): boolean {
  return false;
}
