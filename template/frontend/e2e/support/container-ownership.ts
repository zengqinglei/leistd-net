import { randomUUID } from 'node:crypto';

interface NamedContainer<T> {
  withName(name: string): T;
  withLabels(labels: Record<string, string>): T;
}

/** 可选执行器归属协议；与后端测试相同，未提供协议时沿用 Testcontainers 默认行为。 */
export function applyRunnerContainerOwnership<T extends NamedContainer<T>>(
  builder: T,
  component: 'postgres' | 'rabbitmq' = 'postgres',
): T {
  const raw = process.env['RPR_DOCKER_LABELS'];
  const prefix = process.env['RPR_DOCKER_NAME_PREFIX'];
  if (raw === undefined && prefix === undefined) return builder;
  const error =
    'Invalid runner container ownership: supply both RPR_DOCKER_LABELS and RPR_DOCKER_NAME_PREFIX.';
  if (!raw || !prefix || !/^[a-z0-9][a-z0-9_.-]{0,150}$/.test(prefix)) throw new Error(error);
  let parsed: unknown;
  try {
    parsed = JSON.parse(raw);
  } catch {
    throw new Error(error);
  }
  if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) throw new Error(error);
  const labels = parsed as Record<string, unknown>;
  if (
    labels['remote-project-runner.managed'] !== 'true' ||
    !labels['remote-project-runner.task'] ||
    !labels['remote-project-runner.attempt'] ||
    Object.entries(labels).some(
      ([key, value]) =>
        !key.startsWith('remote-project-runner.') || typeof value !== 'string' || !value.trim(),
    )
  )
    throw new Error(error);
  return builder
    .withName(`${prefix}-e2e-${component}-${randomUUID()}`)
    .withLabels(labels as Record<string, string>);
}
