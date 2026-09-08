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
