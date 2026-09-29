import { HttpErrorResponse } from '@angular/common/http';

/**
 * 字段级错误。数组形的 `field` 按服务端 JSON 命名策略写出（与请求体字段同名）；
 * 官方字典形的键是属性名（PascalCase）。只用于分组展示消息，不按它定位表单控件。
 */
export interface ApiErrorItem {
  code?: string;
  detail?: string;
  field?: string;
}

/**
 * RFC 9457 问题详情。`errors` 有两种形状：
 * 本框架的数组 `[{ field, detail, code }]`（逐字段带错误码），
 * 与官方 `HttpValidationProblemDetails` 的字典 `{ 字段: [消息…] }`（Minimal API 的 `AddValidation()`，键为属性名）。
 */
interface ApiProblemDetails {
  code?: string;
  detail?: string;
  errors?: ApiErrorItem[] | Record<string, string[] | string>;
  title?: string;
  traceId?: string;
}

const DEFAULT_NETWORK_ERROR_MESSAGE =
  'Unable to reach the server. Check your connection and try again.';

export class ApplicationHttpError extends Error {
  readonly code?: string;
  readonly details: readonly ApiErrorItem[];
  readonly status: number;
  readonly traceId?: string;
  readonly traceIdLabel: string;
  readonly url: string | null;

  private constructor(
    response: HttpErrorResponse,
    problem: ApiProblemDetails,
    traceIdLabel: string,
  ) {
    const details = readErrors(problem.errors);
    // 有字段错误时由它们组成消息，而不是 detail / title：校验失败时那两项只是概括
    // （"One or more validation errors occurred." / "提交的信息有误。"），说不出哪条规则没过，
    // 而字段错误是服务端已按当前语言本地化好的具体原因。先前把 title 排在前面，
    // 用户看到的永远是那句概括，真正的原因只剩在网络面板里。
    const fieldMessages = firstMessagePerField(details);
    const message =
      (fieldMessages.length > 0 ? fieldMessages.join('\n') : undefined) ||
      problem.detail ||
      problem.title ||
      response.statusText ||
      'The request failed.';

    super(message, { cause: response });
    this.name = 'ApplicationHttpError';
    this.status = response.status;
    this.traceId = problem.traceId;
    this.traceIdLabel = traceIdLabel;
    this.url = response.url;
    this.code = stringCode(problem) || details.find((item) => item.code)?.code;
    this.details = details;
  }

  /**
   * @param networkErrorMessage 没有收到任何响应（status 0：断网、请求被中止、跨域被拦）时展示的消息。
   *   这时 `error` 是浏览器的异常对象，它的 message（"Failed to fetch"）是给开发者看的英文，
   *   而且不同浏览器措辞不同；原始异常仍经 `cause` 保留。
   */
  static from(
    response: HttpErrorResponse,
    networkErrorMessage?: string,
    traceIdLabel = 'Trace ID',
  ): ApplicationHttpError {
    if (response.status === 0) {
      return new ApplicationHttpError(
        response,
        {
          detail: networkErrorMessage ?? DEFAULT_NETWORK_ERROR_MESSAGE,
        },
        traceIdLabel,
      );
    }
    return new ApplicationHttpError(response, readProblemDetails(response.error), traceIdLabel);
  }
}

export function applicationErrorMessage(error: unknown): string {
  if (error instanceof ApplicationHttpError && error.status >= 500 && error.traceId) {
    return `${error.message} (${error.traceIdLabel}: ${error.traceId})`;
  }
  return error instanceof Error ? error.message : 'An unexpected error occurred.';
}

/**
 * 每个字段只取第一条：同一字段常同时违反多条规则（空串既"必填"又"长度不足"），
 * 全列出来是重复噪音，改掉第一条后下一条自然会再报。
 */
function firstMessagePerField(details: readonly ApiErrorItem[]): string[] {
  const seenFields = new Set<string>();
  const messages: string[] = [];
  for (const item of details) {
    const text = item.detail;
    const field = item.field ?? '';
    if (!text || seenFields.has(field) || messages.includes(text)) {
      continue;
    }
    seenFields.add(field);
    messages.push(text);
  }
  return messages;
}

function readProblemDetails(value: unknown): ApiProblemDetails {
  if (typeof value === 'string') {
    return { detail: value };
  }

  if (!value || typeof value !== 'object' || Array.isArray(value)) {
    return {};
  }

  return value as ApiProblemDetails;
}

/** 从 Problem Details 读取稳定业务错误码。 */
export function apiErrorCode(value: unknown): string | undefined {
  return stringCode(readProblemDetails(value));
}

function stringCode(problem: ApiProblemDetails): string | undefined {
  return typeof problem.code === 'string' ? problem.code : undefined;
}

/** 两种形状都展开为 `ApiErrorItem`：字典的每条消息各成一项，保留字段名与全部消息。 */
function readErrors(errors: ApiProblemDetails['errors']): ApiErrorItem[] {
  if (Array.isArray(errors)) {
    return errors.filter((item): item is ApiErrorItem => !!item && typeof item === 'object');
  }

  if (!errors || typeof errors !== 'object') {
    return [];
  }

  return Object.entries(errors).flatMap(([field, messages]) =>
    (Array.isArray(messages) ? messages : [messages])
      .filter((message): message is string => typeof message === 'string')
      .map((detail) => ({ field, detail })),
  );
}
