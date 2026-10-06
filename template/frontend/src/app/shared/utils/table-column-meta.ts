import type { AppColumnMeta, AppTableFeatures } from './table-features';
import type { ColumnDef, ColumnVisibilityState, RowData } from '@tanstack/table-core';

export type TableViewport = 'mobile' | 'tablet' | 'desktop';

/**
 * 行操作列的列元数据：右侧吸附、不参与排序与隐藏。
 *
 * 四个平台表格共用同一份。class 串各写一遍时会各自漂移，
 * 表现是吸附列的背景/边框在不同页面对不上——那种不一致没人会当成缺陷去修。
 */
export const ACTIONS_COLUMN_META: AppColumnMeta = {
  priority: 'primary',
  locked: true,
  headClass:
    'w-px px-2 text-right whitespace-nowrap sticky right-0 z-20 bg-card border-l border-border',
  cellClass:
    'w-px px-2 py-2 whitespace-nowrap sticky right-0 z-10 bg-card group-hover:bg-muted/50 border-l border-border',
};

/**
 * 行的主列（名称、标识）的列元数据：始终显示，不参与隐藏。
 *
 * 列按优先级逐档收起，到最窄一档（`TableFit` 在容器上写 `data-table-fit="mobile"`）仍放不下时，
 * 只剩不换行的长名称撑宽表格，吸附的操作列就会压住被横向滚走的状态列。此时主列占满剩余宽度，
 * 单元格内容包一层 `TITLE_CONTENT_CLASS`，让长名称截断而不是撑宽。更宽的档位不截断：
 * 那里放不下应当先收列，主列可压缩会让 `TableFit` 永远量不到溢出。
 */
export const TITLE_COLUMN_META: AppColumnMeta = {
  priority: 'primary',
  locked: true,
  cellClass: 'px-4 py-2 sm:px-6 in-data-[table-fit=mobile]:w-full',
};

/**
 * 主列单元格内容的外层 class：最窄一档下不按内容撑宽（行内尺寸包含），宽度由单元格给定，
 * 里面的文字用 `truncate` 截断。
 */
export const TITLE_CONTENT_CLASS = 'min-w-0 in-data-[table-fit=mobile]:[contain:inline-size]';

export function tableColumnVisibility<TData extends RowData>(
  columns: readonly ColumnDef<AppTableFeatures, TData>[],
  viewport: TableViewport,
): ColumnVisibilityState {
  return Object.fromEntries(
    columns.flatMap((column) => {
      const id = 'id' in column ? column.id : undefined;
      if (!id) {
        return [];
      }

      const meta = column.meta;
      const visible =
        meta?.locked === true ||
        meta?.priority === undefined ||
        meta.priority === 'primary' ||
        (meta.priority === 'secondary' && viewport !== 'mobile') ||
        (meta.priority === 'tertiary' && viewport === 'desktop');

      return [[id, visible]];
    }),
  );
}
