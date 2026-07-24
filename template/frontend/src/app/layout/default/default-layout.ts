import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { HlmSidebarImports } from '@spartan-ng/helm/sidebar';

import { DefaultFooter } from '../components/default-footer/default-footer';
import { DefaultHeader } from '../components/default-header/default-header';
import { DefaultSidebar } from '../components/default-sidebar/default-sidebar';

@Component({
  selector: 'app-default-layout',
  standalone: true,
  imports: [RouterOutlet, DefaultHeader, DefaultFooter, DefaultSidebar, ...HlmSidebarImports],
  templateUrl: './default-layout.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DefaultLayout {}
