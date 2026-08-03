/** 头像回退配色（背景/前景成对，柔和 pastel，保证可读对比度）。 */
export interface AvatarPalette {
  background: string;
  color: string;
}

const AVATAR_PALETTE: readonly AvatarPalette[] = [
  { background: '#dbeafe', color: '#1d4ed8' },
  { background: '#dcfce7', color: '#15803d' },
  { background: '#fef3c7', color: '#b45309' },
  { background: '#fce7f3', color: '#be185d' },
  { background: '#ede9fe', color: '#6d28d9' },
];

/** 按 seed（用户名 / 显示名）稳定映射到一档头像配色。 */
export function avatarPalette(seed: string): AvatarPalette {
  let total = 0;
  for (const char of seed) {
    total += char.charCodeAt(0);
  }
  return AVATAR_PALETTE[total % AVATAR_PALETTE.length];
}
