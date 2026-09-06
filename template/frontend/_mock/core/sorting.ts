import { MockException } from './models';

/**
 * 解析 `"字段 asc|desc"` 排序串，并按白名单校验字段。
 *
 * 与后端 `SortingRequest` 同形：解析在一处，字段白名单由各接口自己给。
 * Mock 必须复刻这条契约——它比真实后端宽松时，只在 Mock 下开发的页面会把
 * "后端根本不支持的排序字段"当成可用，直到接上真实服务才收到 400。
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
      code: 'Error:BadRequest',
      message: `Invalid sorting expression: ${text}`,
    });
  }

  if (!allowedFields.includes(field as TField)) {
    throw new MockException(400, {
      code: 'Error:BadRequest',
      message: `Unsupported sorting field: ${field}`,
    });
  }

  return { field: field as TField, descending: direction === 'desc' };
}
