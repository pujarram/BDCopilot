// Registers Fluent UI Web Components and applies a Tieto-aligned brand theme.
import '@fluentui/web-components/button.js';
import '@fluentui/web-components/text-input.js';
import '@fluentui/web-components/textarea.js';
import '@fluentui/web-components/dropdown.js';
import '@fluentui/web-components/option.js';
import '@fluentui/web-components/badge.js';
import '@fluentui/web-components/switch.js';
import '@fluentui/web-components/field.js';
import '@fluentui/web-components/label.js';

import { setTheme } from '@fluentui/web-components';
import { webLightTheme, type Theme } from '@fluentui/tokens';

/** Tieto brand palette mapped onto Fluent light tokens (navy + electric yellow accents). */
const tietoFluentTheme: Theme = {
  ...webLightTheme,
  colorBrandForeground1: '#3531cf',
  colorBrandForeground2: '#4e60e7',
  colorBrandBackground: '#3531cf',
  colorBrandBackgroundHover: '#4e60e7',
  colorBrandBackgroundPressed: '#021e57',
  colorBrandBackgroundSelected: '#3531cf',
  colorBrandStroke1: '#3531cf',
  colorBrandStroke2: '#839df9',
  colorCompoundBrandForeground1: '#3531cf',
  colorCompoundBrandForeground1Hover: '#4e60e7',
  colorCompoundBrandForeground1Pressed: '#021e57',
  colorCompoundBrandBackground: '#3531cf',
  colorCompoundBrandBackgroundHover: '#4e60e7',
  colorCompoundBrandBackgroundPressed: '#021e57',
  colorCompoundBrandStroke: '#3531cf',
  colorCompoundBrandStrokeHover: '#4e60e7',
  colorCompoundBrandStrokePressed: '#021e57',
  colorNeutralForeground1: '#021e57',
  colorNeutralForeground2: '#57524f',
  colorNeutralForeground3: '#a8a29e',
  colorNeutralBackground1: '#ffffff',
  colorNeutralBackground2: '#fcfaf7',
  colorNeutralBackground3: '#f3f0ef',
  colorNeutralStroke1: '#e2e0df',
  colorNeutralStroke2: '#d2d0cb',
  colorStrokeFocus2: '#839df9',
  fontFamilyBase: "'Tieto Sans', system-ui, -apple-system, 'Segoe UI', sans-serif",
};

export function initializeFluentDesignSystem(): void {
  // Tieto brand is light-theme only (matches tieto.com / Polaris shell).
  setTheme(tietoFluentTheme);
}
