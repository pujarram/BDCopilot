/** Quick-ask chips shared by Chat page and FAB Copilot panel. Click sends immediately. */
export interface CopilotQuickChip {
  key: string;
  label: string;
  /** Full prompt sent to the AI when the chip is clicked. */
  prompt: string;
}

export const COPILOT_QUICK_CHIPS: CopilotQuickChip[] = [
  {
    key: 'gdpr',
    label: 'GDPR for banking',
    prompt: 'How should GDPR and data residency be described to banking clients?'
  },
  {
    key: 'wealth-security',
    label: 'Wealth security topics',
    prompt: 'What security and compliance topics are covered for wealth proposals?'
  },
  {
    key: 'wealth-template',
    label: 'Wealth proposal structure',
    prompt: 'Summarize the wealth proposal template structure and recommended sections.'
  },
  {
    key: 'retail-roi',
    label: 'Retail ROI guidance',
    prompt: 'What ROI and payback guidance appears in the retail business case?'
  },
  {
    key: 'citations',
    label: 'How citations work',
    prompt: 'Explain SharePoint permission awareness and how BD Copilot cites indexed documents.'
  },
  {
    key: 'rfp-start',
    label: 'Draft RFP outline',
    prompt:
      'Based on indexed win material, outline the executive summary themes for a wealth management RFP response.'
  }
];
