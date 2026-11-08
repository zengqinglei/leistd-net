import { requirePermission } from './authorization';
import * as Dto from '../../src/app/features/platform/dtos/distributed-event.dto';
import { PERMISSIONS } from '../../src/app/shared/constants/permission.constants';
import { MockException, MockRequest } from '../core/models';
import {
  DISTRIBUTED_EVENT_DATABASES,
  DISTRIBUTED_EVENT_OUTBOX,
  DISTRIBUTED_EVENT_INBOX,
  DISTRIBUTED_EVENT_REJECTIONS,
  DISTRIBUTED_EVENT_PAYLOADS,
  DISTRIBUTED_EVENT_RAW_MESSAGES,
} from '../data/distributed-events';
import { getMockSessionTenantKey } from '../utils/current-user';

type Kind = 'outbox' | 'inbox' | 'rejections';
type Row =
  | Dto.DistributedEventOutboxOutputDto
  | Dto.DistributedEventInboxOutputDto
  | Dto.DistributedEventRejectionOutputDto;
type Detail =
  | Dto.DistributedEventOutboxDetailOutputDto
  | Dto.DistributedEventInboxDetailOutputDto
  | Dto.DistributedEventRejectionDetailOutputDto;
type Filter = Record<string, unknown>;

/** Mock 拦截器传来 getAll() 数组；与直接调用处理器的测试使用同一读法。 */
function queryValues(req: MockRequest): Record<string, string> {
  return Object.fromEntries(
    Object.entries(req.queryParams).map(([key, value]) => [
      key,
      String(Array.isArray(value) ? (value[0] ?? '') : (value ?? '')),
    ]),
  );
}

function failure(status: number, name: string): never {
  throw new MockException(status, { code: `DistributedEvents:${name}`, message: name });
}

function records(kind: Kind): Detail[] {
  return kind === 'outbox'
    ? DISTRIBUTED_EVENT_OUTBOX
    : kind === 'inbox'
      ? DISTRIBUTED_EVENT_INBOX
      : DISTRIBUTED_EVENT_REJECTIONS;
}

/** 拒收始终集中在宿主；选库仅缩小查询范围，不模拟租户身份。 */
function scope(req: MockRequest, kind?: Kind, manage = false) {
  requirePermission(
    manage ? PERMISSIONS.distributedEvents.manage : PERMISSIONS.distributedEvents.default,
  );
  const tenant = getMockSessionTenantKey();
  const database = queryValues(req)['database'];
  if (tenant !== 'host' && (database || kind === 'rejections')) failure(403, 'HostOnly');
  const selected = DISTRIBUTED_EVENT_DATABASES.find((item) => item.key === (database || 'host'));
  if (!selected) failure(404, 'DatabaseNotFound');
  return (row: Row) => {
    if (kind === 'rejections') return true;
    const tenantId = 'tenantId' in row ? row.tenantId : null;
    return tenant !== 'host'
      ? tenantId === tenant
      : selected.isHost || selected.tenantIds.includes(tenantId ?? '');
  };
}

function timestamp(row: Row) {
  return 'creationTime' in row ? row.creationTime : row.receivedAt;
}

function matches(row: Row, filter: Filter, headers: Record<string, string> = {}) {
  const keyword = String(filter['keyword'] || '');
  const parent = headers['traceparent']?.match(/^00-([0-9a-f]{32})-([0-9a-f]{16})-[0-9a-f]{2}$/);
  const traceId = parent && !/^0+$/.test(parent[1]) && !/^0+$/.test(parent[2]) ? parent[1] : null;
  const get = (key: string) => filter[key] || undefined;
  return (
    (!get('before') || Date.parse(timestamp(row)) <= Date.parse(String(get('before')))) &&
    (!get('status') || row.status === get('status')) &&
    (!get('eventName') || row.eventName === get('eventName')) &&
    (!get('keyword') ||
      row.eventId === get('keyword') ||
      ('orderingKey' in row && row.orderingKey === get('keyword')) ||
      (!!traceId && /^[0-9a-f]{32}$/i.test(keyword) && traceId === keyword.toLowerCase()) ||
      headers['leistd-correlation-id'] === keyword) &&
    (!get('errorCode') || ('lastErrorCode' in row && row.lastErrorCode === get('errorCode'))) &&
    (!get('consumer') || ('consumer' in row && row.consumer === get('consumer'))) &&
    (!get('reason') || ('reason' in row && row.reason === get('reason')))
  );
}

