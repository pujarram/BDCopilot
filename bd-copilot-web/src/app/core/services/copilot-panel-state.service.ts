import { Injectable, signal } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class CopilotPanelStateService {
  readonly open = signal(false);
  readonly readAloud = signal(false);

  toggle(): void {
    this.open.update(v => !v);
  }

  openPanel(): void {
    this.open.set(true);
  }

  close(): void {
    this.open.set(false);
  }
}
