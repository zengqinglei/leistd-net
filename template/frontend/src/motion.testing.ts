import { beforeAll } from 'vitest';

// Angular TestBed 默认关闭框架动画，Spartan brain 的单测也关闭 CSS 动效，以便直接观察终态。
beforeAll(() => {
  const style = document.createElement('style');
  style.id = 'test-disable-motion';
  style.textContent =
    '*, *::before, *::after { transition: none !important; animation: none !important; }';
  document.head.append(style);
});
