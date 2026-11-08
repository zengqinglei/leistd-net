import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';
//#if (IncludeLocalization)
import { TranslocoDirective } from '@jsverse/transloco';
//#endif
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideInfo } from '@ng-icons/lucide';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmTooltipImports } from '@spartan-ng/helm/tooltip';

import { AccountPreferencesService } from '../../../../../../core/services/account-preferences-service';
import { StatPanel } from '../../../../../../shared/components/stat-panel/stat-panel';
import { AppNumber } from '../../../../../../shared/pipes/app-number-pipe';
//#if (!IncludeLocalization)
import { englishText } from '../../../../../../shared/utils/english-text';
//#endif
import { formatElapsedTime } from '../../../../../../shared/utils/relative-time';
import { DistributedEventStatusOutputDto } from '../../../../dtos/distributed-event.dto';
// prettier-ignore
import {
  //#if (!IncludeLocalization)
  ENGLISH,
  //#endif
  EventView,
} from '../../distributed-event-display';

/** 快照卡片与跳转意图；等待时长使用共享时间工具按当前语言格式化。 */
@Component({
  selector: 'app-distributed-event-statistics',
  // prettier-ignore
  imports: [
    NgIcon,
    HlmButton,
    ...HlmTooltipImports,
    StatPanel,
    AppNumber,
    //#if (IncludeLocalization)
    TranslocoDirective,
    //#endif
  ],
  providers: [provideIcons({ lucideInfo })],
  templateUrl: './distributed-event-statistics.html',
  host: { class: 'block min-w-0' },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DistributedEventStatistics {
  readonly status = input<DistributedEventStatusOutputDto | null>(null);
  readonly isHost = input(true);
  readonly navigate = output<{ view: EventView; status: string }>();
  private readonly preferences = inject(AccountPreferencesService);
  protected readonly displayLocale = this.preferences.displayLocale;
  protected readonly oldestAge = computed(() => {
    const seconds = this.status()?.oldestPendingOutboxAgeSeconds;
    return seconds === undefined || seconds === null
      ? '—'
      : formatElapsedTime(seconds, this.displayLocale());
  });
  //#if (!IncludeLocalization)
  protected readonly t = englishText(ENGLISH);
  //#endif
  protected jump(view: EventView, status: string): void {
    this.navigate.emit({ view, status });
  }
}
