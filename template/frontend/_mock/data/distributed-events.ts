import {
  DistributedEventCatalogOutputDto,
  DistributedEventDatabaseOutputDto,
  DistributedEventInboxDetailOutputDto,
  DistributedEventOutboxDetailOutputDto,
  DistributedEventPayloadOutputDto,
  DistributedEventRejectionDetailOutputDto,
  DistributedEventStatusOutputDto,
} from '../../src/app/features/platform/dtos/distributed-event.dto';

/** 页面内样本；覆盖宿主与两个租户、三种终态和退避，不持久化到共享存储。 */
export const DISTRIBUTED_EVENT_TENANTS = [
  '00000000-0000-4000-8000-000000000011',
  '00000000-0000-4000-8000-000000000012',
];

export const DISTRIBUTED_EVENT_DATABASES: DistributedEventDatabaseOutputDto[] = [
  { key: 'host', isHost: true, tenantIds: [] },
  {
    key: DISTRIBUTED_EVENT_TENANTS[0],
    isHost: false,
    representativeTenantId: DISTRIBUTED_EVENT_TENANTS[0],
    tenantIds: DISTRIBUTED_EVENT_TENANTS,
  },
];

const seededAt = Date.now();
const ago = (seconds: number) => new Date(seededAt - seconds * 1000).toISOString();
const eventId = (index: number) => `00000000-0000-4000-8000-${index.toString().padStart(12, '0')}`;
const headers = {
  traceparent: '00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01',
  'leistd-correlation-id': '4bf92f3577b34da6a3ce929d0e0e4736',
};

export const DISTRIBUTED_EVENT_OUTBOX: DistributedEventOutboxDetailOutputDto[] = [
  {
    record: {
      id: '1',
      eventId: eventId(101),
      eventName: 'order.paid',
      tenantId: DISTRIBUTED_EVENT_TENANTS[0],
      orderingKey: 'order:10086',
      occurredAt: ago(600),
      status: 'deadLettered',
      attemptCount: 1,
      lastErrorCode: 'message-too-large',
      lastError: 'The message exceeds the transport size limit.',
      redeliverCount: 0,
      creationTime: ago(590),
      firstFailureAt: ago(589),
      lastModificationTime: ago(589),
    },
    headers,
  },
  {
    record: {
      id: '2',
      eventId: eventId(102),
      eventName: 'customer.activated',
      tenantId: DISTRIBUTED_EVENT_TENANTS[1],
      orderingKey: 'customer:10002',
      occurredAt: ago(90000),
      status: 'deadLettered',
      attemptCount: 38,
      lastErrorCode: 'retry-window-exceeded',
      lastError: 'The retry window has elapsed.',
      redeliverCount: 0,
      creationTime: ago(89990),
      firstFailureAt: ago(89989),
      lastModificationTime: ago(60),
    },
    headers,
  },
  {
    record: {
      id: '3',
      eventId: eventId(103),
      eventName: 'invoice.issued',
      occurredAt: ago(400),
      status: 'pending',
      attemptCount: 3,
      nextAttemptAt: ago(-30),
      lastErrorCode: 'connection-reset',
      lastError: 'The connection was reset.',
      redeliverCount: 0,
      creationTime: ago(390),
      firstFailureAt: ago(389),
      lastModificationTime: ago(20),
    },
    headers,
  },
  {
    record: {
      id: '4',
      eventId: eventId(104),
      eventName: 'order.paid',
      tenantId: DISTRIBUTED_EVENT_TENANTS[0],
      orderingKey: 'order:10085',
      occurredAt: ago(180),
      status: 'sent',
      attemptCount: 1,
      redeliverCount: 0,
      creationTime: ago(170),
      sentAt: ago(169),
      lastModificationTime: ago(169),
    },
    headers,
  },
];

