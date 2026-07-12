type Localize = (messageParts: TemplateStringsArray, ...expressions: readonly unknown[]) => string;

declare global {
  var $localize: Localize;
}

globalThis.$localize ??= (messageParts, ...expressions) =>
  messageParts.reduce((message, part, index) => message + (index === 0 ? '' : String(expressions[index - 1])) + part, '');

export {};
