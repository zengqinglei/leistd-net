import type { NotificationOutputDto } from '../../src/app/core/services/signalr-service';

/** Mock 通知数据（内存）。 */
export const NOTIFICATIONS: NotificationOutputDto[] = [
  {
    id: '00000000-0000-0000-0000-000000000001',
    title: 'Welcome to the Leistd full-stack template notification center',
    content:
      'This is a sample system notification demonstrating how title, content, time, and the unread marker are laid out in the bell panel.',
    type: 'System',
    isRead: false,
    creationTime: new Date(Date.now() - 60_000).toISOString()
  },
  {
    id: '00000000-0000-0000-0000-000000000002',
    title: 'Data updated: the customer order sync task has completed',
    content:
      'Data you follow has changed. This content is intentionally longer to verify that the PrimeNG tooltip width adapts to the viewport after truncation.',
    type: 'DataChange',
    isRead: true,
    creationTime: new Date(Date.now() - 3_600_000).toISOString()
  },
  {
    id: '00000000-0000-0000-0000-000000000003',
    title: 'Workflow approval reminder: a cross-department purchase request is awaiting your action',
    content:
      'The request contains multiple line items and budget notes. Please approve it before end of day today or reassign it to the responsible owner.',
    type: 'Workflow',
    isRead: false,
    creationTime: new Date(Date.now() - 7_200_000).toISOString()
  }
];
