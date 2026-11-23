import { HttpHeaders, HttpRequest } from '@angular/common/http';

export class MockException extends Error {
  constructor(
    public status: number,
    public error?: any,
  ) {
    super();
  }
}

/**
 * 处理器要显式给出状态码、响应头或延迟时返回的响应；其他返回值一律按 200 正文返回。
 * 私有字段让它按名义区分：对象字面量不能当成它，自带 `status` 字段的业务 DTO 也就不会被误当成响应。
 */
export class MockResponse {
  readonly #brand = true;
  readonly status: number;
  readonly headers?: HttpHeaders;
  readonly body?: unknown;
  /** 模拟延迟，单位：毫秒。 */
  readonly delay?: number;

  constructor(
    init: { status?: number; headers?: HttpHeaders; body?: unknown; delay?: number } = {},
  ) {
    this.status = init.status ?? 200;
    this.headers = init.headers;
    this.body = init.body;
    this.delay = init.delay;
  }

  /** 按私有字段判定：只有经构造函数创建的实例才算，同名字段的普通对象不算。 */
  static is(value: unknown): value is MockResponse {
    return typeof value === 'object' && value !== null && #brand in value;
  }
}

export interface MockRequest {
  readonly original: HttpRequest<any>;
  readonly url: string;
  readonly queryParams: Record<string, any>;
  readonly headers: HttpHeaders;
  readonly body: any;
  params: any;
}

export interface MockConfig {
  enable: boolean;
  include?: string | string[];
  exclude?: string | string[];
  /** 模拟延迟，单位：毫秒。 */
  delay?: number;
  log?: boolean;
}
