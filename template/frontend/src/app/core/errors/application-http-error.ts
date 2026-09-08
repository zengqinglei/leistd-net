import { HttpErrorResponse } from '@angular/common/http';

interface ApiErrorItem {
  code?: string;
  detail?: string;
  message?: string;
  pointer?: string;
}

interface ApiProblemDetails {
  code?: string;
  detail?: string;
  errors?: ApiErrorItem[];
  message?: string;
  title?: string;
}

export class ApplicationHttpError extends Error {
  readonly code?: string;
  readonly details: readonly ApiErrorItem[];
  readonly status: number;
  readonly url: string | null;

  private constructor(response: HttpErrorResponse, problem: ApiProblemDetails) {
    const details = Array.isArray(problem.errors) ? problem.errors : [];
    const message =
      problem.detail ||
      problem.message ||
      problem.title ||
      details.find((item) => item.detail || item.message)?.detail ||
      details.find((item) => item.detail || item.message)?.message ||
      response.statusText ||
      'The request failed.';

    super(message, { cause: response });
    this.name = 'ApplicationHttpError';
    this.status = response.status;
    this.url = response.url;
    this.code = problem.code || details.find((item) => item.code)?.code;
    this.details = details;
  }

  static from(response: HttpErrorResponse): ApplicationHttpError {
    return new ApplicationHttpError(response, readProblemDetails(response.error));
  }
}

export function applicationErrorMessage(error: unknown): string {
  return error instanceof Error ? error.message : 'An unexpected error occurred.';
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
