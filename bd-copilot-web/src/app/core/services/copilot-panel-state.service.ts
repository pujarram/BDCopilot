import { Injectable, signal } from '@angular/core';

const READ_ALOUD_KEY = 'bd-copilot-read-aloud';

@Injectable({ providedIn: 'root' })
export class CopilotPanelStateService {
  readonly open = signal(false);
  readonly readAloud = signal(this.readStoredReadAloud());

  toggle(): void {
    this.open.update(v => !v);
  }

  openPanel(): void {
    this.open.set(true);
  }

  close(): void {
    this.open.set(false);
  }

  setReadAloud(value: boolean): void {
    this.readAloud.set(value);
    try {
      localStorage.setItem(READ_ALOUD_KEY, value ? '1' : '0');
    } catch {
      // ignore
    }
  }

  private readStoredReadAloud(): boolean {
    try {
      return localStorage.getItem(READ_ALOUD_KEY) === '1';
    } catch {
      return false;
    }
  }
}
