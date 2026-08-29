import { Injectable, signal } from '@angular/core';
import { CorpusSource } from '../models/api-models';

export type ReuseTarget = 'rfp' | 'business-case';

export interface SearchReusePayload {
  target: ReuseTarget;
  query: string;
  fileName: string;
  locator?: string | null;
  excerpt: string;
  sharePointUrl?: string | null;
  corpusSource: CorpusSource;
  /** Short label for the banner */
  label: string;
}

/**
 * Hands Knowledge Search “Use in RFP / Business Case” context to the generators.
 */
@Injectable({ providedIn: 'root' })
export class SearchReuseService {
  private readonly pending = signal<SearchReusePayload | null>(null);

  set(payload: SearchReusePayload): void {
    this.pending.set(payload);
  }

  /** Read and clear one-shot reuse context for a target page. */
  consume(target: ReuseTarget): SearchReusePayload | null {
    const current = this.pending();
    if (!current || current.target !== target) return null;
    this.pending.set(null);
    return current;
  }

  clear(): void {
    this.pending.set(null);
  }
}
