/** Phase 2 — single Generate workspace document types. */
export interface GenerateDocType {
  path: string;
  label: string;
  description: string;
}

export const GENERATE_DOC_TYPES: GenerateDocType[] = [
  {
    path: 'rfp',
    label: 'RFP',
    description:
      'Draft a full RFP response from indexed win material — stream sections, approve, then export to Word or PowerPoint.'
  },
  {
    path: 'business-case',
    label: 'Business case',
    description:
      'Executive business case with comparable engagements, benefit ranges, history, and export.'
  },
  {
    path: 'proposal',
    label: 'Proposal',
    description:
      'Full proposal pack from your library — live streaming, optional deck and architecture diagram, export.'
  },
  {
    path: 'battle-card',
    label: 'Battle card',
    description:
      'Grounded competitive intelligence — eight sections, compare-two mode, publish to corpus, pursuit linking.'
  }
];

export function generateRoute(type: string): string {
  return `/generate/${type}`;
}

/** Map legacy top-level paths to Generate hub child routes. */
export function legacyGenerateRedirect(path: string): string | null {
  const normalized = path.replace(/^\//, '').toLowerCase();
  if (normalized === 'competitive') return generateRoute('battle-card');
  if (GENERATE_DOC_TYPES.some(t => t.path === normalized)) return generateRoute(normalized);
  return null;
}
