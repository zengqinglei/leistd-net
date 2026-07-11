import type { NotificationOutputDto } from '../../src/app/core/services/signalr-service';

/** Mock 通知数据（内存）。 */
export const NOTIFICATIONS: NotificationOutputDto[] = [
  {
    id: '00000000-0000-0000-0000-000000000001',
    title: '欢迎使用 Leistd 全栈模板通知中心',
    content: '这是一条示例系统通知，用于展示标题、内容、时间和未读标记在小铃铛面板中的层次关系。',
    type: 'System',
    isRead: false,
    creationTime: new Date(Date.now() - 60_000).toISOString()
  },
  {
    id: '00000000-0000-0000-0000-000000000002',
    title: '数据已更新：客户订单同步任务已完成',
    content: '你关注的数据发生了变更。该内容故意设置得更长一些，用于验证内容截断后的 PrimeNG Tooltip 展示宽度能跟随视口自适应。',
    type: 'DataChange',
    isRead: true,
    creationTime: new Date(Date.now() - 3_600_000).toISOString()
  },
  {
    id: '00000000-0000-0000-0000-000000000003',
    title: '工作流审批提醒：有一条跨部门采购申请等待你处理',
    content: '申请单包含多个物料明细和预算说明，请在今天下班前完成审批或转交给对应负责人。',
    type: 'Workflow',
    isRead: false,
    creationTime: new Date(Date.now() - 7_200_000).toISOString()
  }
];
