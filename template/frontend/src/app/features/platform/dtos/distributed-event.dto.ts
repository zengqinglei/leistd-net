import { PagedRequestDto } from '../../../shared/dtos/paged-request.dto';

/** 记录标识为十进制字符串，避免 long 超出 JavaScript 安全整数范围；时间均为 UTC ISO 字符串。 */
export type DistributedEventInboxStatus = 'pending' | 'processed' | 'failed';
export type DistributedEventOutboxStatus = 'pending' | 'sent' | 'deadLettered';
export type DistributedEventRejectionStatus = 'pending' | 'redecoded' | 'discarded';
export type DistributedEventRecoverySkipReason = 'not-found' | 'invalid-state' | 'redecode-failed';
export type DistributedEventTransportStatus = 'healthy' | 'degraded' | 'unhealthy' | 'unknown';

/** 框架自己产生的发件箱失败码；传输可以定义其他稳定失败码。 */
export const DISTRIBUTED_EVENT_OUTBOX_FAILURE_CODES = {
  retryWindowExceeded: 'retry-window-exceeded',
} as const;

export const DISTRIBUTED_EVENT_TEST = {
  name: 'event-bus.test',
  consumerName: 'Leistd.EventBus.Test',
} as const;

/** 事件目录：订阅与保留期内发件箱、收件箱事件名的并集。 */
export interface DistributedEventCatalogOutputDto {
  eventName: string;
  outgoing: boolean;
  incoming: boolean;
  consumers: string[];
  /** 最近成功发出时刻（UTC）。 */
  lastSentAt?: string | null;
  /** 最近接收时刻（UTC）。 */
  lastReceivedAt?: string | null;
  /** 最近成功处理时刻（UTC）。 */
  lastProcessedAt?: string | null;
  pendingOutbox: number;
  pendingInbox: number;
  deadLettered: number;
  failedInbox: number;
  lastErrorCode?: string | null;
}

/** 宿主可选择的物理库；不暴露连接串或指纹。 */
export interface DistributedEventDatabaseOutputDto {
  /** host 或代表租户的 Guid 字符串。 */
  key: string;
  isHost: boolean;
  representativeTenantId?: string | null;
  /** 目录提供的租户清单；宿主项为空，不代表无租户。 */
  tenantIds: string[];
}

/** 详情概要；没有管理权限也可读取，载荷不随之返回。 */
export interface DistributedEventInboxDetailOutputDto {
  record: DistributedEventInboxOutputDto;
  headers: Record<string, string>;
  coordinates: Record<string, string>;
}

/** 按条件恢复筛选；发件箱只选死信，拒收只选 Pending。 */
export interface DistributedEventInboxFilterDto {
  /** 本次恢复的时间上限（UTC），必填，防止卷入后续新记录。 */
  before: string;
  eventName?: string | null;
  /** 事件标识、排序键、TraceId 或 CorrelationId 精确匹配。 */
  keyword?: string | null;
  errorCode?: string | null;
  /** 必填，只能选 Failed 或 Processed。 */
  status: 'processed' | 'failed';
  consumer?: string | null;
}

/** 收件箱概要；不含载荷或消息头。 */
export interface DistributedEventInboxOutputDto {
  /** 记录标识，JSON 为十进制字符串。 */
  id: string;
  /** 发布方的原始事件标识。 */
  eventId: string;
  eventName: string;
  /** 记录所属租户；null 为宿主。 */
  tenantId?: string | null;
  orderingKey?: string | null;
  /** 原始业务发生时刻（UTC）。 */
  occurredAt: string;
  /** 当前一轮的尝试次数；收件箱重放后从零计数。 */
  attemptCount: number;
  /** Pending 时的下一次尝试时刻（UTC）。 */
  nextAttemptAt?: string | null;
  /** 最近失败的稳定错误码。 */
  lastErrorCode?: string | null;
  /** 最近失败的诊断文本，最多 512 个字符。 */
  lastError?: string | null;
  /** 最后状态变更时刻（UTC），保留期以此计算。 */
  lastModificationTime: string;
  status: DistributedEventInboxStatus;
  consumer: string;
  /** 额外收到相同事件的次数。 */
  duplicateCount: number;
  replayCount: number;
  /** 首次持久接收时刻（UTC）。 */
  receivedAt: string;
  /** 成功处理时刻（UTC）。 */
  processedAt?: string | null;
}

/** 详情概要；没有管理权限也可读取，载荷不随之返回。 */
export interface DistributedEventOutboxDetailOutputDto {
  record: DistributedEventOutboxOutputDto;
  headers: Record<string, string>;
}

