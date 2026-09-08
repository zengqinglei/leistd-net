import { signal } from '@angular/core';

/** 行展开状态：被响应式隐藏的列不丢数据，点行首箭头即可展开查看。 */
export interface ExpandableRows {
  isExpanded(id: string): boolean;
  toggle(id: string): void;
}

/**
 * 创建行展开状态。
 *
 * 每次切换都换一个新的 `Set`（而不是原地增删）：signal 按引用判等，
 * 原地改动不会触发变更检测，表现是点了箭头没反应。
 */
export function createExpandableRows(): ExpandableRows {
  const expanded = signal<ReadonlySet<string>>(new Set());

  return {
    isExpanded: (id) => expanded().has(id),
    toggle: (id) => {
      const next = new Set(expanded());
      if (!next.delete(id)) {
        next.add(id);
      }
      expanded.set(next);
    },
  };
}
