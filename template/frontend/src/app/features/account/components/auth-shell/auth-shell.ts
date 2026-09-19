import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';
//#if (IncludeLocalization)
import { TranslocoModule } from '@jsverse/transloco';
//#endif
import { HlmCardImports } from '@spartan-ng/helm/card';

//#if (IncludeLocalization)
import { LanguageSwitcher } from '../../../../shared/components/language-switcher/language-switcher';
//#endif
import { Logo } from '../../../../shared/components/logo/logo';
import { ThemeModeToggle } from '../../../../shared/components/theme-mode-toggle/theme-mode-toggle';

/**
 * 认证页（登录、注册、强制启用双因素）共用的外壳：品牌标识、居中卡片、右上角主题与语言切换。
 *
 * 这几页在一次登录里会被连续经过，外观必须一致，否则用户会以为跳到了另一个站点。
 * 卡片之外的补充信息（如开发环境的测试账号）投影到带 `authShellFooter` 属性的元素。
 */
@Component({
  selector: 'app-auth-shell',
  // prettier-ignore
  imports: [
    RouterLink,
    ...HlmCardImports,
    Logo,
    ThemeModeToggle,
    //#if (IncludeLocalization)
    LanguageSwitcher,
    TranslocoModule,
    //#endif
  ],
  templateUrl: './auth-shell.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AuthShell {
  /** 卡片宽度：表单用 `default`；需要并排内容（如二维码与密钥）用 `wide`。 */
  readonly width = input<'default' | 'wide'>('default');
  /**
   * 已登录时语言切换会写回账户设置。强制启用两步验证的受限会话调不了那个接口，
   * 放着切换器只会换来一条"保存失败"，所以那一页关掉它；主题只存在本机，照常显示。
   */
  readonly showLanguageSwitcher = input(true);
}
