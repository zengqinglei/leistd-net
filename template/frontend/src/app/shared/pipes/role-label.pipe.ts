import { Pipe, PipeTransform } from '@angular/core';

import { ROLE_LABEL_MAP } from '../models/role.enum';

//#if (IncludeLocalization)
/**
 * 角色名称映射 Pipe（纯管道）。用法：{{ role | roleLabel | transloco }}
 * ——本管道得词条键，再由 transloco 按当前语言翻译。保持纯管道，响应式由下游 `| transloco` 负责。
 */
//#else
/**
 * 角色名称映射 Pipe（纯管道）。用法：{{ role | roleLabel }} → 直接得英文默认标签。
 */
//#endif
@Pipe({
  name: 'roleLabel',
  standalone: true
})
export class RoleLabelPipe implements PipeTransform {
  transform(value: string | undefined | null): string {
    if (!value) return '-';
    return ROLE_LABEL_MAP[value as keyof typeof ROLE_LABEL_MAP] ?? value;
  }
}

//#if (IncludeLocalization)
/**
 * 获取角色标签。ROLE_LABEL_MAP 值是词条键，本函数返回该键，
 * 调用方注入 TranslocoService 自行翻译（以便随语言切换在响应式上下文重新求值）。
 */
//#else
/**
 * 获取角色标签（返回英文默认标签）。
 */
//#endif
export function getRoleLabel(role: string): string {
  return ROLE_LABEL_MAP[role as keyof typeof ROLE_LABEL_MAP] ?? role;
}