function list(kind: Kind, req: MockRequest) {
  const allowed = scope(req, kind);
  const statuses =
    kind === 'outbox'
      ? ['pending', 'sent', 'deadLettered']
      : kind === 'inbox'
        ? ['pending', 'processed', 'failed']
        : ['pending', 'redecoded', 'discarded'];
  if (queryValues(req)['status'] && !statuses.includes(queryValues(req)['status']))
    throw new MockException(400);
  const items = records(kind)
    .filter((item) => allowed(item.record) && matches(item.record, queryValues(req), item.headers))
    .map((item) => item.record)
    .sort((a, b) => (BigInt(a.id) > BigInt(b.id) ? -1 : 1));
  const offset = Math.max(0, Number(queryValues(req)['offset']) || 0);
  const limit = Math.min(1000, Math.max(1, Number(queryValues(req)['limit']) || 10));
  return { totalCount: items.length, items: items.slice(offset, offset + limit) };
}

function detail(kind: Kind, req: MockRequest, payload?: false): Detail;
function detail(
  kind: 'outbox' | 'inbox',
  req: MockRequest,
  payload: true,
): Dto.DistributedEventPayloadOutputDto;
function detail(
  kind: 'rejections',
  req: MockRequest,
  payload: true,
): Dto.DistributedEventRawMessageOutputDto;
function detail(kind: Kind, req: MockRequest, payload = false) {
  const allowed = scope(req, kind, payload);
  const item = records(kind).find(
    (value) => value.record.id === req.params['id'] && allowed(value.record),
  );
  if (!item) failure(404, 'RecordNotFound');
  if (!payload) return structuredClone(item);
  return kind === 'rejections'
    ? DISTRIBUTED_EVENT_RAW_MESSAGES[`rejections:${item.record.id}`]
    : DISTRIBUTED_EVENT_PAYLOADS[`${kind}:${item.record.id}`];
}

function status(req: MockRequest): Dto.DistributedEventStatusOutputDto {
  const allowed = scope(req);
  const outbox = DISTRIBUTED_EVENT_OUTBOX.map((item) => item.record).filter(allowed);
  const inbox = DISTRIBUTED_EVENT_INBOX.map((item) => item.record).filter(allowed);
  const pending = outbox.filter((item) => item.status === 'pending');
  const recent = (time?: string | null) => !!time && Date.parse(time) >= Date.now() - 86400000;
  return {
    pendingOutbox: pending.length,
    oldestPendingOutboxAgeSeconds: pending.length
      ? Math.max(...pending.map((item) => (Date.now() - Date.parse(item.creationTime)) / 1000))
      : null,
    deadLettered: outbox.filter((item) => item.status === 'deadLettered').length,
    pendingInbox: inbox.filter((item) => item.status === 'pending').length,
    failedInbox: inbox.filter((item) => item.status === 'failed').length,
    pendingRejections:
      getMockSessionTenantKey() === 'host'
        ? DISTRIBUTED_EVENT_REJECTIONS.filter((item) => item.record.status === 'pending').length
        : null,
    sentLast24Hours: outbox.filter((item) => recent(item.sentAt)).length,
    processedLast24Hours: inbox.filter((item) => recent(item.processedAt)).length,
    transportName: 'RabbitMQ',
    transportStatus: 'healthy',
    transportStatusChangedAt: new Date(Date.now() - 3600000).toISOString(),
    transportReportedAt: new Date().toISOString(),
    payloadEnabled: true,
    testEventEnabled: true,
  };
}

