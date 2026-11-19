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

/** 待重新解码仍属未解决拒收，可由管理员主动丢弃。 */
export function canDiscard(record: EventRecord): boolean {
  return 'redecodeCount' in record && ['pending', 'redecodePending'].includes(record.status);
}

/** 可勾选：至少有一种恢复动作适用；拒收待处理两者皆可，待重新解码只能丢弃。 */
export function canSelect(record: EventRecord): boolean {
  return canRecover(record) || canDiscard(record);
}

export type StateBadgeVariant = 'warning' | 'success' | 'destructive' | 'secondary';

/** 界面状态：发件箱、收件箱的 Pending 有过尝试即显示重试中；拒收的次数是重新解码次数，不参与。 */
export function displayState(view: EventView, status: string, attempts: number): string {
  return status === 'pending' && attempts > 0 && view !== 'rejections' ? 'retrying' : status;
}

/** 状态文案键：发件箱的 Pending 与指标卡同叫"待发送"，其余视图沿用共用文案。 */
export function stateLabelKey(view: EventView, state: string): string {
  return `distributedEvents.state.${view === 'outbox' && state === 'pending' ? 'pendingSend' : state}`;
}

/** 徽章变体：进行中为 secondary；拒收的待处理需要人工处理，用 warning。 */
export function stateVariant(view: EventView, state: string): StateBadgeVariant {
  return state === 'pending'
    ? view === 'rejections'
      ? 'warning'
      : 'secondary'
    : (STATE_VARIANTS[state] ?? 'secondary');
}

