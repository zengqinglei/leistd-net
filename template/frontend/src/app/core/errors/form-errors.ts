import { FieldTree, ValidationError } from '@angular/forms/signals';

import { ApplicationHttpError, applicationErrorMessage } from './application-http-error';

/** 已声明字段才参与匹配；DTO 别名、嵌套与索引路径由调用处显式指向字段。 */
export function mapFormErrors<T>(
  error: unknown,
  fields: FieldTree<T>,
  aliases: Readonly<Record<string, ValidationError.WithFieldTree['fieldTree']>> = {},
  fallbackMessage = 'The request could not be completed.',
): { errors: ValidationError.WithFieldTree[]; message: string | null } {
  if (!(error instanceof ApplicationHttpError) || !error.details.length) {
    return { errors: [], message: applicationErrorMessage(error) };
  }
  const declared = new Map<string, ValidationError.WithFieldTree['fieldTree']>();
  for (const key of Object.keys(fields)) {
    declared.set(
      key.toLowerCase(),
      (fields as Record<string, ValidationError.WithFieldTree['fieldTree']>)[key],
    );
  }
  for (const [key, field] of Object.entries(aliases)) declared.set(key.toLowerCase(), field);
  const errors: ValidationError.WithFieldTree[] = [];
  const remaining: string[] = [];
  let missingMessage = false;
  for (const item of error.details) {
    const fieldTree = item.field ? declared.get(item.field.toLowerCase()) : undefined;
    if (fieldTree && item.detail) {
      errors.push({ kind: 'server', message: item.detail, fieldTree });
    } else {
      if (item.detail) remaining.push(item.detail);
      else missingMessage = true;
    }
  }
  // 混合响应里的纯错误码不能再次回显已经挂到字段上的摘要。
  if (missingMessage) remaining.push(errors.length ? fallbackMessage : error.message);
  return { errors, message: remaining.length ? [...new Set(remaining)].join('\n') : null };
}
