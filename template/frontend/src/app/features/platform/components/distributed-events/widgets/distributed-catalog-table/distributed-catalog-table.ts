import { ChangeDetectionStrategy, Component } from '@angular/core';

import {
  DistributedEventTable,
  EVENT_TABLE_ICONS,
  EVENT_TABLE_IMPORTS,
} from '../distributed-event-table/distributed-event-table';

/** 事件目录表；复用表格呈现，固定该类记录的列与操作。 */
@Component({
  selector: 'app-distributed-catalog-table',
  imports: EVENT_TABLE_IMPORTS,
  providers: [EVENT_TABLE_ICONS],
  templateUrl: '../distributed-event-table/distributed-event-table.html',
  host: { class: 'flex min-w-0 flex-col gap-6' },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DistributedCatalogTable extends DistributedEventTable {
  protected override view(): 'catalog' {
    return 'catalog';
  }
  protected override columnsForView() {
    return [
      this.titleColumn(),
      this.dataColumn('direction', 'secondary'),
      this.dataColumn('consumer', 'secondary'),
      this.dataColumn('lastSent'),
      this.dataColumn('lastReceivedProcessed'),
      this.dataColumn('pending', 'secondary'),
      this.dataColumn('anomalies', 'primary'),
      this.dataColumn('error'),
      this.actionsColumn(),
    ];
  }
}
