import { Injectable, inject, signal } from '@angular/core';
import { app } from '@microsoft/teams-js';
import { AuthService } from './auth.service';

export interface CopilotUser {
  objectId: string;
  displayName: string;
  runningInTeams: boolean;
}

const DEMO_USER: CopilotUser = {
  objectId: 'demo-user-0000-0000-0000-000000000000',
  displayName: 'Demo User (running outside Teams)',
  runningInTeams: false
};

/**
 * Wraps @microsoft/teams-js so the rest of the app only ever depends on a plain CopilotUser
 * signal. Outside Teams, prefers Entra oid from AuthService when signed in.
 */
@Injectable({ providedIn: 'root' })
export class TeamsService {
  private readonly auth = inject(AuthService);
  readonly user = signal<CopilotUser>(DEMO_USER);
  readonly ready = signal(false);

  async initialize(): Promise<void> {
    const isEmbedded = window.self !== window.top;

    if (!isEmbedded) {
      this.applyAuthIdentity();
      this.ready.set(true);
      return;
    }

    try {
      await app.initialize();
      const context = await app.getContext();

      this.user.set({
        objectId: context.user?.id ?? DEMO_USER.objectId,
        displayName: context.user?.displayName ?? context.user?.userPrincipalName ?? 'Teams User',
        runningInTeams: true
      });

      app.notifySuccess();
    } catch (err) {
      console.warn('Teams SDK initialization failed, falling back to demo identity.', err);
      this.applyAuthIdentity();
    } finally {
      this.ready.set(true);
    }
  }

  /** Call after login so RAG requests use Entra oid instead of the demo user. */
  applyAuthIdentity(): void {
    if (this.user().runningInTeams) return;
    const oid = this.auth.objectId();
    if (oid) {
      this.user.set({
        objectId: oid,
        displayName: this.auth.displayName() ?? 'Entra User',
        runningInTeams: false
      });
    } else {
      this.user.set(DEMO_USER);
    }
  }
}
