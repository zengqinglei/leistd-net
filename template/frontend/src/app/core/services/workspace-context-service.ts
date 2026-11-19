//#if (TenantWorkspace)
import { HttpClient, HttpContext } from '@angular/common/http';
import { Injectable, Injector, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';

import { PLATFORM_ENTRY_PERMISSIONS } from '../../shared/constants/permission.constants';
import { CurrentPermissionsOutputDto } from '../../shared/dtos/permission.dto';
import { WorkspaceOutputDto } from '../dtos/workspace.dto';
import { WORKSPACE_SCOPE } from '../interceptors/http-context-tokens';
import { EntryRouteService } from '../routing/entry-route-service';
import { AuthService } from './auth-service';
import { AuthorizationService } from './authorization-service';
import { SessionContextService } from './session-context-service';
//#if (IncludeRealTime || IncludeNotifications)
import { SignalRService } from './signalr-service';
//#endif

/** 当前范围只存在本标签页内；存储只记最后一次成功的落点。 */
@Injectable({ providedIn: 'root' })
export class WorkspaceContextService {
  private readonly injector = inject(Injector);
  private get http() {
    return this.injector.get(HttpClient);
  }
  private readonly router = inject(Router);
  private get entry() {
    return this.injector.get(EntryRouteService);
  }
  private get session() {
    return this.injector.get(SessionContextService);
  }
  get auth() {
    return this.injector.get(AuthService);
  }
  private get authorization() {
    return this.injector.get(AuthorizationService);
  }
  readonly workspaces = signal<readonly WorkspaceOutputDto[]>([]);
  readonly target = signal<string | null | undefined>(undefined);
  readonly current = signal<string | null>(null);
  readonly filter = signal('');
  readonly canAccessConsole = signal(false);
  private readonly lastTenant = signal(localStorage.getItem('last-tenant-workspace'));
  readonly returnWorkspace = computed(
    () => this.workspaces().find((item) => item.name === this.lastTenant()) ?? this.workspaces()[0],
  );
  readonly actor = computed(() => {
    const scope = this.requestScope();
    return scope ? this.workspaces().find((workspace) => workspace.name === scope) : null;
  });
  readonly actorId = computed(() => this.actor()?.userId ?? this.auth.currentUser()?.id);
  readonly tenantId = computed(() => this.actor()?.tenantId ?? this.auth.currentUser()?.tenantId);
  private revision = 0;
  private recovering: Promise<void> | undefined;
  //#if (IncludeRealTime || IncludeNotifications)
  private get signalR() {
    return this.injector.get(SignalRService);
  }
  //#endif

  constructor() {
    this.router.events.pipe(takeUntilDestroyed()).subscribe((event) => {
      if (event instanceof NavigationEnd) {
        const routeName = this.nameFromUrl(event.urlAfterRedirects);
        const name =
          this.workspaces().find((item) => item.name.toLowerCase() === routeName?.toLowerCase())
            ?.name ?? routeName;
        this.current.set(name);
        if (name) {
          localStorage.setItem('last-workspace', name);
          localStorage.setItem('last-tenant-workspace', name);
          this.lastTenant.set(name);
        } else if (/^\/(platform|workspace)(?:\/|$)/.test(event.urlAfterRedirects))
          localStorage.setItem('last-workspace', '');
      }
    });
  }

  clear(): void {
    this.revision++;
    this.workspaces.set([]);
    this.target.set(null);
    this.current.set(null);
    this.filter.set('');
    this.canAccessConsole.set(false);
  }

  nameFromUrl(url: string): string | null {
    const name = /^\/w\/([^/?#]+)/.exec(url)?.[1];
    return name ? decodeURIComponent(name) : null;
  }

  requestScope(): string | null {
    return this.target() === undefined
      ? this.nameFromUrl(this.entry.url())
      : (this.target() ?? null);
  }

  async refresh(): Promise<void> {
    const list = await firstValueFrom(
      this.http.get<WorkspaceOutputDto[]>('/api/v1/auth/me/workspaces', {
        context: new HttpContext().set(WORKSPACE_SCOPE, null),
      }),
    );
    this.workspaces.set(list);
    if (this.auth.currentUser()?.tenantId) this.canAccessConsole.set(false);
    else {
      const permissions = await firstValueFrom(
        this.http.get<CurrentPermissionsOutputDto>('/api/v1/permissions/current', {
          context: new HttpContext().set(WORKSPACE_SCOPE, null),
        }),
      );
      this.canAccessConsole.set(
        permissions.permissions.some((permission) =>
          PLATFORM_ENTRY_PERMISSIONS.some((entry) => entry === permission),
        ),
      );
    }
  }

  async establish(name: string | null): Promise<boolean> {
    if (this.target() === name && this.authorization.loaded()) return true;
    const revision = ++this.revision;
    this.target.set(name);
    this.authorization.clear();
    //#if (IncludeRealTime || IncludeNotifications)
    void this.signalR.setWorkspace(name, !this.auth.currentUser()?.tenantId);
    //#endif
    await this.session.establish();
    return revision === this.revision;
  }

  path(route: string): string {
    const name = this.current();
    return name ? `/w/${encodeURIComponent(name)}${route}` : route;
  }

  landingUrl(): string {
    const last = localStorage.getItem('last-workspace');
    if (last === '' && !this.auth.currentUser()?.tenantId) return '/platform';
    const selected = this.workspaces().find((item) => item.name === last) ?? this.workspaces()[0];
    return selected ? `/w/${encodeURIComponent(selected.name)}/platform` : '/platform';
  }

  switchTo(name: string | null): void {
    const url = this.router.url;
    const suffix = url.startsWith('/w/') ? url.replace(/^\/w\/[^/]+/, '').split(/[?#]/)[0] : '';
    const destination = name
      ? `/w/${encodeURIComponent(
          name,
        )}${suffix.startsWith('/platform') || suffix.startsWith('/workspace') ? suffix : '/platform'}`
      : '/platform';
    void this.router.navigateByUrl(destination);
  }

  recoverUnavailable(): Promise<void> {
    if (!this.recovering) {
      this.recovering = this.refresh()
        .then(() => this.switchTo(this.workspaces()[0]?.name ?? null))
        .finally(() => {
          this.recovering = undefined;
        });
    }
    return this.recovering;
  }
}
//#endif
