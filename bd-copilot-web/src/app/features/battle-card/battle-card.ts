import { CUSTOM_ELEMENTS_SCHEMA, Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { ApiService } from '../../core/services/api.service';
import { TeamsService } from '../../core/services/teams.service';
import { ToastService } from '../../core/services/toast.service';
import { SearchReuseService, SearchReusePayload } from '../../core/services/search-reuse.service';
import { GeneratePursuitContextService } from '../../core/services/generate-pursuit-context.service';
import {
  DynamicsDealContext,
  GeneratedDocument,
  GeneratedDocumentHistory,
  GeneratedDocumentHistoryListItem,
  GeneratedSection,
  MultiApprovalStatus,
  Opportunity,
  RfpStreamEvent
} from '../../core/models/api-models';

type StreamStatus = 'queued' | 'writing-title' | 'generating' | 'done' | 'error';
type SourceTab = 'text' | 'transcript' | 'file';

interface StreamSection {
  order: number;
  title: string;
  displayTitle: string;
  content: string;
  status: StreamStatus;
  sources: GeneratedSection['sources'];
}

interface BattleCardSectionMeta {
  key: string;
  label: string;
  tone: 'accent' | 'success' | 'danger' | 'warning' | 'pro';
}

const HISTORY_TYPE = 'battle-card';

const SECTION_META: BattleCardSectionMeta[] = [
  { key: 'snapshot', label: 'Competitor snapshot', tone: 'accent' },
  { key: 'strengths', label: 'Our strengths', tone: 'success' },
  { key: 'weaknesses', label: 'Their weaknesses', tone: 'danger' },
  { key: 'differentiators', label: 'Key differentiators', tone: 'warning' },
  { key: 'objections', label: 'Objection handling', tone: 'pro' },
  { key: 'win', label: 'Win themes', tone: 'success' },
  { key: 'watch', label: 'Watch-outs', tone: 'danger' },
  { key: 'actions', label: 'Recommended actions', tone: 'accent' }
];

const WIN_LOSS_PRESETS = [
  {
    label: 'Win vs chatbot vendor',
    competitor: 'Incumbent Chatbot Vendor',
    intel:
      'We won on grounded SharePoint citations, ACL-aware answers, and Teams delivery fusion. Incumbent had mature FAQ NLP but no Planner integration.'
  },
  {
    label: 'Loss to big consult',
    competitor: 'Big Consult AI Suite',
    intel:
      'Lost on perceived global delivery bench. Counter with faster M365 time-to-value, lower TCO, and reusable RFP language from indexed wins.'
  },
  {
    label: 'Win vs point proposal tool',
    competitor: 'Point Proposal Tools',
    intel:
      'Won when pursuit needed end-to-end delivery intelligence, not slide-only generation. Emphasize corpus + Planner sync and governed export.'
  }
];

@Component({
  selector: 'app-battle-card',
  templateUrl: './battle-card.html',
  imports: [DatePipe],
  schemas: [CUSTOM_ELEMENTS_SCHEMA]
})
export class BattleCard implements OnInit, OnDestroy {
  private readonly api = inject(ApiService);
  private readonly teams = inject(TeamsService);
  private readonly toast = inject(ToastService);
  private readonly searchReuse = inject(SearchReuseService);
  private readonly pursuitCtx = inject(GeneratePursuitContextService);
  private abort: AbortController | null = null;

  protected readonly presets = WIN_LOSS_PRESETS;
  protected readonly sectionMeta = SECTION_META;

  protected readonly sourceTab = signal<SourceTab>('text');
  protected readonly pastedText = signal('');
  protected readonly transcript = signal('');
  protected readonly fileName = signal<string | null>(null);
  protected readonly compareMode = signal(false);

  protected readonly competitor = signal('Incumbent Chatbot Vendor');
  protected readonly competitorB = signal('');
  protected readonly ourSolution = signal('BD Copilot grounded assistant');
  protected readonly customerContext = signal('');
  protected readonly complianceRegion = signal('EU');
  protected readonly selectedOpportunityId = signal('');

  protected readonly deals = signal<DynamicsDealContext[]>([]);
  protected readonly opportunities = signal<Opportunity[]>([]);
  protected readonly reuseBanner = signal<SearchReusePayload | null>(null);

  protected readonly generating = signal(false);
  protected readonly streaming = signal(false);
  protected readonly exporting = signal(false);
  protected readonly saving = signal(false);
  protected readonly publishing = signal(false);
  protected readonly copied = signal(false);

  protected readonly result = signal<GeneratedDocument | null>(null);
  protected readonly savedRow = signal<GeneratedDocumentHistory | null>(null);
  protected readonly history = signal<GeneratedDocumentHistoryListItem[]>([]);
  protected readonly approval = signal<MultiApprovalStatus | null>(null);
  protected readonly progressLabel = signal('');
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly streamSections = signal<StreamSection[]>([]);

  protected readonly cardSections = computed(() => {
    const doc = this.result();
    if (!doc) return [] as { meta: BattleCardSectionMeta; section: GeneratedSection }[];
    return doc.sections
      .map(section => {
        const meta = this.matchSectionMeta(section.title);
        return meta ? { meta, section } : null;
      })
      .filter((x): x is { meta: BattleCardSectionMeta; section: GeneratedSection } => !!x);
  });

  protected readonly canGenerate = computed(() => {
    const tab = this.sourceTab();
    if (tab === 'text') return this.pastedText().trim().length > 5 || this.customerContext().trim().length > 5;
    if (tab === 'transcript') return this.transcript().trim().length > 5;
    if (tab === 'file') return !!this.fileName();
    return true;
  });

  ngOnInit(): void {
    this.applyPursuitContext();
    this.applySearchReuse();
    this.refreshHistory();
    this.api.listDynamicsDeals().subscribe({
      next: d => this.deals.set(d),
      error: err => console.error(err)
    });
    this.api.listOpportunities().subscribe({
      next: o => this.opportunities.set(o),
      error: err => console.error(err)
    });
  }

  private applyPursuitContext(): void {
    const ctx = this.pursuitCtx.context();
    if (!ctx) return;
    this.customerContext.set(ctx.focusNotes);
    if (ctx.opportunityId) this.selectedOpportunityId.set(ctx.opportunityId);
    this.toast.show(`Grounded on pursuit “${ctx.name}”.`);
  }

  ngOnDestroy(): void {
    this.abort?.abort();
  }

  protected setSourceTab(tab: SourceTab): void {
    this.sourceTab.set(tab);
    this.errorMessage.set(null);
  }

  protected applyPreset(preset: (typeof WIN_LOSS_PRESETS)[number]): void {
    this.competitor.set(preset.competitor);
    this.pastedText.set(preset.intel);
    this.sourceTab.set('text');
    this.toast.show(`Loaded preset: ${preset.label}`);
  }

  protected applyDeal(deal: DynamicsDealContext): void {
    this.customerContext.set(deal.focusNotes);
    this.selectedOpportunityId.set(deal.opportunityId);
    this.toast.show(`Pulled Dynamics context for ${deal.client}.`);
  }

  protected clearReuse(): void {
    this.reuseBanner.set(null);
  }

  protected onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;
    const reader = new FileReader();
    reader.onload = () => {
      const text = String(reader.result ?? '');
      this.pastedText.set(text);
      this.fileName.set(file.name);
      this.sourceTab.set('file');
      this.toast.show(`Loaded ${file.name}`);
    };
    reader.readAsText(file);
  }

  protected async generate(): Promise<void> {
    if (!this.canGenerate() && !this.customerContext().trim()) {
      this.errorMessage.set('Add competitive intelligence (paste, transcript, or file) before generating.');
      return;
    }

    this.abort?.abort();
    this.abort = new AbortController();
    this.generating.set(true);
    this.streaming.set(true);
    this.result.set(null);
    this.approval.set(null);
    this.savedRow.set(null);
    this.errorMessage.set(null);
    this.streamSections.set([]);
    this.progressLabel.set('Connecting to BD Copilot…');

    const raw = this.buildRawIntelligence();
    const tab = this.sourceTab();

    try {
      let doc: GeneratedDocument | null = null;
      for await (const evt of this.api.generateBattleCardStream(
        {
          competitor: this.competitor(),
          competitorB: this.compareMode() ? this.competitorB() || undefined : undefined,
          ourSolution: this.ourSolution(),
          customerContext: this.customerContext() || undefined,
          userObjectId: this.teams.user().objectId,
          complianceRegion: this.complianceRegion(),
          opportunityId: this.selectedOpportunityId() || undefined,
          sourceType: tab === 'transcript' ? 'transcript' : tab === 'file' ? 'file' : 'text',
          rawIntelligence: raw || undefined
        },
        this.abort.signal
      )) {
        if (this.abort.signal.aborted) return;
        this.applyStreamEvent(evt);
        if (evt.type === 'complete' && evt.document) doc = evt.document;
      }

      if (doc) {
        this.startApprovals(doc);
        this.persistToDatabase(doc);
        if (this.selectedOpportunityId()) {
          this.linkToOpportunity(doc);
        }
      }
    } catch (err) {
      if (!this.abort?.signal.aborted) {
        console.error(err);
        this.errorMessage.set('Battle card generation failed — is the API running on :5154?');
        this.generating.set(false);
        this.streaming.set(false);
        this.progressLabel.set('');
      }
    }
  }

  protected discard(): void {
    this.abort?.abort();
    this.result.set(null);
    this.savedRow.set(null);
    this.approval.set(null);
    this.streamSections.set([]);
    this.streaming.set(false);
    this.generating.set(false);
    this.progressLabel.set('');
  }

  protected exportDocx(): void {
    this.exportCurrent('docx');
  }

  protected exportPptx(): void {
    this.exportCurrent('pptx');
  }

  protected saveToDatabase(): void {
    const doc = this.result();
    if (!doc) return;
    this.persistToDatabase(doc, true);
  }

  protected publishToCorpus(): void {
    const doc = this.result();
    if (!doc) return;
    this.publishing.set(true);
    this.api.publishBattleCardToCorpus({
      document: doc,
      userObjectId: this.teams.user().objectId,
      displayName: this.teams.user().displayName
    }).subscribe({
      next: res => {
        this.publishing.set(false);
        this.toast.show(res.message || 'Published to Battlecards corpus.');
      },
      error: err => {
        console.error(err);
        this.publishing.set(false);
        this.toast.show('Could not publish to corpus.');
      }
    });
  }

  protected copyJson(): void {
    const doc = this.result();
    if (!doc) return;
    const payload = {
      title: doc.title,
      competitor: this.competitor(),
      competitorB: this.compareMode() ? this.competitorB() : null,
      ourSolution: this.ourSolution(),
      sections: Object.fromEntries(doc.sections.map(s => [s.title, s.content]))
    };
    void navigator.clipboard.writeText(JSON.stringify(payload, null, 2));
    this.copied.set(true);
    setTimeout(() => this.copied.set(false), 2000);
  }

  protected refreshHistory(): void {
    this.api.listDocumentHistory(HISTORY_TYPE).subscribe({
      next: rows => this.history.set(rows),
      error: err => console.error(err)
    });
  }

  protected exportHistory(item: GeneratedDocumentHistoryListItem, format: 'docx' | 'pptx' = 'docx'): void {
    this.exporting.set(true);
    this.api.exportDocumentHistory(HISTORY_TYPE, item.id, this.teams.user().objectId, format).subscribe({
      next: result => {
        void this.api.downloadExport(result)
          .then(() => {
            this.exporting.set(false);
            this.toast.show(`Downloaded ${result.fileName}`);
          })
          .catch(err => {
            console.error(err);
            this.exporting.set(false);
          });
      },
      error: err => {
        console.error(err);
        this.exporting.set(false);
        this.toast.show('Export failed.');
      }
    });
  }

  protected decide(role: string, status: 'Approved' | 'Rejected'): void {
    const doc = this.result();
    if (!doc) return;
    this.api.decideApproval({
      generationId: doc.generationId,
      role,
      status,
      userObjectId: this.teams.user().objectId,
      displayName: this.teams.user().displayName
    }).subscribe({
      next: s => {
        this.approval.set(s);
        if (s.isFullyApproved) {
          this.result.update(d => d ? { ...d, status: 'Approved' } : d);
          this.toast.show('Legal + Sales approved — export enabled.');
        }
      },
      error: err => {
        console.error(err);
        this.toast.show('Approval update failed.');
      }
    });
  }

  protected parseBullets(content: string): string[] {
    return content
      .split('\n')
      .map(l => l.replace(/^[\-\*\u2022›>\s]+/, '').trim())
      .filter(l => l.length > 0 && !l.startsWith('Objection:') && !l.startsWith('Response:'));
  }

  protected parseObjections(content: string): { objection: string; response: string }[] {
    const pairs: { objection: string; response: string }[] = [];
    let pending: string | null = null;
    for (const line of content.split('\n')) {
      const cleaned = line.replace(/^[\-\*\u2022›>\s]+/, '').trim();
      if (cleaned.toLowerCase().startsWith('objection:')) {
        pending = cleaned.slice('objection:'.length).trim();
      } else if (cleaned.toLowerCase().startsWith('response:') && pending) {
        pairs.push({ objection: pending, response: cleaned.slice('response:'.length).trim() });
        pending = null;
      }
    }
    return pairs;
  }

  protected isObjectionSection(title: string): boolean {
    return title.toLowerCase().includes('objection');
  }

  private applySearchReuse(): void {
    const payload = this.searchReuse.consume('battle-card');
    if (!payload) return;
    this.reuseBanner.set(payload);
    this.pastedText.set(payload.excerpt);
    this.sourceTab.set('text');
    if (payload.query) {
      this.competitor.set(payload.query);
    }
    this.toast.show(`Reusing “${payload.label}” from Knowledge Search.`);
  }

  private buildRawIntelligence(): string {
    const parts = [this.pastedText().trim(), this.transcript().trim()].filter(Boolean);
    return parts.join('\n\n');
  }

  private matchSectionMeta(title: string): BattleCardSectionMeta | null {
    const lower = title.toLowerCase();
    if (lower.includes('snapshot') || lower.includes('side-by-side')) return SECTION_META[0];
    if (lower.includes('strength')) return SECTION_META[1];
    if (lower.includes('weakness')) return SECTION_META[2];
    if (lower.includes('differentiator')) return SECTION_META[3];
    if (lower.includes('objection')) return SECTION_META[4];
    if (lower.includes('win')) return SECTION_META[5];
    if (lower.includes('watch')) return SECTION_META[6];
    if (lower.includes('action') || lower.includes('recommend')) return SECTION_META[7];
    return { key: 'other', label: title, tone: 'accent' };
  }

  private applyStreamEvent(evt: RfpStreamEvent): void {
    switch (evt.type) {
      case 'outline': {
        const titles = evt.sectionTitles ?? [];
        this.progressLabel.set('Outline ready — retrieving Battlecards sources…');
        this.streamSections.set(
          titles.map((title, i) => ({
            order: i + 1,
            title,
            displayTitle: title,
            content: '',
            status: 'queued' as StreamStatus,
            sources: []
          }))
        );
        break;
      }
      case 'section-start': {
        const order = evt.order ?? 1;
        this.generating.set(false);
        this.progressLabel.set(`Writing ${String(order).padStart(2, '0')} · ${evt.title ?? ''}`);
        this.patchByOrder(order, s => ({
          ...s,
          title: evt.title ?? s.title,
          displayTitle: evt.title ?? s.displayTitle,
          status: 'generating',
          content: '',
          sources: evt.sources ?? []
        }));
        break;
      }
      case 'token': {
        const order = evt.order ?? 1;
        const delta = evt.delta ?? '';
        if (!delta) return;
        this.patchByOrder(order, s => ({ ...s, status: 'generating', content: s.content + delta }));
        break;
      }
      case 'section-done': {
        const order = evt.order ?? 1;
        this.patchByOrder(order, s => ({
          ...s,
          status: 'done',
          content: evt.content ?? s.content,
          sources: evt.sources ?? s.sources
        }));
        break;
      }
      case 'complete': {
        if (evt.document) {
          this.result.set(evt.document);
          this.toast.show(`Battle card ready — ${evt.document.sections.length} sections.`);
        }
        this.generating.set(false);
        this.streaming.set(false);
        this.progressLabel.set('Battle card ready');
        break;
      }
      case 'error': {
        this.errorMessage.set(evt.message ?? 'Generation failed.');
        this.generating.set(false);
        this.streaming.set(false);
        this.progressLabel.set('');
        break;
      }
    }
  }

  private patchByOrder(order: number, fn: (s: StreamSection) => StreamSection): void {
    this.streamSections.update(list => list.map(s => (s.order === order ? fn(s) : s)));
  }

  private startApprovals(doc: GeneratedDocument): void {
    this.api.startMultiApproval({
      generationId: doc.generationId,
      documentTitle: doc.title,
      userObjectId: this.teams.user().objectId
    }).subscribe({
      next: s => this.approval.set(s),
      error: err => console.error(err)
    });
  }

  private persistToDatabase(doc: GeneratedDocument, showToast = false): void {
    this.saving.set(true);
    this.api.saveDocumentHistory(HISTORY_TYPE, {
      document: doc,
      userObjectId: this.teams.user().objectId,
      displayName: this.teams.user().displayName,
      metadata: {
        competitor: this.competitor(),
        competitorB: this.competitorB(),
        ourSolution: this.ourSolution()
      }
    }).subscribe({
      next: row => {
        this.savedRow.set(row);
        this.saving.set(false);
        this.refreshHistory();
        if (showToast) this.toast.show('Saved to battle card history.');
      },
      error: err => {
        console.error(err);
        this.saving.set(false);
        this.toast.show('Could not save to database.');
      }
    });
  }

  private linkToOpportunity(doc: GeneratedDocument): void {
    const oppId = this.selectedOpportunityId();
    if (!oppId) return;
    this.api.linkOpportunityDocument({
      opportunityId: oppId,
      generationId: doc.generationId,
      documentType: 'BattleCard',
      title: doc.title
    }).subscribe({
      next: () => this.toast.show('Linked battle card to pursuit.'),
      error: err => console.error(err)
    });
  }

  private exportCurrent(format: 'docx' | 'pptx'): void {
    const doc = this.result();
    if (!doc) return;
    this.exporting.set(true);
    this.api.exportGeneration({
      generationId: doc.generationId,
      userObjectId: this.teams.user().objectId,
      format,
      document: { ...doc, status: 'Approved' },
      requireApproved: false
    }).subscribe({
      next: result => {
        void this.api.downloadExport(result)
          .then(() => {
            this.exporting.set(false);
            this.toast.show(`Downloaded ${result.fileName}`);
          })
          .catch(err => {
            console.error(err);
            this.exporting.set(false);
          });
      },
      error: err => {
        console.error(err);
        this.exporting.set(false);
        this.toast.show(err?.error?.detail ?? 'Export failed.');
      }
    });
  }
}
