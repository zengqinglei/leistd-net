import { HttpHeaders, HttpRequest } from '@angular/common/http';

import { DISTRIBUTED_EVENT_API } from './distributed-event';
import { PERMISSIONS } from '../../src/app/shared/constants/permission.constants';
import { MockException, MockRequest } from '../core/models';
import { PERMISSION_GRANTS } from '../data/authorization';
import {
  DISTRIBUTED_EVENT_OUTBOX,
  DISTRIBUTED_EVENT_INBOX,
  DISTRIBUTED_EVENT_REJECTIONS,
  DISTRIBUTED_EVENT_PAYLOADS,
  DISTRIBUTED_EVENT_TENANTS,
} from '../data/distributed-events';
//#if (IncludeMultiTenancy)
import { setMockSessionUserId, setMockSessionTenantKey } from '../utils/current-user';
//#else
import { setMockSessionUserId } from '../utils/current-user';
//#endif

type Api = typeof DISTRIBUTED_EVENT_API;
function call<K extends keyof Api>(
  key: K,
  overrides: Partial<MockRequest> = {},
): ReturnType<Api[K]> {
  const request: MockRequest = {
    original: new HttpRequest('GET', '/api/v1/distributed-events'),
    url: '/api/v1/distributed-events',
    queryParams: {},
    headers: new HttpHeaders(),
    body: {},
    params: {},
    ...overrides,
  };
  return (DISTRIBUTED_EVENT_API[key] as (req: MockRequest) => unknown)(request) as ReturnType<
    Api[K]
  >;
}

