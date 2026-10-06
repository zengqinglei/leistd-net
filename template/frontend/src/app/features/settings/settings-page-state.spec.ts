import { TestBed } from '@angular/core/testing';
import { toast } from '@spartan-ng/brain/sonner';
import { Subject } from 'rxjs';

import { SettingsPageState } from './settings-page-state';
//#if (IncludeLocalization)
import { provideTranslocoTesting } from '../../core/i18n/transloco.testing';
//#endif
import { SettingService } from '../../core/settings/setting-service';
import { SettingOutputDto } from '../../core/settings/setting.dto';

/**
 * 设置页快照：最后一次读取为准。
 *
 * 切换语言与写入都会触发重取；较早发出的那次若晚到，会用旧值覆盖新快照，
 * 表现为偏好页显示的语言与实际不一致（全功能端到端发现）。
 */
describe('SettingsPageState', () => {
  const setting = (userValue: string): SettingOutputDto =>
    ({ name: 'Display.Language', userValue }) as SettingOutputDto;

  it('ignores a stale response once a newer load has started', () => {
    const responses: Subject<SettingOutputDto[]>[] = [];
    TestBed.configureTestingModule({
      providers: [
        SettingsPageState,
        {
          provide: SettingService,
          useValue: {
            getSettings: () => {
              const response = new Subject<SettingOutputDto[]>();
              responses.push(response);
              return response;
            },
          },
        },
        //#if (IncludeLocalization)
        ...provideTranslocoTesting(['en']),
        //#endif
      ],
    });
    const state = TestBed.inject(SettingsPageState);

    state.load();
    responses[1].next([setting('zh-CN')]);
    responses[0].next([setting('en')]);

    expect(state.settings()[0].userValue).toBe('zh-CN');
  });

  /** 每次读取都返回一个由用例应答的响应。 */
  function stateWithManualResponses(): {
    state: SettingsPageState;
    responses: Subject<SettingOutputDto[]>[];
  } {
    const responses: Subject<SettingOutputDto[]>[] = [];
    TestBed.configureTestingModule({
      providers: [
        SettingsPageState,
        {
          provide: SettingService,
          useValue: {
            getSettings: () => {
              const response = new Subject<SettingOutputDto[]>();
              responses.push(response);
              return response;
            },
          },
        },
        //#if (IncludeLocalization)
        ...provideTranslocoTesting(['en']),
        //#endif
      ],
    });
    return { state: TestBed.inject(SettingsPageState), responses };
  }

  it('reports a first-load failure as page state without a toast, and a retry clears it', () => {
    const notify = vi.spyOn(toast, 'error').mockImplementation(() => '');
    const { state, responses } = stateWithManualResponses();

    responses[0].error(new Error('boom'));

    // 还没有快照可保留：页面据此显示错误态与重试，而不是"没有可配置项"
    expect(state.loadError()).toBe('boom');
    expect(state.loaded()).toBe(false);
    expect(notify).not.toHaveBeenCalled();

    state.load();
    responses[1].next([setting('en')]);

    expect(state.loadError()).toBeNull();
    expect(state.loaded()).toBe(true);
  });

  it('keeps the snapshot and only notifies when a later refresh fails', () => {
    const notify = vi.spyOn(toast, 'error').mockImplementation(() => '');
    const { state, responses } = stateWithManualResponses();
    responses[0].next([setting('en')]);

    state.load();
    responses[1].error(new Error('boom'));

    expect(state.settings()[0].userValue).toBe('en');
    expect(state.loadError()).toBeNull();
    expect(notify).toHaveBeenCalledOnce();
  });
});
