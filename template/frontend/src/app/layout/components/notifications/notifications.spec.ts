//#if (IncludeNotifications)
import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
//#if (IncludeLocalization)
import { TranslocoService } from '@jsverse/transloco';
//#endif
import { toast } from '@spartan-ng/brain/sonner';
//#if (IncludeLocalization)
import { BehaviorSubject } from 'rxjs';
//#endif

import { NotificationOutputDto, NotificationService } from './notification-service';
import { Notifications } from './notifications';
import { ApplicationHttpError } from '../../../core/errors/application-http-error';
import { ConfirmService } from '../../../core/feedback/confirm-service';
//#if (IncludeLocalization)

const EN: Record<string, string> = {
  'layout.notifications.title': 'Notifications',
  'layout.notifications.unreadAria': 'Notifications ({{count}} unread)',
};
const ZH: Record<string, string> = {
  'layout.notifications.title': '通知',
  'layout.notifications.unreadAria': '通知（{{count}} 条未读）',
};
//#endif

describe('Notifications', () => {
  let service: jasmine.SpyObj<NotificationService>;
  //#if (IncludeLocalization)
  let translations: BehaviorSubject<Record<string, string>>;
  let transloco: {
    translate: (key: string, params?: Record<string, unknown>) => string;
    selectTranslation: () => unknown;
  };
  //#endif
  let confirmService: jasmine.SpyObj<ConfirmService>;
  let errorToast: jasmine.Spy;

  beforeEach(() => {
    service = jasmine.createSpyObj<NotificationService>(
      'NotificationService',
      ['init', 'markAsRead', 'markAllAsRead', 'clearAll', 'clearOne', 'getIcon'],
      { notifications: signal([]), unreadCount: signal(0), loading: signal(false) },
    );
    service.init.and.resolveTo();
    confirmService = jasmine.createSpyObj<ConfirmService>('ConfirmService', ['open']);
    confirmService.open.and.resolveTo(true);
    errorToast = spyOn(toast, 'error');
    //#if (IncludeLocalization)
    // 最小 Transloco 桩：translationReady 依赖 selectTranslation() 在语言切换时再次发射。
    translations = new BehaviorSubject<Record<string, string>>(EN);
    transloco = {
      translate: (key: string, params?: Record<string, unknown>) => {
        const text = translations.value[key] ?? key;
        return params ? text.replace('{{count}}', String(params['count'])) : text;
      },
      selectTranslation: () => translations.asObservable(),
    };
    //#endif

    TestBed.configureTestingModule({
      imports: [Notifications],
      // prettier-ignore
      providers: [
        provideRouter([]),
        { provide: NotificationService, useValue: service },
        { provide: ConfirmService, useValue: confirmService },
        //#if (IncludeLocalization)
        { provide: TranslocoService, useValue: transloco },
        //#endif
      ],
    });
  });

  function createComponent(): Notifications {
    const fixture = TestBed.createComponent(Notifications);
    fixture.detectChanges();
    return fixture.componentInstance;
  }

  function httpError(status: number): ApplicationHttpError {
    return ApplicationHttpError.from(
      new HttpErrorResponse({
        status,
        statusText: 'Server Error',
        error: { detail: 'Delete failed.' },
      }),
    );
  }

  it('keeps the panel open and surfaces an error when clearing all fails', async () => {
    service.clearAll.and.rejectWith(httpError(500));
    const component = createComponent();
    component.notificationOpen.set('open');

    await component.clearAllNotifications();

    expect(errorToast).toHaveBeenCalled();
    expect(component.notificationOpen())
      .withContext('a failed delete must not look successful')
      .toBe('open');
  });

  it('closes the panel only after clearing all succeeds', async () => {
    service.clearAll.and.resolveTo();
    const component = createComponent();
    component.notificationOpen.set('open');

    await component.clearAllNotifications();

    expect(errorToast).not.toHaveBeenCalled();
    expect(component.notificationOpen()).toBe('closed');
  });
  //#if (IncludeLocalization)

  it('re-evaluates ARIA labels when the language changes at runtime', () => {
    (service.unreadCount as unknown as { set(v: number): void }).set(2);
    const component = createComponent();

    expect(component.panelLabel()).toBe('Notifications');
    expect(component.triggerLabel()).toBe('Notifications (2 unread)');

    translations.next(ZH);

    expect(component.panelLabel()).withContext('panel name must follow the language').toBe('通知');
    expect(component.triggerLabel())
      .withContext('trigger name must follow the language')
      .toBe('通知（2 条未读）');
  });
  //#endif

  it('still navigates when marking as read fails', async () => {
    const router = TestBed.inject(Router);
    const navigate = spyOn(router, 'navigateByUrl');
    service.markAsRead.and.rejectWith(httpError(500));
    const component = createComponent();
    component.notificationOpen.set('open');

    await component.onNotificationClick({
      id: 'n-1',
      title: 't',
      content: 'c',
      type: 'info',
      isRead: false,
      link: '/platform/users',
      creationTime: new Date().toISOString(),
    } as NotificationOutputDto);

    expect(errorToast).withContext('failure must still be surfaced').toHaveBeenCalled();
    expect(navigate)
      .withContext('navigation is the primary action and must not be blocked')
      .toHaveBeenCalledWith('/platform/users');
    expect(component.notificationOpen()).toBe('closed');
  });

  it('surfaces an error when dismissing a single notification fails', async () => {
    service.clearOne.and.rejectWith(httpError(503));
    const component = createComponent();

    await component.clearOneNotification('n-1');

    expect(errorToast).toHaveBeenCalled();
  });
});
//#endif
