import { MockException } from './models';

/**
 * 解析 `"字段 asc|desc"` 排序串并按白名单校验字段，与后端 `SortingRequest` 同形；Mock 不能比后端宽松，
 * 否则只在 Mock 下可用的排序字段接上真实服务才收到 400。
 */
export function parseMockSorting<TField extends string>(
  sorting: unknown,
  allowedFields: readonly TField[],
  defaultField: TField,
): { field: TField; descending: boolean } {
  const expression = Array.isArray(sorting) ? sorting[0] : sorting;
  const text = expression === undefined || expression === null ? '' : String(expression).trim();

  if (text === '') {
    return { field: defaultField, descending: false };
  }

  const parts = text.split(/\s+/);
  const [field, rawDirection] = parts;
  // 后端用 OrdinalIgnoreCase 比较方向词，Mock 只认小写就会比真实服务更严
  const direction = rawDirection?.toLowerCase();

  if (
    parts.length > 2 ||
    (direction !== undefined && direction !== 'asc' && direction !== 'desc')
  ) {
    throw new MockException(400, {
      message: `Invalid sorting expression: ${text}`,
    });
  }

  if (!allowedFields.includes(field as TField)) {
    throw new MockException(400, {
      message: `Unsupported sorting field: ${field}`,
    });
  }

  return { field: field as TField, descending: direction === 'desc' };
}