/** Mock 的恢复边界与权限行为；真实分批事务和审计由后端用例验证。 */
describe('distributed event mock', () => {
  let outbox: typeof DISTRIBUTED_EVENT_OUTBOX;
  let inbox: typeof DISTRIBUTED_EVENT_INBOX;
  let rejections: typeof DISTRIBUTED_EVENT_REJECTIONS;
  let payloads: typeof DISTRIBUTED_EVENT_PAYLOADS;
  let grants: string[];

  beforeEach(() => {
    outbox = structuredClone(DISTRIBUTED_EVENT_OUTBOX);
    inbox = structuredClone(DISTRIBUTED_EVENT_INBOX);
    rejections = structuredClone(DISTRIBUTED_EVENT_REJECTIONS);
    payloads = structuredClone(DISTRIBUTED_EVENT_PAYLOADS);
    grants = [...PERMISSION_GRANTS['Role/role_member'].permissionNames];
    setMockSessionUserId('user_admin');
  });
  afterEach(() => {
    DISTRIBUTED_EVENT_OUTBOX.splice(0, DISTRIBUTED_EVENT_OUTBOX.length, ...outbox);
    DISTRIBUTED_EVENT_INBOX.splice(0, DISTRIBUTED_EVENT_INBOX.length, ...inbox);
    DISTRIBUTED_EVENT_REJECTIONS.splice(0, DISTRIBUTED_EVENT_REJECTIONS.length, ...rejections);
    for (const key of Object.keys(DISTRIBUTED_EVENT_PAYLOADS))
      delete DISTRIBUTED_EVENT_PAYLOADS[key];
    Object.assign(DISTRIBUTED_EVENT_PAYLOADS, payloads);
    PERMISSION_GRANTS['Role/role_member'].permissionNames = grants;
    setMockSessionUserId(null);
  });

  it('publishes a test with matching sent and processed records', () => {
    const result = call('POST /api/v1/distributed-events/test');
    const queryParams = { keyword: result.eventId };
    const sent = call('GET /api/v1/distributed-events/outbox', { queryParams }).items;
    const processed = call('GET /api/v1/distributed-events/inbox', { queryParams }).items;
    expect(sent).toHaveLength(1);
    expect(processed).toHaveLength(1);
    expect(sent[0].status).toBe('sent');
    expect(processed[0].status).toBe('processed');
  });

  it('reports payload availability and both transport timestamps', () => {
    const status = call('GET /api/v1/distributed-events/status');
    expect(status.payloadEnabled).toBe(true);
    expect(Number.isFinite(Date.parse(status.transportStatusChangedAt!))).toBe(true);
    expect(Number.isFinite(Date.parse(status.transportReportedAt!))).toBe(true);
  });

  it.each(['outbox', 'inbox', 'rejections'] as const)(
    'finds %s by trace or distinct correlation ID without partial matching',
    (kind) => {
      const route = `GET /api/v1/distributed-events/${kind}` as const;
      for (const keyword of ['4BF92F3577B34DA6A3CE929D0E0E4736', 'Caller_ABC-request-27'])
        expect(call(route, { queryParams: { keyword } }).totalCount).toBeGreaterThan(0);
      expect(call(route, { queryParams: { keyword: 'Caller_ABC-request' } }).totalCount).toBe(0);
      expect(call(route, { queryParams: { keyword: 'caller_abc-request-27' } }).totalCount).toBe(0);
    },
  );

  it('restores more than one thousand matching dead letters without touching later records', () => {
    const template = DISTRIBUTED_EVENT_OUTBOX[0];
    for (let index = 0; index < 1200; index++)
      DISTRIBUTED_EVENT_OUTBOX.push({
        ...structuredClone(template),
        record: {
          ...template.record,
          id: String(100 + index),
          creationTime: '2025-01-01T00:00:00Z',
        },
      });
    const originalId = template.record.eventId;
    const result = call('POST /api/v1/distributed-events/outbox/redeliver', {
      body: { filter: { before: '2025-01-02T00:00:00Z', eventName: template.record.eventName } },
    });
    expect(result.affectedCount).toBe(1200);
    expect(
      DISTRIBUTED_EVENT_OUTBOX.filter((item) => BigInt(item.record.id) >= 100n).every(
        (item) =>
          item.record.status === 'pending' &&
          item.record.eventId === originalId &&
          item.record.attemptCount === template.record.attemptCount,
      ),
    ).toBe(true);
    expect(template.record.status).toBe('deadLettered');
  });

  it('reports missing and ineligible IDs while preserving outbox attempt totals', () => {
    const original = DISTRIBUTED_EVENT_OUTBOX[0].record;
    const attempts = original.attemptCount;
    const result = call('POST /api/v1/distributed-events/outbox/redeliver', {
      body: { ids: [original.id, '3', '999999999999999999'] },
    });
    expect(result.affectedCount).toBe(1);
    expect(result.skipped).toEqual([
      { id: '3', reasonCode: 'invalid-state' },
      { id: '999999999999999999', reasonCode: 'not-found' },
    ]);
    expect(original.attemptCount).toBe(attempts);
    expect(original.firstFailureAt).toBeNull();
  });

  it('rejects both selectors, missing cutoff and oversized ID sets', () => {
    for (const body of [
      { ids: ['1'], filter: { before: new Date().toISOString() } },
      { filter: {} },
      { ids: Array.from({ length: 101 }, (_, index) => String(index)) },
    ]) {
      expect(() => call('POST /api/v1/distributed-events/outbox/redeliver', { body })).toThrow(
        MockException,
      );
    }
  });

  it('resets inbox attempt count for a new replay round', () => {
    const row = DISTRIBUTED_EVENT_INBOX[0].record;
    call('POST /api/v1/distributed-events/inbox/replay', { body: { ids: [row.id] } });
    expect(row.attemptCount).toBe(0);
    expect(row.replayCount).toBe(1);
    expect(row.status).toBe('pending');
    expect(row.lastErrorCode).toBeNull();
  });

  it('keeps undecodable raw messages pending until discarded', () => {
    const row = DISTRIBUTED_EVENT_REJECTIONS[0].record;
    expect(
      call('POST /api/v1/distributed-events/rejections/redecode', { body: { ids: [row.id] } })
        .skipped[0].reasonCode,
    ).toBe('redecode-failed');
    expect(row.status).toBe('pending');
    expect(
      call('POST /api/v1/distributed-events/rejections/discard', { body: { ids: [row.id] } })
        .affectedCount,
    ).toBe(1);
    expect(row.status).toBe('discarded');
  });

  it('allows read-only metadata while denying payload and writes', () => {
    PERMISSION_GRANTS['Role/role_member'].permissionNames = [PERMISSIONS.distributedEvents.default];
    setMockSessionUserId('user_demo');
    const detail = call('GET /api/v1/distributed-events/outbox/:id', { params: { id: '1' } });
    expect(detail).not.toHaveProperty('payload');
    expect(detail.record).not.toHaveProperty('payload');
    expect(() =>
      call('GET /api/v1/distributed-events/outbox/:id/payload', { params: { id: '1' } }),
    ).toThrow(MockException);
    expect(() => call('POST /api/v1/distributed-events/test')).toThrow(MockException);
  });

  //#if (IncludeMultiTenancy)
  it('restricts tenant data and denies all host-only entry points', () => {
    setMockSessionTenantKey(DISTRIBUTED_EVENT_TENANTS[0]);
    const rows = call('GET /api/v1/distributed-events/outbox').items;
    expect(rows.length).toBeGreaterThan(0);
    expect(
      rows.every((row) => 'tenantId' in row && row.tenantId === DISTRIBUTED_EVENT_TENANTS[0]),
    ).toBe(true);
    expect(call('GET /api/v1/distributed-events/status').pendingRejections).toBeNull();
    expect(() => call('GET /api/v1/distributed-events/databases')).toThrow(MockException);
    expect(() => call('GET /api/v1/distributed-events/rejections')).toThrow(MockException);
    expect(() =>
      call('GET /api/v1/distributed-events/outbox', { queryParams: { database: 'host' } }),
    ).toThrow(MockException);
  });

  //#endif
  it('accepts the query arrays emitted by the real Mock interceptor', () => {
    const result = call('GET /api/v1/distributed-events/outbox', {
      queryParams: { database: ['host'], status: ['deadLettered'], limit: ['1'], offset: ['0'] },
    });
    expect(result.totalCount).toBe(2);
    expect(result.items).toHaveLength(1);
    expect(result.items[0].status).toBe('deadLettered');
  });

  it('does not publish a host event into a selected tenant database', () => {
    expect(() =>
      call('POST /api/v1/distributed-events/test', {
        queryParams: { database: DISTRIBUTED_EVENT_TENANTS[0] },
      }),
    ).toThrow(MockException);
  });
});
