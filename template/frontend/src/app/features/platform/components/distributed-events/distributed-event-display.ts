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
    eventName:
      record.eventName ??
      ('coordinates' in record
        ? record.coordinates['queue'] ||
          record.coordinates['topic'] ||
          record.coordinates['routingKey'] ||
          record.transport
        : '—'),
    eventId:
      record.eventId ??
      ('coordinates' in record
        ? record.coordinates['messageId'] || record.coordinates['offset'] || record.id
        : '—'),
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

/** 运维动作的显示名与记录类型保持一致。 */
export function recoveryAction(view: EventView): 'redeliver' | 'replay' | 'redecode' {
  return view === 'outbox' ? 'redeliver' : view === 'inbox' ? 'replay' : 'redecode';
}
/** 框架已知拒收原因有译文，第三方传输自定义码保留原值。 */
export function reasonKey(reason: string): string | null {
  return (
    (
      {
        'invalid-envelope': 'distributedEvents.reason.invalidEnvelope',
        'unknown-tenant': 'distributedEvents.reason.unknownTenant',
        'invalid-payload': 'distributedEvents.reason.invalidPayload',
      } as Record<string, string>
    )[reason] ?? null
  );
}

/** 不含本地化时的界面文案，与 en.json 同步。 */
//#if (!IncludeLocalization)
export const ENGLISH: Record<string, string> = {
  'distributedEvents.title': 'Distributed events',
  'distributedEvents.database': 'Physical database',
  'distributedEvents.hostDatabase': 'Host / default database',
  'distributedEvents.tenantScope': 'Current tenant',
  'distributedEvents.host': 'Host',
  'distributedEvents.noTransport': 'No transport',
  'distributedEvents.transportChanged': 'Status changed',
  'distributedEvents.transportReported': 'Last reported',
  'distributedEvents.last24Hours': 'Last 24h: {{sent}} sent · {{processed}} processed',
  'distributedEvents.refresh': 'Refresh',
  'distributedEvents.sendTest': 'Send test event',
  'distributedEvents.pendingOutbox': 'Pending send',
  'distributedEvents.deadLettered': 'Dead letters',
  'distributedEvents.failedInbox': 'Processing failed',
  'distributedEvents.pendingRejections': 'Pending rejections',
  'distributedEvents.pendingHelp': 'Waiting for transport delivery. Retries remain pending.',
  'distributedEvents.oldest': 'Oldest {{age}} · Pending processing {{count}}',
  'distributedEvents.deadHelp': 'Permanent failure or retry window elapsed',
  'distributedEvents.failedHelp': 'Handler retry attempts exhausted',
  'distributedEvents.rejectionHelp': 'Host inbox · Cannot decode or resolve tenant',
  'distributedEvents.views': 'Event views',
  'distributedEvents.search': 'Exact event ID / ordering key / trace ID / correlation ID',
  'distributedEvents.searchRejections': 'Exact event ID / trace ID / correlation ID',
  'distributedEvents.searchCatalog': 'Search event name',
  'distributedEvents.clear': 'Clear filters',
  'distributedEvents.noResults': 'No matching events',
  'distributedEvents.onlyAnomalies': 'Only anomalies',
  'distributedEvents.chooseState':
    'Choose dead letters, failed/processed inbox or pending rejections first.',
  'distributedEvents.retry': 'Retry',
  'distributedEvents.loading': 'Loading events',
  'distributedEvents.emptyHelp':
    'Change the filters or send a test event to inspect the delivery path.',
  'distributedEvents.selectPage': 'Select recoverable records on this page',
  'distributedEvents.selectRecord': 'Select record {{id}}',
  'distributedEvents.actions': 'Actions',
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
  'distributedEvents.replayWarning':
    'This will execute the handler again, including any external effects. Confirm that repeating the business operation is appropriate.',
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
  'distributedEvents.action.replay': 'Replay',
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
  'distributedEvents.column.lastReceivedProcessed': 'Last received / processed',
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
  'distributedEvents.field.direction': 'Direction',
  'distributedEvents.cancel': 'Cancel',
  'distributedEvents.reason.invalidEnvelope': 'Invalid envelope',
  'distributedEvents.reason.unknownTenant': 'Unknown tenant',
  'distributedEvents.reason.invalidPayload': 'Invalid payload',
  'distributedEvents.bulk.selected.redeliver': 'Redeliver selected ({{count}})',
  'distributedEvents.bulk.selected.replay': 'Replay selected ({{count}})',
  'distributedEvents.bulk.selected.redecode': 'Re-decode selected ({{count}})',
  'distributedEvents.bulk.selected.discard': 'Discard selected ({{count}})',
  'distributedEvents.bulk.filter.redeliver': 'Redeliver current filter',
  'distributedEvents.bulk.filter.replay': 'Replay current filter',
  'distributedEvents.bulk.filter.redecode': 'Re-decode current filter',
  'distributedEvents.bulk.filter.discard': 'Discard current filter',
  'distributedEvents.confirmation.scope': 'Scope',
  'distributedEvents.confirmation.cutoff': 'Loaded through',
  'distributedEvents.confirmation.criteria': 'Filters',
  'distributedEvents.confirmation.ids': 'Record IDs',
  'distributedEvents.confirmation.question.redeliver': 'Redeliver {{count}} dead letters?',
  'distributedEvents.confirmation.question.replay': 'Replay {{count}} inbox records?',
  'distributedEvents.confirmation.question.redecode': 'Re-decode {{count}} pending rejections?',
  'distributedEvents.confirmation.question.discard': 'Discard {{count}} pending rejections?',
  'distributedEvents.confirmation.selectedQuestion.redeliver':
    'Redeliver {{count}} selected dead letters?',
  'distributedEvents.confirmation.selectedQuestion.replay':
    'Replay {{count}} selected inbox records?',
  'distributedEvents.confirmation.selectedQuestion.redecode':
    'Re-decode {{count}} selected pending rejections?',
  'distributedEvents.confirmation.selectedQuestion.discard':
    'Discard {{count}} selected pending rejections?',
  'distributedEvents.confirmation.filterQuestion.redeliver':
    'Redeliver {{count}} dead letters matching the current filter?',
  'distributedEvents.confirmation.filterQuestion.replay':
    'Replay {{count}} inbox records matching the current filter?',
  'distributedEvents.confirmation.filterQuestion.redecode':
    'Re-decode {{count}} pending rejections matching the current filter?',
  'distributedEvents.confirmation.filterQuestion.discard':
    'Discard {{count}} pending rejections matching the current filter?',
  'distributedEvents.confirmation.button.redeliver': 'Redeliver {{count}} records',
  'distributedEvents.confirmation.button.replay': 'Replay {{count}} records',
  'distributedEvents.confirmation.button.redecode': 'Re-decode {{count}} records',
  'distributedEvents.confirmation.button.discard': 'Discard {{count}} records',
  'distributedEvents.confirmation.consequence.redeliver':
    'The event ID is preserved and consumers deduplicate by ID. Fix the cause first. Later records are excluded. Records return to pending in batches and the dispatcher resumes delivery.',
  'distributedEvents.confirmation.consequence.redecode':
    'Re-decode with the current transport. Successful messages enter the target tenant inbox; undecodable messages stay pending. Later messages are excluded.',
  'distributedEvents.confirmation.consequence.discard':
    'These raw messages will stop processing and will not reach consumers. Later messages are excluded.',
  'distributedEvents.confirmation.result.redeliver': 'Redelivered {{count}} · Skipped {{skipped}}',
  'distributedEvents.confirmation.result.replay': 'Replayed {{count}} · Skipped {{skipped}}',
  'distributedEvents.confirmation.result.redecode': 'Re-decoded {{count}} · Skipped {{skipped}}',
  'distributedEvents.confirmation.result.discard': 'Discarded {{count}} · Skipped {{skipped}}',
};
//#endif