/** 按条件恢复筛选；发件箱只选死信，拒收只选 Pending。 */
export interface DistributedEventOutboxFilterDto {
  /** 本次恢复的时间上限（UTC），必填，防止卷入后续新记录。 */
  before: string;
  eventName?: string | null;
  /** 事件标识、排序键、TraceId 或 CorrelationId 精确匹配。 */
  keyword?: string | null;
  errorCode?: string | null;
}

/** 发件箱概要；不含载荷或消息头。 */
export interface DistributedEventOutboxOutputDto {
  /** 记录标识，JSON 为十进制字符串。 */
  id: string;
  /** 发布方的原始事件标识。 */
  eventId: string;
  eventName: string;
  /** 记录所属租户；null 为宿主。 */
  tenantId?: string | null;
  orderingKey?: string | null;
  /** 原始业务发生时刻（UTC）。 */
  occurredAt: string;
  /** 累计发送尝试次数，手动重投不清零。 */
  attemptCount: number;
  /** Pending 时的下一次尝试时刻（UTC）。 */
  nextAttemptAt?: string | null;
  /** 最近失败的稳定错误码。 */
  lastErrorCode?: string | null;
  /** 最近失败的诊断文本，最多 512 个字符。 */
  lastError?: string | null;
  /** 最后状态变更时刻（UTC），保留期以此计算。 */
  lastModificationTime: string;
  /** 持久状态；Pending 且次数大于零时界面显示重试中。 */
  status: DistributedEventOutboxStatus;
  redeliverCount: number;
  /** 写入发件箱的时刻（UTC）。 */
  creationTime: string;
  /** 本轮首次发送失败的时刻（UTC）。 */
  firstFailureAt?: string | null;
  /** 成功发送时刻（UTC）。 */
  sentAt?: string | null;
}

/** 仅 Manage 策略可读取的发件箱或收件箱 JSON 载荷。 */
export interface DistributedEventPayloadOutputDto {
  payload: string;
}

/** 仅宿主 Manage 策略可读取的拒收原始消息。 */
export interface DistributedEventRawMessageOutputDto {
  /** 原始字节的 Base64 字符串；允许空消息。 */
  rawBody: string;
}

/** 恢复结果；按条件分批提交后返回累计条数。 */
export interface DistributedEventRecoveryOutputDto {
  affectedCount: number;
  /** 未改动的标识和原因；按条件不返回不存在项。 */
  skipped: DistributedEventRecoverySkipDto[];
}

/** 单条或勾选恢复未成功的记录。 */
export interface DistributedEventRecoverySkipDto {
  /** 记录标识，JSON 为十进制字符串。 */
  id: string;
  /** 稳定原因：not-found、invalid-state 或 redecode-failed。 */
  reasonCode: DistributedEventRecoverySkipReason;
}

/** 详情概要；没有管理权限也可读取，载荷不随之返回。 */
export interface DistributedEventRejectionDetailOutputDto {
  record: DistributedEventRejectionOutputDto;
  headers: Record<string, string>;
  coordinates: Record<string, string>;
}

/** 按条件恢复筛选；发件箱只选死信，拒收只选 Pending。 */
export interface DistributedEventRejectionFilterDto {
  /** 本次恢复的时间上限（UTC），必填，防止卷入后续新记录。 */
  before: string;
  eventName?: string | null;
  /** 事件标识、TraceId 或 CorrelationId 精确匹配。 */
  keyword?: string | null;
  /** 拒收原因码精确匹配；只恢复 Pending。 */
  reason?: string | null;
}

/** 宿主集中拒收概要；不含原始字节或消息头。 */
export interface DistributedEventRejectionOutputDto {
  /** 记录标识，JSON 为十进制字符串。 */
  id: string;
  transport: string;
  /** 不透明的来源坐标，用于定位无法解码的消息；不含消息头或原始字节。 */
  coordinates: Record<string, string>;
  reason: string;
  /** 拒收诊断文本，最多 512 个字符。 */
  error: string;
  eventId?: string | null;
  eventName?: string | null;
  status: DistributedEventRejectionStatus;
  redecodeCount: number;
  /** 首次收到原始消息的时刻（UTC）。 */
  receivedAt: string;
  /** 重新解码成功或丢弃时刻（UTC）。 */
  resolvedAt?: string | null;
  /** 最后状态变更时刻（UTC）。 */
  lastModificationTime: string;
}

