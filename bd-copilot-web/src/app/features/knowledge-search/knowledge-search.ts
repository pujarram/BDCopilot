import { CUSTOM_ELEMENTS_SCHEMA, Component, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { generateRoute } from '../../shared/generate-doc-types';
import { ApiService } from '../../core/services/api.service';
import { TeamsService } from '../../core/services/teams.service';
import { ToastService } from '../../core/services/toast.service';
import { SearchReuseService } from '../../core/services/search-reuse.service';
import {
  Citation,
  CorpusSource,
  KnowledgeSearchResponse,
  SearchResultItem
} from '../../core/models/api-models';
import { AiStreamText } from '../../shared/ai-stream-text.component';

@Component({
  selector: 'app-knowledge-search',
  templateUrl: './knowledge-search.html',
  imports: [AiStreamText],
  schemas: [CUSTOM_ELEMENTS_SCHEMA]
})
export class KnowledgeSearch {
  private readonly api = inject(ApiService);
  private readonly teams = inject(TeamsService);
  private readonly toast = inject(ToastService);
  private readonly reuse = inject(SearchReuseService);
  private readonly router = inject(Router);

  protected readonly query = signal('Wealth Copilot');
  protected readonly corpusSource = signal<CorpusSource>('Online');
  protected readonly searching = signal(false);
  protected readonly answer = signal<string | null>(null);
  protected readonly citations = signal<Citation[]>([]);
  protected readonly results = signal<SearchResultItem[]>([]);
  protected readonly modelLabel = signal('');
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly hasSearched = signal(false);

  protected onInput(event: Event): void {
    this.query.set((event.target as HTMLInputElement).value ?? '');
  }

  protected onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter') this.search();
  }

  protected search(): void {
    const q = this.query().trim();
    if (!q) return;

    this.searching.set(true);
    this.errorMessage.set(null);
    this.answer.set(null);
    this.citations.set([]);
    this.results.set([]);

    this.api.searchAsk({
      query: q,
      userObjectId: this.teams.user().objectId,
      topK: 8,
      corpusSource: this.corpusSource()
    }).subscribe({
      next: (res: KnowledgeSearchResponse) => {
        this.answer.set(res.answer);
        this.citations.set(res.citations ?? []);
        this.results.set(res.results ?? []);
        this.modelLabel.set(`${res.aiProvider} · ${res.model}`);
        this.hasSearched.set(true);
        this.searching.set(false);
      },
      error: err => {
        this.errorMessage.set(
          err?.error?.detail ||
            'Could not reach BD Copilot API — confirm the API and Ollama are running.'
        );
        console.error(err);
        this.searching.set(false);
        this.hasSearched.set(true);
      }
    });
  }

  protected openCitation(c: Citation): void {
    this.api.openCitation(c, this.teams.user().objectId);
  }

  protected citationUrl(c: Citation): string | null {
    return this.api.resolveCitationOpenUrl(c, this.teams.user().objectId);
  }

  protected openSource(item: SearchResultItem): void {
    this.api.openCitation(item.source, this.teams.user().objectId);
  }

  protected useInRfp(item: SearchResultItem): void {
    this.handOff('rfp', item);
  }

  protected useInBusinessCase(item: SearchResultItem): void {
    this.handOff('business-case', item);
  }

  protected useInBattleCard(item: SearchResultItem): void {
    this.handOff('battle-card', item);
  }

  protected useAnswerInRfp(): void {
    const first = this.results()[0];
    if (!first) {
      this.toast.show('Search first so we have sources to reuse.');
      return;
    }
    this.handOff('rfp', first, this.answer() ?? undefined);
  }

  protected useAnswerInBusinessCase(): void {
    const first = this.results()[0];
    if (!first) {
      this.toast.show('Search first so we have sources to reuse.');
      return;
    }
    this.handOff('business-case', first, this.answer() ?? undefined);
  }

  private handOff(
    target: 'rfp' | 'business-case' | 'battle-card',
    item: SearchResultItem,
    answerOverlay?: string
  ): void {
    const excerpt = answerOverlay
      ? `${answerOverlay.trim()}\n\n— Primary source: ${item.source.fileName}${item.source.locator ? ' · ' + item.source.locator : ''}\n${item.excerpt}`
      : item.excerpt;

    const label = `${item.source.fileName}${item.source.locator ? ' · ' + item.source.locator : ''}`;

    this.reuse.set({
      target,
      query: this.query().trim(),
      fileName: item.source.fileName,
      locator: item.source.locator,
      excerpt,
      sharePointUrl: item.source.sharePointUrl,
      corpusSource: this.corpusSource(),
      label
    });

    const path = generateRoute(
      target === 'battle-card' ? 'battle-card' : target === 'rfp' ? 'rfp' : 'business-case'
    );
    void this.router.navigateByUrl(path);
    this.toast.show(
      target === 'rfp'
        ? 'Opening RFP with this source…'
        : target === 'battle-card'
          ? 'Opening Battle Card with this source…'
          : 'Opening Business Case with this source…'
    );
  }
}