function catalog(req: MockRequest): Dto.DistributedEventCatalogOutputDto[] {
  const allowed = scope(req);
  const outbox = DISTRIBUTED_EVENT_OUTBOX.map((item) => item.record).filter(allowed);
  const inbox = DISTRIBUTED_EVENT_INBOX.map((item) => item.record).filter(allowed);
  const latest = (values: (string | null | undefined)[]) =>
    values
      .filter((value): value is string => !!value)
      .sort()
      .at(-1) ?? null;
  return [...new Set([...outbox, ...inbox].map((item) => item.eventName))]
    .sort()
    .map((eventName) => {
      const outgoing = outbox.filter((item) => item.eventName === eventName);
      const incoming = inbox.filter((item) => item.eventName === eventName);
      return {
        eventName,
        outgoing: outgoing.length > 0,
        incoming: incoming.length > 0,
        consumers: [...new Set(incoming.map((item) => item.consumer))],
        lastSentAt: latest(outgoing.map((item) => item.sentAt)),
        lastReceivedAt: latest(incoming.map((item) => item.receivedAt)),
        lastProcessedAt: latest(incoming.map((item) => item.processedAt)),
        pendingOutbox: outgoing.filter((item) => item.status === 'pending').length,
        pendingInbox: incoming.filter((item) => item.status === 'pending').length,
        deadLettered: outgoing.filter((item) => item.status === 'deadLettered').length,
        failedInbox: incoming.filter((item) => item.status === 'failed').length,
        lastErrorCode: [...outgoing, ...incoming].find((item) => item.lastErrorCode)?.lastErrorCode,
      };
    });
}

function recover(
  kind: Kind,
  action: 'redeliver' | 'replay' | 'redecode' | 'discard',
  req: MockRequest,
): Dto.DistributedEventRecoveryOutputDto {
  const allowed = scope(req, kind, true);
  const { ids, filter } = req.body as { ids?: string[]; filter?: Filter };
  if (
    !!ids === !!filter ||
    (ids &&
      (!Array.isArray(ids) ||
        !ids.length ||
        ids.length > 100 ||
        ids.some((id) => !/^\d+$/.test(id)))) ||
    (filter &&
      (!filter['before'] ||
        !Number.isFinite(Date.parse(String(filter['before']))) ||
        (kind === 'inbox' && !['failed', 'processed'].includes(String(filter['status'])))))
  )
    throw new MockException(400, {
      message: 'Specify IDs (1–100) or a filter with a valid cutoff.',
    });
  const eligible = (row: Row) =>
    kind === 'outbox'
      ? row.status === 'deadLettered'
      : kind === 'inbox'
        ? ['failed', 'processed'].includes(row.status)
        : row.status === 'pending';
  const targets = ids
    ? [...new Set(ids)]
    : records(kind)
        .filter(
          (item) =>
            allowed(item.record) &&
            eligible(item.record) &&
            matches(item.record, filter!, item.headers),
        )
        .map((item) => item.record.id);
  const result: Dto.DistributedEventRecoveryOutputDto = { affectedCount: 0, skipped: [] };
  for (const id of targets) {
    const row = records(kind).find((item) => item.record.id === id && allowed(item.record))?.record;
    if (!row || !eligible(row)) {
      result.skipped.push({ id, reasonCode: row ? 'invalid-state' : 'not-found' });
      continue;
    }
    const now = new Date().toISOString();
    if ('redeliverCount' in row) {
      Object.assign(row, {
        status: 'pending',
        redeliverCount: row.redeliverCount + 1,
        firstFailureAt: null,
        nextAttemptAt: now,
        sentAt: null,
        lastError: null,
        lastErrorCode: null,
      });
    } else if ('replayCount' in row) {
      Object.assign(row, {
        status: 'pending',
        replayCount: row.replayCount + 1,
        attemptCount: 0,
        processedAt: null,
        nextAttemptAt: now,
        lastError: null,
        lastErrorCode: null,
      });
    } else if (action === 'discard') {
      Object.assign(row, { status: 'discarded', resolvedAt: now });
    } else {
      row.redecodeCount += 1;
      // 样本保留损坏的原始字节；重试不能把无法解码的消息伪装成成功。
      row.lastModificationTime = now;
      result.skipped.push({ id, reasonCode: 'redecode-failed' });
      continue;
    }
    row.lastModificationTime = now;
    result.affectedCount += 1;
  }
  return result;
}

