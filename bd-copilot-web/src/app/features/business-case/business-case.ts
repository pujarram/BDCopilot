import { CUSTOM_ELEMENTS_SCHEMA, Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { ApiService } from '../../core/services/api.service';
import { TeamsService } from '../../core/services/teams.service';
import { ToastService } from '../../core/services/toast.service';
import { SearchReuseService, SearchReusePayload } from '../../core/services/search-reuse.service';
import {
  GeneratedDocument,
  GeneratedDocumentHistory,
  GeneratedDocumentHistoryListItem,
  GeneratedSection,
  RfpStreamEvent,
  CorpusSource
} from '../../core/models/api-models';

type StreamStatus = 'queued' | 'writing-title' | 'generating' | 'done' | 'error';

interface StreamSection {
  order: number;
  title: string;
  displayTitle: string;
  content: string;
  status: StreamStatus;
  sources: GeneratedSection['sources'];
}

const HISTORY_TYPE = 'business-case';

@Component({
  selector: 'app-business-case',
  templateUrl: './business-case.html',
  imports: [DatePipe],
  schemas: [CUSTOM_ELEMENTS_SCHEMA]
})
export class BusinessCase implements OnInit, OnDestroy {
  private readonly api = inject(ApiService);
  private readonly teams = inject(TeamsService);
  private readonly toast = inject(ToastService);
  private readonly searchReuse = inject(SearchReuseService);

  protected readonly initiative = signal('AI Wealth Copilot Platform');
  protected readonly audience = signal('ExecutiveSponsor');
  protected readonly corpusSource = signal<CorpusSource>('Online');
  protected readonly focusNotes = signal<string | null>(null);
  protected readonly reuseBanner = signal<SearchReusePayload | null>(null);

  protected readonly generating = signal(false);
  protected readonly streaming = signal(false);
  protected readonly approving = signal(false);
  protected readonly exporting = signal(false);
  protected readonly saving = signal(false);
  protected readonly channelBusy = signal(false);
  protected readonly result = signal<GeneratedDocument | null>(null);
  protected readonly savedRow = signal<GeneratedDocumentHistory | null>(null);
  protected readonly history = signal<GeneratedDocumentHistoryListItem[]>([]);
  protected readonly historyDetail = signal<GeneratedDocumentHistory | null>(null);
  protected readonly historySections = signal<GeneratedSection[]>([]);
  protected readonly streamSections = signal<StreamSection[]>([]);
  protected readonly selectedSection = signal<GeneratedSection | null>(null);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly progressLabel = signal('');

  private abort: AbortController | null = null;

  ngOnInit(): void {
    this.applySearchReuse();
    this.refreshHistory();
  }

  protected clearReuse(): void {
    this.reuseBanner.set(null);
    this.focusNotes.set(null);
  }

  private applySearchReuse(): void {
    const payload = this.searchReuse.consume('business-case');
    if (!payload) return;

    this.reuseBanner.set(payload);
    this.focusNotes.set(payload.excerpt);
    this.corpusSource.set(payload.corpusSource);
    if (payload.query) {
      this.initiative.set(payload.query);
    }
    this.toast.show(`Reusing “${payload.label}” from Knowledge Search.`);
  }

  ngOnDestroy(): void {
    this.abort?.abort();
  }

  protected generate(): void {
    this.abort?.abort();
    this.abort = new AbortController();
    this.generating.set(true);
    this.streaming.set(true);
    this.errorMessage.set(null);
    this.result.set(null);
    this.savedRow.set(null);
    this.selectedSection.set(null);
    this.streamSections.set([]);
    this.progressLabel.set('Connecting to BD Copilot…');
    void this.runLiveStream(this.abort.signal);
  }

  protected isApproved(): boolean {
    return this.result()?.status === 'Approved';
  }

  protected approve(): void {
    const doc = this.result();
    if (!doc) return;
    this.approving.set(true);
    this.api.approveGeneration({
      generationId: doc.generationId,
      userObjectId: this.teams.user().objectId,
      document: doc
    }).subscribe({
      next: updated => {
        this.result.set(updated);
        this.approving.set(false);
        this.persistToDatabase(updated);
        this.toast.show('Draft approved — export is now enabled.');
      },
      error: err => {
        console.error(err);
        this.approving.set(false);
        this.toast.show('Could not approve draft.');
      }
    });
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

  protected saveToBdChannel(): void {
    const saved = this.savedRow();
    if (!saved) {
      const doc = this.result();
      if (!doc) return;
      this.saving.set(true);
      this.api.saveDocumentHistory(HISTORY_TYPE, {
        document: doc,
        userObjectId: this.teams.user().objectId,
        displayName: this.teams.user().displayName,
        metadata: { initiative: this.initiative(), audience: this.audience() }
      }).subscribe({
        next: row => {
          this.savedRow.set(row);
          this.saving.set(false);
          this.uploadChannel(row.id);
        },
        error: err => {
          console.error(err);
          this.saving.set(false);
          this.toast.show('Could not save to database before channel upload.');
        }
      });
      return;
    }
    this.uploadChannel(saved.id);
  }

  protected viewSection(section: GeneratedSection | StreamSection): void {
    if (!section.content) return;
    this.selectedSection.set({
      order: section.order,
      title: section.title,
      content: section.content,
      sources: section.sources ?? []
    });
    this.logFeedbackQuiet('Edit', section.title);
  }

  protected discard(): void {
    this.abort?.abort();
    this.logFeedbackQuiet('Discard');
    this.result.set(null);
    this.savedRow.set(null);
    this.streamSections.set([]);
    this.selectedSection.set(null);
    this.streaming.set(false);
    this.generating.set(false);
    this.progressLabel.set('');
  }

  protected refreshHistory(): void {
    this.api.listDocumentHistory(HISTORY_TYPE).subscribe({
      next: rows => this.history.set(rows),
      error: err => console.error(err)
    });
  }

  protected openHistory(item: GeneratedDocumentHistoryListItem): void {
    this.api.getDocumentHistory(HISTORY_TYPE, item.id).subscribe({
      next: row => {
        this.historyDetail.set(row);
        this.historySections.set(this.parseSections(row));
      },
      error: err => {
        console.error(err);
        this.toast.show('Could not load history item.');
      }
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
            this.refreshHistory();
          })
          .catch(err => {
            console.error(err);
            this.exporting.set(false);
            this.toast.show('Download failed after export.');
          });
      },
      error: err => {
        console.error(err);
        this.exporting.set(false);
        this.toast.show(err?.error?.detail ?? err?.error?.title ?? 'Export failed.');
      }
    });
  }

  protected channelHistory(item: GeneratedDocumentHistoryListItem): void {
    this.uploadChannel(item.id);
  }

  protected closeHistoryDetail(): void {
    this.historyDetail.set(null);
    this.historySections.set([]);
  }

  private uploadChannel(id: string): void {
    this.channelBusy.set(true);
    this.api.saveDocumentHistoryToChannel(HISTORY_TYPE, id, this.teams.user().objectId).subscribe({
      next: row => {
        this.channelBusy.set(false);
        this.savedRow.set(row);
        this.refreshHistory();
        if (row.channelUploadStatus === 'Uploaded') {
          this.toast.show('Uploaded to BD channel Business Cases folder.');
        } else if (row.channelUploadStatus === 'SkippedNoGraph') {
          this.toast.show('Saved in DB. Channel upload waits for Graph / BdChannelDriveId.');
        } else {
          this.toast.show(row.channelUploadError ?? 'Channel upload status updated.');
        }
      },
      error: err => {
        console.error(err);
        this.channelBusy.set(false);
        this.toast.show('Save to BD channel failed.');
      }
    });
  }

  private persistToDatabase(doc: GeneratedDocument, showToast = false): void {
    this.saving.set(true);
    this.api.saveDocumentHistory(HISTORY_TYPE, {
      document: doc,
      userObjectId: this.teams.user().objectId,
      displayName: this.teams.user().displayName,
      metadata: { initiative: this.initiative(), audience: this.audience() }
    }).subscribe({
      next: row => {
        this.savedRow.set(row);
        this.saving.set(false);
        this.refreshHistory();
        if (showToast) this.toast.show('Saved to business case history.');
      },
      error: err => {
        console.error(err);
        this.saving.set(false);
        this.toast.show('Could not save to database.');
      }
    });
  }

  private async runLiveStream(signal: AbortSignal): Promise<void> {
    try {
      const stream = this.api.generateBusinessCaseStream(
        {
          initiative: this.initiative(),
          audience: this.audience(),
          userObjectId: this.teams.user().objectId,
          corpusSource: this.corpusSource(),
          focusNotes: this.focusNotes() ?? undefined
        },
        signal
      );
      for await (const evt of stream) {
        if (signal.aborted) return;
        this.applyStreamEvent(evt);
      }
    } catch (err) {
      if (signal.aborted) return;
      console.error(err);
      this.errorMessage.set(
        'Could not stream from BD Copilot API. Confirm the API was restarted after the stream endpoints were added.'
      );
      this.progressLabel.set('');
      this.generating.set(false);
      this.streaming.set(false);
    }
  }

  private applyStreamEvent(evt: RfpStreamEvent): void {
    switch (evt.type) {
      case 'outline': {
        const titles = evt.sectionTitles ?? [];
        this.progressLabel.set('Outline ready — retrieving sources…');
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
          this.persistToDatabase(evt.document);
        }
        this.generating.set(false);
        this.streaming.set(false);
        this.progressLabel.set('Draft ready for review');
        this.toast.show(`Generated ${evt.document?.sections.length ?? 0} sections.`);
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

  private exportCurrent(format: 'docx' | 'pptx' | 'zip'): void {
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
            this.result.update(d => (d ? { ...d, status: 'Approved' } : d));
            this.toast.show(`Downloaded ${result.fileName}`);
          })
          .catch(err => {
            console.error(err);
            this.exporting.set(false);
            window.location.href = this.api.resolveDownloadUrl(result.downloadUrl);
            this.toast.show('Opening download…');
          });
      },
      error: err => {
        console.error(err);
        this.exporting.set(false);
        this.toast.show(err?.error?.detail ?? err?.error?.title ?? 'Export failed.');
      }
    });
  }

  private parseSections(row: GeneratedDocumentHistory): GeneratedSection[] {
    if (row.sections?.length) return row.sections;
    if (!row.sectionsJson) return [];
    try {
      return JSON.parse(row.sectionsJson) as GeneratedSection[];
    } catch {
      return [];
    }
  }

  private logFeedbackQuiet(action: string, sectionTitle?: string): void {
    const doc = this.result();
    if (!doc) return;
    this.api.logFeedback({
      generationId: doc.generationId,
      userObjectId: this.teams.user().objectId,
      action,
      sectionTitle
    }).subscribe({ error: err => console.error(err) });
  }
}
