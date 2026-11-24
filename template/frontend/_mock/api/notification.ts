import { PagedResultDto } from '../../src/app/shared/dtos/paged-result.dto';
import { MockException, MockRequest } from '../core/models';
//#if (IncludeLocalization)
import { NOTIFICATION_TEXTS_ZH_CN, NOTIFICATIONS } from '../data/notifications';
//#else
import { NOTIFICATIONS } from '../data/notifications';
//#endif

import type { NotificationOutputDto } from '../../src/app/shared/dtos/notification.dto';

/** 与后端 `GetNotificationsInputDto.DefaultLimit` 同值。 */
export const NOTIFICATION_DEFAULT_LIMIT = 50;

/** 与后端 `GetNotificationsInputDto.MaximumLimit` 同值。 */
export const NOTIFICATION_MAXIMUM_LIMIT = 100;

function query(req: MockRequest, name: string): string | undefined {
  const raw = req.queryParams[name];
  const value = Array.isArray(raw) ? raw[0] : raw;
  return value === undefined || value === null || value === '' ? undefined : String(value);
}

/** 与后端同一口径：最新的在前、按 offset/limit 分页；越界是 400 字段错误、不带业务码。 */
function listPage(req: MockRequest): PagedResultDto<NotificationOutputDto> {
  const offset = Number(query(req, 'offset') ?? 0);
  const limit = Number(query(req, 'limit') ?? NOTIFICATION_DEFAULT_LIMIT);
  if (!Number.isInteger(offset) || offset < 0) {
    throw new MockException(400, {
      errors: [{ field: 'offset', detail: 'Offset must be 0 or greater.' }],
    });
  }
  if (!Number.isInteger(limit) || limit < 1 || limit > NOTIFICATION_MAXIMUM_LIMIT) {
    throw new MockException(400, {
      errors: [
        { field: 'limit', detail: `Limit must be between 1 and ${NOTIFICATION_MAXIMUM_LIMIT}.` },
      ],
    });
  }
  const unreadOnly = query(req, 'unreadOnly') === 'true';
  const ordered = NOTIFICATIONS.filter((n) => !unreadOnly || !n.isRead).sort(
    (a, b) => new Date(b.creationTime).getTime() - new Date(a.creationTime).getTime(),
  );
  return { totalCount: ordered.length, items: ordered.slice(offset, offset + limit) };
}

/** 通知中心 Mock API（对应框架的 NotificationEndpoints）。 */
export const NOTIFICATION_API = {
  //#if (IncludeLocalization)
  // 中文界面给中文文案，其余语言给英文；请求头不是受支持语言时用英文（与设置接口同一规则）
  'GET /api/v1/notifications': (req: MockRequest): PagedResultDto<NotificationOutputDto> => {
    const page = listPage(req);
    if (req.headers.get('Accept-Language') !== 'zh-CN') {
      return page;
    }
    return {
      ...page,
      items: page.items.map((notification) => ({
        ...notification,
        ...NOTIFICATION_TEXTS_ZH_CN[notification.id],
      })),
    };
  },
  //#else
  'GET /api/v1/notifications': (req: MockRequest): PagedResultDto<NotificationOutputDto> =>
    listPage(req),
  //#endif

  'GET /api/v1/notifications/unread-count': () => {
    return NOTIFICATIONS.filter((n) => !n.isRead).length;
  },

  'PUT /api/v1/notifications/:id/read': (req: MockRequest) => {
    const item = NOTIFICATIONS.find((n) => n.id === req.params.id);
    if (item) item.isRead = true;
    return null;
  },

  'PUT /api/v1/notifications/read-all': () => {
    NOTIFICATIONS.forEach((n) => (n.isRead = true));
    return null;
  },

  'DELETE /api/v1/notifications': () => {
    // 持久清空：原地清空数组，后续 GET 返回空列表。
    NOTIFICATIONS.length = 0;
    return null;
  },

  'DELETE /api/v1/notifications/:id': (req: MockRequest) => {
    const index = NOTIFICATIONS.findIndex((n) => n.id === req.params.id);
    if (index !== -1) NOTIFICATIONS.splice(index, 1);
    return null;
  },
};
