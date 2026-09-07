import { CUSTOM_ELEMENTS_SCHEMA, Component, OnInit, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { ApiService } from '../../core/services/api.service';
import { TeamsService } from '../../core/services/teams.service';
import { ToastService } from '../../core/services/toast.service';
import { CorpusSource, DocumentListItem, SyncHealthStatus } from '../../core/models/api-models';

@Component({
  selector: 'app-document-library',
  templateUrl: './document-library.html',
  imports: [DatePipe],
  schemas: [CUSTOM_ELEMENTS_SCHEMA]
})
export class DocumentLibrary implements OnInit {
  private readonly api = inject(ApiService);
  private readonly teams = inject(TeamsService);
  private readonly toast = inject(ToastService);

  protected readonly documents = signal<DocumentListItem[]>([]);
  protected readonly syncHealth = signal<SyncHealthStatus | null>(null);
  protected readonly loading = signal(true);
  protected readonly syncBusy = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly corpusSource = signal<CorpusSource | 'All'>('All');
  protected readonly staleCount = computed(() => this.documents().filter(d => d.isStale).length);
  protected readonly staleMonths = computed(() => this.documents().find(d => d.staleAfterMonths)?.staleAfterMonths ?? 6);

  ngOnInit(): void {
    this.loadSyncHealth();
    this.loadDocuments();
  }

  protected setCorpus(source: CorpusSource | 'All'): void {
    this.corpusSource.set(source);
    this.loadDocuments();
  }

  protected seedDocuments(): void {
    this.syncBusy.set(true);
    this.api.seedDocuments().subscribe({
      next: health => {
        this.syncHealth.set(health);
        this.syncBusy.set(false);
        this.toast.show('Pilot documents seeded.');
        this.loadDocuments();
      },
      error: err => {
        console.error(err);
        this.syncBusy.set(false);
        this.toast.show('Seed failed — see the browser console.');
      }
    });
  }

  protected runSync(): void {
    this.syncBusy.set(true);
    this.api.runSync().subscribe({
      next: health => {
        this.syncHealth.set(health);
        this.syncBusy.set(false);
        this.toast.show('Sync completed.');
        this.loadDocuments();
      },
      error: err => {
        console.error(err);
        this.syncBusy.set(false);
        this.toast.show('Sync failed — see the browser console.');
      }
    });
  }

  protected indexLocalDocs(): void {
    this.syncBusy.set(true);
    this.api.syncLocalDocs().subscribe({
      next: result => {
        this.syncBusy.set(false);
        this.toast.show(result.statusMessage || 'Local docs indexed.');
        this.loadDocuments();
      },
      error: err => {
        console.error(err);
        this.syncBusy.set(false);
        this.toast.show('Local index failed — see the browser console.');
      }
    });
  }

  private loadSyncHealth(): void {
    this.api.getSyncHealth().subscribe({
      next: health => this.syncHealth.set(health),
      error: err => console.error(err)
    });
  }

  private loadDocuments(): void {
    this.loading.set(true);
    this.api.listDocuments(this.teams.user().objectId, this.corpusSource()).subscribe({
      next: docs => { this.documents.set(docs); this.loading.set(false); },
      error: err => {
        this.errorMessage.set('Could not reach BD Copilot API — see the browser console for details.');
        console.error(err);
        this.loading.set(false);
      }
    });
  }
}
