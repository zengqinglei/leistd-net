import assert from 'node:assert/strict';
import { test } from 'node:test';
import { ESLint } from 'eslint';

const eslint = new ESLint();
for (const [name, template, expected] of [
  ['invalid submit', '<button type="submit" [disabled]="form().invalid()">Save</button>', 1],
  ['negated valid', '<button [disabled]="!form().valid() || saving()">Save</button>', 1],
  [
    'multiline expression',
    '<button type="button" (click)="save()" [disabled]="saving() ||\n form().invalid()">Save</button>',
    1,
  ],
  ['submit input', '<input type="submit" [disabled]="!form().valid()" />', 1],
  ['request in flight', '<button type="submit" [disabled]="form().submitting()">Save</button>', 0],
  ['permission precondition', '<button [disabled]="!canSave()">Save</button>', 0],
  ['ordinary field', '<input [disabled]="form().invalid()" />', 0],
  ['similar name', '<button [disabled]="isInvalid()">Save</button>', 0],
]) {
  test(`submission disabled rule: ${name}`, async () => {
    const [result] = await eslint.lintText(template, { filePath: 'src/submission-rule.html' });
    assert.equal(
      result.messages.filter((message) => message.ruleId === 'local/no-invalid-disabled').length,
      expected,
    );
    assert.equal(result.fatalErrorCount, 0);
  });
}
