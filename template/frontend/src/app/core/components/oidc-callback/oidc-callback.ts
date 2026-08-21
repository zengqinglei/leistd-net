//#if (ResourceService)
import { ChangeDetectionStrategy, Component } from '@angular/core';

@Component({
  selector: 'app-oidc-callback',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: '<p class="p-6 text-sm text-muted-foreground">Completing sign in...</p>',
})
export class OidcCallback {}
//#endif