const STATE_VARIANTS: Record<string, StateBadgeVariant> = {
  retrying: 'warning',
  sent: 'success',
  processed: 'success',
  deadLettered: 'destructive',
  failed: 'destructive',
  redecoded: 'success',
  discarded: 'secondary',
  redecodePending: 'warning',
};

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
  'distributedEvents.rejectionHelp': 'Cannot decode or resolve recipient',
  'distributedEvents.views': 'Event views',
  'distributedEvents.search': 'Exact event ID / ordering key / trace ID / correlation ID',
  'distributedEvents.searchRejections': 'Exact event ID / trace ID / correlation ID',
  'distributedEvents.searchCatalog': 'Search event name',
  'distributedEvents.clear': 'Clear filters',
  'distributedEvents.noResults': 'No matching events',
  'distributedEvents.onlyAnomalies': 'Only anomalies',
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
  'distributedEvents.state.redecodePending': 'Awaiting re-decode',
  'distributedEvents.health.healthy': 'Healthy',
  'distributedEvents.health.degraded': 'Degraded',
  'distributedEvents.health.unhealthy': 'Unhealthy',
  'distributedEvents.health.unknown': 'Unknown',
  'distributedEvents.action.redeliver': 'Redeliver',
  'distributedEvents.action.replay': 'Replay',
  'distributedEvents.action.redecode': 'Re-decode',
  'distributedEvents.action.discard': 'Discard',
  'distributedEvents.column.event': 'Event',
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
  'distributedEvents.reason.unknownTenant': 'Unknown recipient',
  'distributedEvents.reason.invalidPayload': 'Invalid payload',
  'distributedEvents.bulk.selected.redeliver': 'Redeliver selected ({{count}})',
  'distributedEvents.bulk.selected.replay': 'Replay selected ({{count}})',
  'distributedEvents.bulk.selected.redecode': 'Re-decode selected ({{count}})',
  'distributedEvents.bulk.selected.discard': 'Discard selected ({{count}})',
  'distributedEvents.bulk.filter.redeliver': 'Redeliver current filter',
  'distributedEvents.bulk.filter.replay': 'Replay current filter',
  'distributedEvents.bulk.filter.redecode': 'Re-decode current filter',
  'distributedEvents.bulk.filter.discard': 'Discard current filter',
  'distributedEvents.confirmation.cutoff': 'Loaded through',
  'distributedEvents.confirmation.criteria': 'Filters',
  'distributedEvents.confirmation.ids': 'Record IDs',
  'distributedEvents.confirmation.question.redeliver': 'Redeliver {{count}} dead letters?',
  'distributedEvents.confirmation.question.replay': 'Replay {{count}} inbox records?',
  'distributedEvents.confirmation.question.redecode': 'Re-decode {{count}} pending rejections?',
  'distributedEvents.confirmation.question.discard': 'Discard {{count}} unresolved rejections?',
  'distributedEvents.confirmation.selectedQuestion.redeliver':
    'Redeliver {{count}} selected dead letters?',
  'distributedEvents.confirmation.selectedQuestion.replay':
    'Replay {{count}} selected inbox records?',
  'distributedEvents.confirmation.selectedQuestion.redecode':
    'Re-decode {{count}} selected pending rejections?',
  'distributedEvents.confirmation.selectedQuestion.discard':
    'Discard {{count}} selected unresolved rejections?',
  'distributedEvents.confirmation.filterQuestion.redeliver':
    'Redeliver {{count}} dead letters matching the current filter?',
  'distributedEvents.confirmation.filterQuestion.replay':
    'Replay {{count}} inbox records matching the current filter?',
  'distributedEvents.confirmation.filterQuestion.redecode':
    'Re-decode {{count}} pending rejections matching the current filter?',
  'distributedEvents.confirmation.filterQuestion.discard':
    'Discard {{count}} unresolved rejections matching the current filter?',
  'distributedEvents.confirmation.button.redeliver': 'Redeliver {{count}} records',
  'distributedEvents.confirmation.button.replay': 'Replay {{count}} records',
  'distributedEvents.confirmation.button.redecode': 'Re-decode {{count}} records',
  'distributedEvents.confirmation.button.discard': 'Discard {{count}} records',
  'distributedEvents.confirmation.consequence.redeliver':
    'The event ID is preserved and consumers deduplicate by ID. Fix the cause first. Later records are excluded. Records return to pending in batches and the dispatcher resumes delivery.',
  'distributedEvents.confirmation.consequence.redecode':
    'Re-decode with a matching receiver. An API without that transport hands the request to a Worker. Successful messages enter the target inbox; undecodable messages stay pending. Later messages are excluded.',
  'distributedEvents.confirmation.consequence.discard':
    'These raw messages will stop processing and will not reach consumers. Later messages are excluded.',
  'distributedEvents.confirmation.result.redeliver': 'Redelivered {{count}} · Skipped {{skipped}}',
  'distributedEvents.confirmation.result.replay': 'Replayed {{count}} · Skipped {{skipped}}',
  'distributedEvents.confirmation.result.redecode':
    'Re-decoded {{count}} · Handed off {{deferred}} · Skipped {{skipped}}',
  'distributedEvents.confirmation.result.discard': 'Discarded {{count}} · Skipped {{skipped}}',
  'distributedEvents.state.pendingSend': 'Pending send',
  'distributedEvents.field.timeRange': 'Time range',
  'distributedEvents.transport': 'Transport',
  'distributedEvents.clearTimeRange': 'Clear time range',
  'distributedEvents.selectedCount': '{{count}} selected',
  'distributedEvents.clearSelection': 'Clear selection',
  'distributedEvents.selectDisabled.outbox': 'Only dead letters can be redelivered',
  'distributedEvents.selectDisabled.inbox': 'Only failed or processed records can be replayed',
  'distributedEvents.selectDisabled.rejections':
    'Only unresolved rejections can be re-decoded or discarded',
  'distributedEvents.moreActions': 'More actions',
  'distributedEvents.copyRecordId': 'Copy record ID',
  'distributedEvents.recordId': 'Record {{id}}',
  'distributedEvents.relatedRecords': 'Related records',
  'distributedEvents.viewRecords': 'View',
  'distributedEvents.nextStep.redeliver':
    'Check transport connectivity or the failure cause, then redeliver. The event ID is preserved and consumers deduplicate by ID.',
  'distributedEvents.nextStep.replay':
    'Fix the handler failure, then replay. Replay executes the handler again, including any external effects.',
  'distributedEvents.nextStep.redecode':
    'Check the sender format or receiver configuration, then re-decode. Discard it once it cannot be recovered.',
  'distributedEvents.nextStep.redecodePending':
    'Handed off for re-decoding and waiting for the receiver. Discard it if no longer needed.',
};
//#endif
