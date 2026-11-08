import * as Dto from '../../dtos/distributed-event.dto';

export type EventView = 'outbox' | 'inbox' | 'rejections' | 'catalog';
export type EventRecord =
  | Dto.DistributedEventOutboxOutputDto
  | Dto.DistributedEventInboxOutputDto
  | Dto.DistributedEventRejectionOutputDto;
export type EventDetail =
  | Dto.DistributedEventOutboxDetailOutputDto
  | Dto.DistributedEventInboxDetailOutputDto
  | Dto.DistributedEventRejectionDetailOutputDto;
export interface EventRow {
  id: string;
  eventName: string;
  eventId: string;
  status: string;
  tenant: string;
  orderingKey: string;
  consumer: string;
  attempts: number;
  error: string;
  next: string | null;
  time: string | null;
  record?: EventRecord;
  catalog?: Dto.DistributedEventCatalogOutputDto;
}

export function eventRow(record: EventRecord): EventRow {
  return {
    id: record.id,
    eventName: record.eventName ?? '—',
    eventId: record.eventId ?? '—',
    status: record.status,
    tenant: 'tenantId' in record ? (record.tenantId ?? 'host') : 'host',
    orderingKey: 'orderingKey' in record ? (record.orderingKey ?? '—') : '—',
    consumer: 'consumer' in record ? record.consumer : '—',
    attempts: 'attemptCount' in record ? record.attemptCount : record.redecodeCount,
    error: 'reason' in record ? record.reason : (record.lastErrorCode ?? '—'),
    next: 'nextAttemptAt' in record ? (record.nextAttemptAt ?? null) : null,
    time: 'creationTime' in record ? record.creationTime : record.receivedAt,
    record,
  };
}

export function canRecover(record: EventRecord): boolean {
  return 'redeliverCount' in record
    ? record.status === 'deadLettered'
    : 'replayCount' in record
      ? record.status === 'failed' || record.status === 'processed'
      : record.status === 'pending';
}

export const STATUS_VARIANTS = {
  pending: 'warning',
  sent: 'success',
  processed: 'success',
  deadLettered: 'destructive',
  failed: 'destructive',
  redecoded: 'success',
  discarded: 'secondary',
} as const;

