import { HttpErrorResponse } from '@angular/common/http';

/** 字段级错误；`field` 与请求体里的字段同名（服务端按 JSON 命名策略写出）。 */
export interface ApiErrorItem {
  code?: string;
  detail?: string;
  field?: string;
  message?: string;
}

interface ApiProblemDetails {
  code?: string;
  detail?: string;
  errors?: ApiErrorItem[];
  message?: string;
  title?: string;
}

const DEFAULT_NETWORK_ERROR_MESSAGE =
  'Unable to reach the server. Check your connection and try again.';

export class ApplicationHttpError extends Error {
  readonly code?: string;
  readonly details: readonly ApiErrorItem[];
  readonly status: number;
  readonly url: string | null;

  private constructor(response: HttpErrorResponse, problem: ApiProblemDetails) {
    const details = Array.isArray(problem.errors) ? problem.errors : [];
    // 有字段错误时由它们组成消息，而不是 detail / title：校验失败时那两项只是概括
    // （"One or more validation errors occurred." / "提交的信息有误。"），说不出哪条规则没过，
    // 而字段错误是服务端已按当前语言本地化好的具体原因。先前把 title 排在前面，
    // 用户看到的永远是那句概括，真正的原因只剩在网络面板里。
    const fieldMessages = firstMessagePerField(details);
    const message =
      (fieldMessages.length > 0 ? fieldMessages.join('\n') : undefined) ||
      problem.detail ||
      problem.message ||
      problem.title ||
      response.statusText ||
      'The request failed.';

    super(message, { cause: response });
    this.name = 'ApplicationHttpError';
    this.status = response.status;
    this.url = response.url;
    this.code = problem.code || details.find((item) => item.code)?.code;
    this.details = details;
  }

  /**
   * @param networkErrorMessage 没有收到任何响应（status 0：断网、请求被中止、跨域被拦）时展示的消息。
   *   这时 `error` 是浏览器的异常对象，它的 message（"Failed to fetch"）是给开发者看的英文，
   *   而且不同浏览器措辞不同；原始异常仍经 `cause` 保留。
   */
  static from(response: HttpErrorResponse, networkErrorMessage?: string): ApplicationHttpError {
    if (response.status === 0) {
      return new ApplicationHttpError(response, {
        detail: networkErrorMessage ?? DEFAULT_NETWORK_ERROR_MESSAGE,
      });
    }
    return new ApplicationHttpError(response, readProblemDetails(response.error));
  }
}

export function applicationErrorMessage(error: unknown): string {
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
    const text = item.detail || item.message;
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
