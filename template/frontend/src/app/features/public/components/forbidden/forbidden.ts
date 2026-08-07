import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';
//#if (IncludeLocalization)
import { TranslocoModule } from '@jsverse/transloco';
//#endif
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideShieldOff } from '@ng-icons/lucide';
import { HlmButton } from '@spartan-ng/helm/button';

/**
 * 403 页面：已登录但缺少所需权限。
 *
 * 与 401 的登录跳转严格区分——未认证应去登录，已认证但无权限应停在这里，
 * 否则用户会陷入"登录成功又被弹回登录页"的循环。
 */
@Component({
  selector: 'app-forbidden',
  standalone: true,
  // prettier-ignore
  imports: [
    RouterLink,
    NgIcon,
    HlmButton,
    //#if (IncludeLocalization)
    TranslocoModule,
    //#endif
  ],
  providers: [provideIcons({ lucideShieldOff })],
  templateUrl: './forbidden.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Forbidden {}
