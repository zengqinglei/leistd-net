import { MockRequest } from '../core/models';
import { NOTIFICATIONS } from '../data/notifications';

/** 通知中心 Mock API（对应 NotificationsController）。 */
export const NOTIFICATION_API = {
  'GET /api/v1/notifications': () => {
    return [...NOTIFICATIONS].sort(
      (a, b) => new Date(b.creationTime).getTime() - new Date(a.creationTime).getTime(),
    );
  },

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
