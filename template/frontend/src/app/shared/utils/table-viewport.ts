import { BreakpointObserver, BreakpointState } from '@angular/cdk/layout';
import { inject, Signal, computed } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';

import { TableViewport } from '../models/table-column-meta';

/**
 * 表格列可见性的两个断点。
 *
 * 与 Tailwind 的 `md` / `lg` 对齐（768px / 1024px）——列在什么宽度下折叠，
 * 必须和页面其它响应式行为同一个刻度，否则表格会在别处还没换布局时先折叠。
 */
const MEDIUM_VIEWPORT = '(min-width: 768px)';
const LARGE_VIEWPORT = '(min-width: 1024px)';

/**
 * 当前视口档位（mobile / tablet / desktop）。
 *
 * 必须在注入上下文里调用（字段初始化处）。四个平台表格共用同一份判定：
 * 各写一遍时断点值会各自漂移，表现是同一页里两个表格在不同宽度下折叠。
 */
export function tableViewportSignal(): Signal<TableViewport> {
  const breakpointObserver = inject(BreakpointObserver);

  const state = toSignal(breakpointObserver.observe([MEDIUM_VIEWPORT, LARGE_VIEWPORT]), {
    initialValue: {
      matches: false,
      breakpoints: { [MEDIUM_VIEWPORT]: false, [LARGE_VIEWPORT]: false },
    } satisfies BreakpointState,
  });

  return computed(() => {
    const breakpoints = state().breakpoints;
    if (breakpoints[LARGE_VIEWPORT] === true) return 'desktop';
    if (breakpoints[MEDIUM_VIEWPORT] === true) return 'tablet';
    return 'mobile';
  });
}