/** 不启用本地化时的界面文案；与 distributed-events/en.json 同步。 */
//#if (!IncludeLocalization)
export const ENGLISH: Record<string, string> = {
  'distributedEvents.title': 'Distributed events',
  'distributedEvents.database': 'Physical database',
  'distributedEvents.hostDatabase': 'Host / default database',
  'distributedEvents.tenantScope': 'Current tenant',
  'distributedEvents.host': 'Host',
  'distributedEvents.noTransport': 'No transport',
  'distributedEvents.last24Hours': 'Last 24h: {{sent}} sent · {{processed}} processed',
  'distributedEvents.refresh': 'Refresh',
  'distributedEvents.sendTest': 'Send test event',
  'distributedEvents.pendingOutbox': 'Pending send',
  'distributedEvents.deadLettered': 'Dead letters',
  'distributedEvents.failedInbox': 'Processing failed',
  'distributedEvents.pendingRejections': 'Pending rejections',
  'distributedEvents.pendingHelp': 'Waiting for transport delivery. Retries remain pending.',
  'distributedEvents.oldest': 'Oldest {{seconds}}s · Pending processing {{count}}',
  'distributedEvents.deadHelp': 'Permanent failure or retry window elapsed',
  'distributedEvents.failedHelp': 'Handler retry attempts exhausted',
  'distributedEvents.rejectionHelp': 'Host inbox · Cannot decode or resolve tenant',
  'distributedEvents.views': 'Event views',
  'distributedEvents.search': 'Exact event ID / ordering key',
  'distributedEvents.searchRejections': 'Exact event ID',
  'distributedEvents.searchCatalog': 'Search event name',
  'distributedEvents.clear': 'Clear filters',
  'distributedEvents.noResults': 'No matching events',
  'distributedEvents.onlyAnomalies': 'Only anomalies',
  'distributedEvents.restoreSelected': 'Recover selected ({{count}})',
  'distributedEvents.restoreFilter': 'Recover current filter',
  'distributedEvents.chooseState':
    'Choose dead letters, failed/processed inbox or pending rejections first.',
  'distributedEvents.retry': 'Retry',
  'distributedEvents.loading': 'Loading events',
  'distributedEvents.emptyHelp':
    'Change the filters or send a test event to inspect the delivery path.',
  'distributedEvents.selectPage': 'Select recoverable records on this page',
  'distributedEvents.selectRecord': 'Select record {{id}}',
  'distributedEvents.actions': 'Actions',
  'distributedEvents.restore': 'Recover',
  'distributedEvents.details': 'View details',
  'distributedEvents.outgoing': 'Outgoing',
  'distributedEvents.incoming': 'Incoming',
  'distributedEvents.results': '{{count}} records',
  'distributedEvents.rowsPerPage': 'Items per page',
  'distributedEvents.page': 'Page {{page}} of {{total}}',
  'distributedEvents.first': 'First page',
  'distributedEvents.previous': 'Previous page',
  'distributedEvents.next': 'Next page',
  'distributedEvents.last': 'Last page',
  'distributedEvents.close': 'Close',
  'distributedEvents.detailHelp':
    'Stored metadata and last result. Payload is available on demand to managers.',
  'distributedEvents.copyId': 'Copy event ID',
  'distributedEvents.overview': 'Overview',
  'distributedEvents.headers': 'Message headers',
  'distributedEvents.coordinates': 'Transport coordinates',
  'distributedEvents.rawBody': 'Raw message (Base64)',
  'distributedEvents.payload': 'Event payload',
  'distributedEvents.showPayload': 'Show payload',
  'distributedEvents.copyPayload': 'Copy payload',
  'distributedEvents.confirm':
    'Recover {{count}} records? Scope: {{scope}}. Filter / IDs: {{filter}}. Loaded through {{before}}. Later records are excluded.',
  'distributedEvents.replayWarning':
    'This will execute the handler again, including any external effects. Confirm that repeating the business operation is appropriate.',
  'distributedEvents.recoveryWarning':
    'The original event ID is preserved. Fix the cause before retrying. Filter recovery runs in batches; the dispatcher resumes processing.',
  'distributedEvents.recovered': 'Recovered {{count}} · Skipped {{skipped}}',
  'distributedEvents.testSent': 'Test event sent: {{id}}',
  'distributedEvents.view.outbox': 'Outbox',
  'distributedEvents.view.inbox': 'Inbox',
  'distributedEvents.view.rejections': 'Rejections',
  'distributedEvents.view.catalog': 'Event catalog',
  'distributedEvents.state.pending': 'Pending',
  'distributedEvents.state.sent': 'Sent',
  'distributedEvents.state.processed': 'Processed',
  'distributedEvents.state.failed': 'Failed',
  'distributedEvents.state.deadLettered': 'Dead letter',
  'distributedEvents.state.retrying': 'Retrying',
  'distributedEvents.state.redecoded': 'Re-decoded',
  'distributedEvents.state.discarded': 'Discarded',
  'distributedEvents.health.healthy': 'Healthy',
  'distributedEvents.health.degraded': 'Degraded',
  'distributedEvents.health.unhealthy': 'Unhealthy',
  'distributedEvents.health.unknown': 'Unknown',
  'distributedEvents.action.redeliver': 'Redeliver',
  'distributedEvents.action.replay': 'Replay handler',
  'distributedEvents.action.redecode': 'Re-decode',
  'distributedEvents.action.discard': 'Discard',
  'distributedEvents.column.event': 'Event',
  'distributedEvents.column.tenant': 'Tenant',
  'distributedEvents.column.orderingKey': 'Ordering key',
  'distributedEvents.column.status': 'Status',
  'distributedEvents.column.consumer': 'Consumer',
  'distributedEvents.column.attempts': 'Attempts',
  'distributedEvents.column.error': 'Last error / reason',
  'distributedEvents.column.next': 'Next attempt',
  'distributedEvents.column.time': 'Created / received',
  'distributedEvents.column.direction': 'Direction',
  'distributedEvents.column.lastSent': 'Last sent',
  'distributedEvents.column.lastReceived': 'Last received',
  'distributedEvents.column.lastProcessed': 'Last processed',
  'distributedEvents.column.pending': 'Pending',
  'distributedEvents.column.anomalies': 'Anomalies',
  'distributedEvents.field.id': 'Record ID',
  'distributedEvents.field.eventId': 'Event ID',
  'distributedEvents.field.eventName': 'Event name',
  'distributedEvents.field.tenantId': 'Tenant ID',
  'distributedEvents.field.orderingKey': 'Ordering key',
  'distributedEvents.field.occurredAt': 'Occurred at',
  'distributedEvents.field.status': 'Status',
  'distributedEvents.field.attemptCount': 'Attempts',
  'distributedEvents.field.nextAttemptAt': 'Next attempt',
  'distributedEvents.field.lastErrorCode': 'Last error code',
  'distributedEvents.field.errorCode': 'Error code',
  'distributedEvents.field.lastError': 'Last error',
  'distributedEvents.field.lastModificationTime': 'Last changed',
  'distributedEvents.field.redeliverCount': 'Manual redeliveries',
  'distributedEvents.field.creationTime': 'Created at',
  'distributedEvents.field.firstFailureAt': 'First failure',
  'distributedEvents.field.sentAt': 'Sent at',
  'distributedEvents.field.consumer': 'Consumer',
  'distributedEvents.field.duplicateCount': 'Duplicates',
  'distributedEvents.field.replayCount': 'Replays',
  'distributedEvents.field.receivedAt': 'Received at',
  'distributedEvents.field.processedAt': 'Processed at',
  'distributedEvents.field.transport': 'Transport',
  'distributedEvents.field.reason': 'Reason code',
  'distributedEvents.field.error': 'Error',
  'distributedEvents.field.redecodeCount': 'Re-decode attempts',
  'distributedEvents.field.resolvedAt': 'Resolved at',
  'distributedEvents.field.outgoing': 'Outgoing',
  'distributedEvents.field.incoming': 'Incoming',
  'distributedEvents.field.consumers': 'Consumers',
  'distributedEvents.field.lastSentAt': 'Last sent',
  'distributedEvents.field.lastReceivedAt': 'Last received',
  'distributedEvents.field.lastProcessedAt': 'Last processed',
  'distributedEvents.field.pendingOutbox': 'Pending send',
  'distributedEvents.field.pendingInbox': 'Pending processing',
  'distributedEvents.field.deadLettered': 'Dead letters',
  'distributedEvents.field.failedInbox': 'Processing failed',
};
//#endif
