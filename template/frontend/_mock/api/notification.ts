import { NOTIFICATIONS } from '../data/notifications';

/** 通知中心 Mock API（对应 NotificationsController）。 */
export const NOTIFICATION_API = {
  'GET /api/v1/notifications': () => {
    return [...NOTIFICATIONS].sort(
      (a, b) => new Date(b.creationTime).getTime() - new Date(a.creationTime).getTime()
    );
  },

  'GET /api/v1/notifications/unread-count': () => {
    return NOTIFICATIONS.filter(n => !n.isRead).length;
  },

  'PUT /api/v1/notifications/:id/read': (_options: unknown, params: Record<string, string>) => {
    const item = NOTIFICATIONS.find(n => n.id === params['id']);
    if (item) item.isRead = true;
    return null;
  },

  'PUT /api/v1/notifications/read-all': () => {
    NOTIFICATIONS.forEach(n => (n.isRead = true));
    return null;
  }
};
