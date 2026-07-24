export default {
  extends: ['stylelint-config-standard', 'stylelint-config-clean-order'],
  plugins: ['stylelint-declaration-block-no-ignored-properties'],
  rules: {
    'plugin/declaration-block-no-ignored-properties': true,
    'selector-type-no-unknown': [
      true,
      {
        ignore: ['custom-elements'],
      },
    ],
    'selector-pseudo-element-no-unknown': [
      true,
      {
        ignorePseudoElements: ['ng-deep'],
      },
    ],
    'at-rule-no-unknown': [
      true,
      {
        ignoreAtRules: ['plugin', 'custom-variant', 'theme', 'apply', 'layer'],
      },
    ],
    'import-notation': 'string',
    // Spartan UI 主题变量用 oklch 小数记法（ui-theme 生成器的官方输出），
    // 不强制改写为百分比/度数，避免与官方产物及后续 healthcheck 升级冲突。
    'hue-degree-notation': null,
    'lightness-notation': null,
    'alpha-value-notation': null,
    'color-function-notation': null,
    // 迁移共存期：PrimeNG 与 Spartan 的主题变量各占一个 :root 块。
    // TODO: PrimeNG 完全移除后（阶段 5）恢复此规则为默认。
    'no-duplicate-selectors': null,
  },
};
