import { expect, it, onTestFinished } from 'vitest';

it('applies the final width immediately without CSS transitions or animations', () => {
  const button = document.createElement('button');
  button.className = 'w-8 transition-all duration-300';
  document.body.append(button);
  onTestFinished(() => button.remove());

  expect(button.getBoundingClientRect().width).toBe(32);

  button.classList.replace('w-8', 'w-10');
  expect(button.getBoundingClientRect().width).toBe(40);
  button.classList.add('animate-spin');
  expect(document.getAnimations()).toHaveLength(0);
});