function sendTest(req: MockRequest): Dto.DistributedEventTestOutputDto {
  scope(req, undefined, true);
  const tenant = getMockSessionTenantKey();
  if (tenant === 'host' && queryValues(req)['database'] && queryValues(req)['database'] !== 'host')
    failure(403, 'HostOnly');
  const eventId = crypto.randomUUID();
  const now = new Date().toISOString();
  const nextId = (kind: Kind) =>
    String(
      records(kind).reduce(
        (max, item) => (BigInt(item.record.id) > max ? BigInt(item.record.id) : max),
        0n,
      ) + 1n,
    );
  const common = {
    eventId,
    eventName: Dto.DISTRIBUTED_EVENT_TEST.name,
    tenantId: tenant === 'host' ? null : tenant,
    occurredAt: now,
    attemptCount: 1,
    lastModificationTime: now,
  };
  const outboxId = nextId('outbox');
  const inboxId = nextId('inbox');
  DISTRIBUTED_EVENT_OUTBOX.unshift({
    record: {
      ...common,
      id: outboxId,
      status: 'sent',
      redeliverCount: 0,
      creationTime: now,
      sentAt: now,
    },
    headers: {},
  });
  DISTRIBUTED_EVENT_INBOX.unshift({
    record: {
      ...common,
      id: inboxId,
      status: 'processed',
      consumer: Dto.DISTRIBUTED_EVENT_TEST.consumerName,
      duplicateCount: 0,
      replayCount: 0,
      receivedAt: now,
      processedAt: now,
    },
    headers: {},
    coordinates: { queue: 'application' },
  });
  const payload = { payload: JSON.stringify({ eventId, occurredOn: now }) };
  DISTRIBUTED_EVENT_PAYLOADS[`outbox:${outboxId}`] = payload;
  DISTRIBUTED_EVENT_PAYLOADS[`inbox:${inboxId}`] = payload;
  return { eventId };
}

export const DISTRIBUTED_EVENT_API = {
  'GET /api/v1/distributed-events/status': status,
  'GET /api/v1/distributed-events/databases': (req: MockRequest) => {
    scope(req);
    if (getMockSessionTenantKey() !== 'host') failure(403, 'HostOnly');
    return DISTRIBUTED_EVENT_DATABASES;
  },
  'GET /api/v1/distributed-events/catalog': catalog,
  'GET /api/v1/distributed-events/outbox': (req: MockRequest) => list('outbox', req),
  'GET /api/v1/distributed-events/inbox': (req: MockRequest) => list('inbox', req),
  'GET /api/v1/distributed-events/rejections': (req: MockRequest) => list('rejections', req),
  'GET /api/v1/distributed-events/outbox/:id': (req: MockRequest) => detail('outbox', req),
  'GET /api/v1/distributed-events/inbox/:id': (req: MockRequest) => detail('inbox', req),
  'GET /api/v1/distributed-events/rejections/:id': (req: MockRequest) => detail('rejections', req),
  'GET /api/v1/distributed-events/outbox/:id/payload': (req: MockRequest) =>
    detail('outbox', req, true),
  'GET /api/v1/distributed-events/inbox/:id/payload': (req: MockRequest) =>
    detail('inbox', req, true),
  'GET /api/v1/distributed-events/rejections/:id/payload': (req: MockRequest) =>
    detail('rejections', req, true),
  'POST /api/v1/distributed-events/outbox/redeliver': (req: MockRequest) =>
    recover('outbox', 'redeliver', req),
  'POST /api/v1/distributed-events/inbox/replay': (req: MockRequest) =>
    recover('inbox', 'replay', req),
  'POST /api/v1/distributed-events/rejections/redecode': (req: MockRequest) =>
    recover('rejections', 'redecode', req),
  'POST /api/v1/distributed-events/rejections/discard': (req: MockRequest) =>
    recover('rejections', 'discard', req),
  'POST /api/v1/distributed-events/test': sendTest,
};