/** 当前查看范围的状态快照；拒收范围始终为宿主集中库。 */
export interface DistributedEventStatusOutputDto {
  /** 待发记录数。 */
  pendingOutbox: number;
  /** 最旧待发记录时长（秒），无待发时为 null。 */
  oldestPendingOutboxAgeSeconds?: number | null;
  deadLettered: number;
  pendingInbox: number;
  failedInbox: number;
  /** 宿主集中待处理拒收数；租户为 null。 */
  pendingRejections?: number | null;
  sentLast24Hours: number;
  processedLast24Hours: number;
  /** 当前进程或宿主库新鲜报告中的传输名；均无时为 null。 */
  transportName?: string | null;
  /** 宿主库新鲜上报的最差健康状态；无新鲜上报时 unknown。 */
  transportStatus: DistributedEventTransportStatus;
  /** 最差状态实例中最近的状态变化时刻；unknown 时为空。 */
  transportStatusChangedAt?: string | null;
  /** 全部新鲜实例的最后上报时刻；unknown 时为空。 */
  transportReportedAt?: string | null;
  /** 端点是否开放载荷；直接管理服务返回 null。 */
  payloadEnabled?: boolean | null;
  testEventEnabled: boolean;
}

/** 测试发布结果；按原事件标识查询发件箱与收件箱。 */
export interface DistributedEventTestOutputDto {
  eventId: string;
}

/** 按 Id 倒序的偏移分页查询；Sorting 不生效。 */
export interface GetDistributedEventInboxInputDto extends PagedRequestDto {
  /** 宿主物理库选择键：host 或代表租户 Id；租户不得传。 */
  database?: string | null;
  eventName?: string | null;
  /** 事件标识、排序键、TraceId 或 CorrelationId 精确匹配。 */
  keyword?: string | null;
  /** 首次加载时刻上限（UTC），含等于该时刻的记录。 */
  before?: string | null;
  /** 状态精确匹配；查询串只接受 camelCase 名称。 */
  status?: DistributedEventInboxStatus | null;
  errorCode?: string | null;
  consumer?: string | null;
}

/** 按 Id 倒序的偏移分页查询；Sorting 不生效。 */
export interface GetDistributedEventOutboxInputDto extends PagedRequestDto {
  /** 宿主物理库选择键：host 或代表租户 Id；租户不得传。 */
  database?: string | null;
  eventName?: string | null;
  /** 事件标识、排序键、TraceId 或 CorrelationId 精确匹配。 */
  keyword?: string | null;
  /** 首次加载时刻上限（UTC），含等于该时刻的记录。 */
  before?: string | null;
  /** 状态精确匹配；查询串只接受 camelCase 名称。 */
  status?: DistributedEventOutboxStatus | null;
  errorCode?: string | null;
}

/** 按 Id 倒序的偏移分页查询；Sorting 不生效。 */
export interface GetDistributedEventRejectionsInputDto extends PagedRequestDto {
  /** 宿主物理库选择键：host 或代表租户 Id；租户不得传。 */
  database?: string | null;
  eventName?: string | null;
  /** 事件标识、TraceId 或 CorrelationId 精确匹配。 */
  keyword?: string | null;
  /** 首次加载时刻上限（UTC），含等于该时刻的记录。 */
  before?: string | null;
  /** 状态精确匹配；查询串只接受 camelCase 名称。 */
  status?: DistributedEventRejectionStatus | null;
  /** 拒收原因码精确匹配。 */
  reason?: string | null;
}

/** 恢复目标；Ids 与 Filter 必须二选一；逐项返回跳过原因。 */
export interface RecoverDistributedEventInboxInputDto {
  /** 勾选的记录标识，最多 100，JSON 接受十进制字符串。 */
  ids?: string[] | null;
  /** 按当前条件恢复，不接受分页参数。 */
  filter?: DistributedEventInboxFilterDto | null;
}

/** 恢复目标；Ids 与 Filter 必须二选一；逐项返回跳过原因。 */
export interface RecoverDistributedEventOutboxInputDto {
  /** 勾选的记录标识，最多 100，JSON 接受十进制字符串。 */
  ids?: string[] | null;
  /** 按当前条件恢复，不接受分页参数。 */
  filter?: DistributedEventOutboxFilterDto | null;
}

/** 恢复目标；Ids 与 Filter 必须二选一；逐项返回跳过原因。 */
export interface RecoverDistributedEventRejectionInputDto {
  /** 勾选的记录标识，最多 100，JSON 接受十进制字符串。 */
  ids?: string[] | null;
  /** 按当前条件恢复，不接受分页参数。 */
  filter?: DistributedEventRejectionFilterDto | null;
}
