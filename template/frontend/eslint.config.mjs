// @ts-check
import eslint from '@eslint/js';
import { defineConfig, globalIgnores } from 'eslint/config';
import tseslint from 'typescript-eslint';
import angular from 'angular-eslint';

import eslintConfigPrettier from 'eslint-config-prettier/flat';
import { importX } from 'eslint-plugin-import-x';
import unusedImports from 'eslint-plugin-unused-imports';

export default defineConfig(
  globalIgnores(['.angular/**', 'coverage/**', 'dist/**']),
  {
    files: ['**/*.ts'],
    plugins: {
      'import-x': importX,
      'unused-imports': unusedImports,
    },
    extends: [
      eslint.configs.recommended,
      ...tseslint.configs.recommended,
      ...tseslint.configs.stylistic,
      ...angular.configs.tsRecommended,
      eslintConfigPrettier,
    ],
    processor: angular.processInlineTemplates,
    rules: {
      '@angular-eslint/component-selector': [
        'error',
        { type: 'element', prefix: 'app', style: 'kebab-case' },
      ],
      '@angular-eslint/directive-selector': [
        'error',
        { type: 'attribute', prefix: 'app', style: 'camelCase' },
      ],
      '@angular-eslint/no-pipe-impure': 'error',
      '@angular-eslint/prefer-output-readonly': 'error',
      '@typescript-eslint/no-unused-vars': 'off',
      'import-x/no-duplicates': 'error',
      'import-x/no-unassigned-import': 'error',
      'import-x/order': [
        'error',
        {
          alphabetize: { order: 'asc' },
          'newlines-between': 'always',
          groups: ['builtin', 'external', 'internal', ['parent', 'sibling', 'index'], 'type'],
        },
      ],
      'no-restricted-imports': [
        'error',
        {
          paths: [
            {
              name: '@angular/forms',
              importNames: ['FormsModule', 'ReactiveFormsModule', 'NgModel', 'NgForm', 'NgControl'],
              message:
                'Use Angular Signal Forms (@angular/forms/signals); FormsModule/ReactiveFormsModule/ngModel are not allowed.',
            },
          ],
        },
      ],
      'unused-imports/no-unused-imports': 'error',
      'unused-imports/no-unused-vars': [
        'error',
        { vars: 'all', varsIgnorePattern: '^_', args: 'after-used', argsIgnorePattern: '^_' },
      ],
      'prefer-template': 'error',
    },
  },
  {
    files: ['_mock/**/*.ts'],
    rules: {
      '@typescript-eslint/no-explicit-any': 'off',
    },
  },
  {
    files: ['**/*.html'],
    extends: [...angular.configs.templateRecommended, ...angular.configs.templateAccessibility],
  },
);
