import { HttpErrorResponse, HttpInterceptorFn, HttpResponse } from '@angular/common/http';
import { InjectionToken, inject } from '@angular/core';
import { from, of, throwError } from 'rxjs';
import { catchError, delay, mergeMap, tap } from 'rxjs/operators';

import { MockConfig, MockException, MockRequest, MockResponse } from './models';
import { environment } from '../../src/environments/environment';

type MockApiHandler = (request: MockRequest) => unknown;
type MockApiRegistry = Record<string, MockApiHandler | unknown>;

export const MOCK_APIS = new InjectionToken<MockApiRegistry>('MOCK_APIS');

export const mockInterceptor: HttpInterceptorFn = (req, next) => {
  const apis = inject(MOCK_APIS);
  const { url, method, params, headers, body } = req;
  const mockConfig = getMockConfig();
  const matchingRule = findMatchingRule(method, url, apis);

  if (!matchingRule) {
    if (shouldMock(url, mockConfig) && getUrlPath(url).startsWith('/api/')) {
      return throwError(
        () =>
          new HttpErrorResponse({
            error: {
              code: 'MOCK_ROUTE_NOT_FOUND',
              message: `Mock API is not defined: ${method.toUpperCase()} ${getUrlPath(url)}`,
            },
            headers: headers.set('Content-Type', 'application/json'),
            status: 501,
            statusText: 'Mock Route Not Found',
            url,
          }),
      );
    }

    return next(req);
  }

  if (!shouldMock(url, mockConfig)) {
    return next(req);
  }

  const mockRequest: MockRequest = {
    original: req,
    url,
    queryParams: params.keys().reduce((acc, key) => ({ ...acc, [key]: params.getAll(key) }), {}),
    headers,
    body,
    params: matchingRule.urlParams,
  };

  if (mockConfig.log) {
    logMock('Mock intercepted', method, url, mockRequest);
  }

  return from(Promise.resolve().then(() => matchingRule.handler(mockRequest))).pipe(
    mergeMap((result) => {
      const mockResponse = result as MockResponse | undefined;
      const response =
        mockResponse && typeof mockResponse.status !== 'undefined'
          ? new HttpResponse(mockResponse)
          : new HttpResponse({ status: 200, body: result });

      return of(response).pipe(delay(mockResponse?.delay ?? mockConfig.delay ?? 0));
    }),
    tap((response) => {
      if (mockConfig.log) {
        logMock('Mock response for', method, url, response);
      }
    }),
    catchError((error: unknown) => {
      if (mockConfig.log) {
        logMock('Mock error for', method, url, error);
      }

      if (error instanceof MockException) {
        return throwError(
          () =>
            new HttpErrorResponse({
              error: error.error,
              headers: req.headers.set('Content-Type', 'application/json'),
              status: error.status,
              statusText: 'Mock Error',
              url: req.url,
            }),
        );
      }

      return throwError(() => error);
    }),
  );
};

function findMatchingRule(
  method: string,
  url: string,
  apis: MockApiRegistry,
): { handler: MockApiHandler; urlParams: Record<string, string> } | null {
  const urlPath = getUrlPath(url);
  const exactRule = apis[`${method.toUpperCase()} ${urlPath}`];

  if (exactRule) {
    return {
      handler: typeof exactRule === 'function' ? (exactRule as MockApiHandler) : () => exactRule,
      urlParams: {},
    };
  }

  for (const [apiPattern, matchedRule] of Object.entries(apis)) {
    const [apiMethod, apiRoute] = apiPattern.split(' ');
    const match = urlPath.match(new RegExp(`^${apiRoute.replace(/:\w+/g, '([^/]+)')}$`));

    if (apiMethod !== method.toUpperCase() || !match) {
      continue;
    }

    const paramNames = (apiRoute.match(/:\w+/g) ?? []).map((name) => name.substring(1));
    const urlParams = Object.fromEntries(paramNames.map((name, index) => [name, match[index + 1]]));

    return {
      handler:
        typeof matchedRule === 'function' ? (matchedRule as MockApiHandler) : () => matchedRule,
      urlParams,
    };
  }

  return null;
}

function getUrlPath(url: string): string {
  const urlWithoutQuery = url.split('?')[0];

  try {
    return new URL(urlWithoutQuery).pathname;
  } catch {
    return urlWithoutQuery;
  }
}

function shouldMock(url: string, mockConfig: Partial<MockConfig>): boolean {
  const urlPath = getUrlPath(url);
  const includeMatched = mockConfig.include
    ? matchesPatterns(urlPath, mockConfig.include)
    : Boolean(mockConfig.enable);
  const excludeMatched = mockConfig.exclude ? matchesPatterns(urlPath, mockConfig.exclude) : false;

  return includeMatched && !excludeMatched;
}

function matchesPatterns(urlPath: string, patterns: string | string[]): boolean {
  const normalizedPatterns = Array.isArray(patterns) ? patterns : [patterns];
  return normalizedPatterns.some((pattern) => new RegExp(pattern).test(urlPath));
}

function getMockConfig(): Partial<MockConfig> {
  const config = environment.useMock;
  return typeof config === 'boolean' ? { enable: config } : config;
}

function logMock(title: string, method: string, url: string, data: unknown): void {
  console.groupCollapsed(`${title}: ${method} ${url}`);
  console.log(data);
  console.groupEnd();
}
