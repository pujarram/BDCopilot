import { Injectable, signal } from '@angular/core';

/** Shared mobile-nav open state between the Tieto header menu button and the prototype rail. */
@Injectable({ providedIn: 'root' })
export class MenuStateService {
  readonly open = signal(false);

  toggle(): void {
    this.open.update(v => !v);
  }

  close(): void {
    this.open.set(false);
  }
}