export const DISTRIBUTED_EVENT_INBOX: DistributedEventInboxDetailOutputDto[] = [
  {
    record: {
      id: '1',
      eventId: eventId(102),
      eventName: 'customer.activated',
      tenantId: DISTRIBUTED_EVENT_TENANTS[1],
      orderingKey: 'customer:10002',
      occurredAt: ago(90000),
      status: 'failed',
      attemptCount: 5,
      consumer: 'Portal.CustomerProjectionHandler',
      lastErrorCode: 'handler-exception',
      lastError: 'The projection could not be updated.',
      duplicateCount: 1,
      replayCount: 0,
      receivedAt: ago(800),
      lastModificationTime: ago(600),
    },
    headers,
    coordinates: { exchange: 'leistd.events', routingKey: 'customer.activated' },
  },
  {
    record: {
      id: '2',
      eventId: eventId(104),
      eventName: 'order.paid',
      tenantId: DISTRIBUTED_EVENT_TENANTS[0],
      orderingKey: 'order:10085',
      occurredAt: ago(180),
      status: 'processed',
      attemptCount: 1,
      consumer: 'Erp.OrderSyncHandler',
      duplicateCount: 0,
      replayCount: 0,
      receivedAt: ago(168),
      processedAt: ago(167),
      lastModificationTime: ago(167),
    },
    headers,
    coordinates: { exchange: 'leistd.events', routingKey: 'order.paid' },
  },
  {
    record: {
      id: '3',
      eventId: eventId(105),
      eventName: 'event-bus.test',
      occurredAt: ago(1),
      status: 'pending',
      attemptCount: 0,
      consumer: 'Leistd.EventBus.Test',
      duplicateCount: 0,
      replayCount: 0,
      receivedAt: ago(1),
      nextAttemptAt: ago(-59),
      lastModificationTime: ago(1),
    },
    headers,
    coordinates: {},
  },
];

export const DISTRIBUTED_EVENT_REJECTIONS: DistributedEventRejectionDetailOutputDto[] = [
  {
    record: {
      id: '1',
      transport: 'RabbitMQ',
      reason: 'invalid-payload',
      error: 'The payload is not valid JSON.',
      status: 'pending',
      redecodeCount: 0,
      receivedAt: ago(300),
      lastModificationTime: ago(300),
    },
    headers: {},
    coordinates: { exchange: 'leistd.events', routingKey: 'customer.activated' },
  },
  {
    record: {
      id: '2',
      transport: 'RabbitMQ',
      reason: 'invalid-envelope',
      error: 'The event name was missing.',
      status: 'discarded',
      redecodeCount: 0,
      receivedAt: ago(600),
      resolvedAt: ago(100),
      lastModificationTime: ago(100),
    },
    headers: {},
    coordinates: {},
  },
];

/** 单独保存，概要与详情 API 不会意外返回载荷。 */
export const DISTRIBUTED_EVENT_PAYLOADS: Record<string, DistributedEventPayloadOutputDto> = {
  'outbox:1': { payload: '{"orderId":"10086","amount":120}' },
  'outbox:2': { payload: '{"customerId":"10002","version":2}' },
  'outbox:3': { payload: '{"invoiceId":"10003"}' },
  'outbox:4': { payload: '{"orderId":"10085","amount":80}' },
  'inbox:1': { payload: '{"customerId":"10002","version":2}' },
  'inbox:2': { payload: '{"orderId":"10085","amount":80}' },
  'inbox:3': { payload: '{"message":"Test event"}' },
  'rejections:1': { rawBody: 'eyJicm9rZW4iOg==' },
  'rejections:2': { rawBody: 'e30=' },
};

export const DISTRIBUTED_EVENT_STATUS: DistributedEventStatusOutputDto = {
  pendingOutbox: 1,
  oldestPendingOutboxAgeSeconds: 390,
  deadLettered: 2,
  pendingInbox: 1,
  failedInbox: 1,
  pendingRejections: 1,
  sentLast24Hours: 1,
  processedLast24Hours: 1,
  transportName: 'RabbitMQ',
  transportStatus: 'healthy',
  testEventEnabled: true,
};

export const DISTRIBUTED_EVENT_CATALOG: DistributedEventCatalogOutputDto[] = [
  {
    eventName: 'order.paid',
    outgoing: true,
    incoming: true,
    consumers: ['Erp.OrderSyncHandler'],
    lastSentAt: ago(169),
    lastReceivedAt: ago(168),
    lastProcessedAt: ago(167),
    pendingOutbox: 0,
    pendingInbox: 0,
    deadLettered: 1,
    failedInbox: 0,
    lastErrorCode: 'message-too-large',
  },
  {
    eventName: 'customer.activated',
    outgoing: true,
    incoming: true,
    consumers: ['Portal.CustomerProjectionHandler'],
    lastReceivedAt: ago(800),
    pendingOutbox: 0,
    pendingInbox: 0,
    deadLettered: 1,
    failedInbox: 1,
    lastErrorCode: 'handler-exception',
  },
  {
    eventName: 'invoice.issued',
    outgoing: true,
    incoming: false,
    consumers: [],
    pendingOutbox: 1,
    pendingInbox: 0,
    deadLettered: 0,
    failedInbox: 0,
    lastErrorCode: 'connection-reset',
  },
  {
    eventName: 'event-bus.test',
    outgoing: false,
    incoming: true,
    consumers: ['Leistd.EventBus.Test'],
    lastReceivedAt: ago(1),
    pendingOutbox: 0,
    pendingInbox: 1,
    deadLettered: 0,
    failedInbox: 0,
  },
];
