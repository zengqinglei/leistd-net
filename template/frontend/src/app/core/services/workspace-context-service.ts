//#if (TenantWorkspace)
import { HttpClient, HttpContext } from '@angular/common/http';
import { Injectable, Injector, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';

import { PLATFORM_ENTRY_PERMISSIONS } from '../../shared/constants/permission.constants';
import { CurrentPermissionsOutputDto } from '../../shared/dtos/permission.dto';
import { WorkspaceOutputDto } from '../dtos/workspace.dto';
import { workspaceDenial } from '../errors/workspace-access-error';
import { WORKSPACE_SCOPE } from '../interceptors/http-context-tokens';
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
  readonly target = signal<string | null>(null);
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
  private directoryRequest: Promise<void> | undefined;
  private consoleRequest: Promise<void> | undefined;
  private startupRefresh: Promise<void> | undefined;
  private startupDenial: unknown;
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
    this.directoryRequest = undefined;
    this.consoleRequest = undefined;
    this.startupRefresh = undefined;
    this.startupDenial = undefined;
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
    return this.target();
  }

  /** 目录的启动结果交给第一条守卫，后续仅复用成功的读取。 */
  async loadForStartup(context: Promise<void>): Promise<void> {
    this.startupDenial = undefined;
    //#if (LocalIdentity)
    if (this.auth.currentUser()?.twoFactorSetupRequired) {
      return context;
    }
    //#endif
    this.startupRefresh = this.readDirectoryAndConsole(
      this.target() === null ? context : undefined,
    );
    const [result] = await Promise.allSettled([context, this.startupRefresh]);
    if (result.status === 'fulfilled') return;
    if (this.target() && workspaceDenial(result.reason)) {
      this.startupDenial = result.reason;
    } else {
      throw result.reason;
    }
  }

  refresh(mode: 'reuse' | 'reload' = 'reuse'): Promise<void> {
    const startup = this.startupRefresh;
    this.startupRefresh = undefined;
    if (mode === 'reload') {
      this.directoryRequest = undefined;
      this.consoleRequest = undefined;
    } else if (startup) {
      return startup;
    }
    return this.readDirectoryAndConsole();
  }

  /** 授权写入成功后更新本人的目录，失败时下次导航仍可重试读取。 */
  refreshAfterAccessChange(homeUserId: string | null): void {
    if (this.auth.currentUser()?.id !== homeUserId) return;
    this.directoryRequest = undefined;
    void this.readDirectory().catch(() => undefined);
  }

  private async readDirectoryAndConsole(homeContext?: Promise<void>): Promise<void> {
    const results = await Promise.allSettled([this.readDirectory(), this.readConsole(homeContext)]);
    // 目录失败优先，与原先的读取顺序一致。
    for (const result of results) {
      if (result.status === 'rejected') throw result.reason;
    }
  }

  private readDirectory(): Promise<void> {
    if (this.directoryRequest) return this.directoryRequest;
    const request = firstValueFrom(
      this.http.get<WorkspaceOutputDto[]>('/api/v1/auth/me/workspaces'),
    ).then(
      (list) => {
        if (this.directoryRequest === request) this.workspaces.set(list);
      },
      (error: unknown) => {
        if (this.directoryRequest === request) this.directoryRequest = undefined;
        throw error;
      },
    );
    return (this.directoryRequest = request);
  }

  private readConsole(homeContext?: Promise<void>): Promise<void> {
    if (this.auth.currentUser()?.tenantId) return Promise.resolve();
    if (this.consoleRequest) return this.consoleRequest;
    const allowed = homeContext
      ? homeContext.then(() => this.authorization.canAccessPlatform())
      : firstValueFrom(
          this.http.get<CurrentPermissionsOutputDto>('/api/v1/permissions/current', {
            context: new HttpContext().set(WORKSPACE_SCOPE, null),
          }),
        ).then(({ permissions }) =>
          PLATFORM_ENTRY_PERMISSIONS.some((entry) => permissions.includes(entry)),
        );
    const request = allowed.then(
      (canAccess) => {
        if (this.consoleRequest === request) this.canAccessConsole.set(canAccess);
      },
      (error: unknown) => {
        if (this.consoleRequest === request) this.consoleRequest = undefined;
        throw error;
      },
    );
    return (this.consoleRequest = request);
  }

  async establish(name: string | null): Promise<boolean> {
    const sameScope = this.target()?.toLowerCase() === name?.toLowerCase();
    const denial = this.startupDenial;
    this.startupDenial = undefined;
    if (sameScope && denial) throw denial;
    const reuse = sameScope && this.authorization.loaded();
    const revision = reuse ? this.revision : ++this.revision;
    this.target.set(name);
    if (!reuse) this.authorization.clear();
    //#if (IncludeRealTime || IncludeNotifications)
    void this.signalR.setWorkspace(name, !this.auth.currentUser()?.tenantId);
    //#endif
    if (!reuse) await this.session.establish();
    if (revision === this.revision && !name && !this.auth.currentUser()?.tenantId) {
      this.canAccessConsole.set(this.authorization.canAccessPlatform());
    }
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
      this.recovering = this.refresh('reload')
        .then(() => this.switchTo(this.workspaces()[0]?.name ?? null))
        .finally(() => {
          this.recovering = undefined;
        });
    }
    return this.recovering;
  }
}
//#endif
