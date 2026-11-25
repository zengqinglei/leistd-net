/// <reference types="@vitest/browser-playwright" />
import { userEvent } from 'vitest/browser';

/**
 * 以键盘模式聚焦元素，使它匹配 `:focus-visible`。Spartan 提示框在触发元素匹配 `:focus-visible` 时随聚焦打开。
 * 同一文件里之前有过点击时，
 * 程序化 `.focus()` 不再匹配 `:focus-visible`，提示框不会弹出。先按一次 Shift 把浏览器切回键盘模式
 * （不移动焦点、不触发按钮），再聚焦。元素已有焦点时先移开，保证派发一次新的 focus（如组合框选中后焦点已回到触发器）。
 * 没有进入键盘模式时当场报错，不让后续"提示没出现"的断言替它背锅。
 */
export async function focusWithKeyboard(
  element: HTMLElement,
  options?: FocusOptions,
): Promise<void> {
  if (document.activeElement === element) element.blur();
  await userEvent.keyboard('{Shift}');
  element.focus(options);
  if (!element.matches(':focus-visible')) {
    throw new Error('focusWithKeyboard: the element was focused but does not match :focus-visible');
  }
}
