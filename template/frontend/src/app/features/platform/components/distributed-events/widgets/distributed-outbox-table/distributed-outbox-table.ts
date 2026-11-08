import { ChangeDetectionStrategy, Component } from '@angular/core';

import {
  DistributedEventTable,
  EVENT_TABLE_ICONS,
  EVENT_TABLE_IMPORTS,
} from '../distributed-event-table/distributed-event-table';

/** 发件箱表；复用表格呈现，固定该类记录的列与操作。 */
@Component({
  selector: 'app-distributed-outbox-table',
  imports: EVENT_TABLE_IMPORTS,
  providers: [EVENT_TABLE_ICONS],
  templateUrl: '../distributed-event-table/distributed-event-table.html',
  host: { class: 'flex min-w-0 flex-col gap-6' },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DistributedOutboxTable extends DistributedEventTable {
  protected override view(): 'outbox' {
    return 'outbox';
  }
  protected override columnsForView() {
    return [
      ...this.selectionColumns(),
      this.titleColumn(),
      ...(this.isHost() ? [this.dataColumn('tenant', 'secondary')] : []),
      this.dataColumn('orderingKey', 'secondary'),
      this.statusColumn(),
      this.dataColumn('attempts', 'secondary'),
      this.dataColumn('error', 'secondary'),
      this.dataColumn('next'),
      this.dataColumn('time'),
      this.actionsColumn(),
    ];
  }
}
