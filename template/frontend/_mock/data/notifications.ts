import type { AppNotification } from '../../src/app/core/services/signalr-service';

/** Mock 通知数据（内存）。 */
export const NOTIFICATIONS: AppNotification[] = [
  {
    id: '00000000-0000-0000-0000-000000000001',
    title: '欢迎使用',
    content: '这是一条示例系统通知。',
    type: 'System',
    isRead: false,
    creationTime: new Date(Date.now() - 60_000).toISOString()
  },
  {
    id: '00000000-0000-0000-0000-000000000002',
    title: '数据已更新',
    content: '你关注的数据发生了变更。',
    type: 'DataChange',
    isRead: true,
    creationTime: new Date(Date.now() - 3_600_000).toISOString()
  }
];
