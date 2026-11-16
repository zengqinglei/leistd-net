//#if (TenantWorkspace)
import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterOutlet } from '@angular/router';

@Component({
  selector: 'app-tenant-layout',
  imports: [RouterOutlet],
  template: `@for (name of [name()]; track name) {
    <router-outlet />
  }`,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TenantLayout {
  private readonly route = inject(ActivatedRoute);
  private readonly params = toSignal(this.route.paramMap, {
    initialValue: this.route.snapshot.paramMap,
  });
  readonly name = computed(() => this.params().get('workspace'));
}
//#endif
