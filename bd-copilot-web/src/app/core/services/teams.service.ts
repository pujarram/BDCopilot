import { Injectable, signal } from '@angular/core';
import { app } from '@microsoft/teams-js';

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
 * signal. When the app isn't hosted inside a Teams iframe (e.g. `ng serve` during local
 * development) it falls back to a demo identity instead of hanging on the Teams handshake —
 * every API call in this scaffold takes a userObjectId, and this is what supplies it.
 */
@Injectable({ providedIn: 'root' })
export class TeamsService {
  readonly user = signal<CopilotUser>(DEMO_USER);
  readonly ready = signal(false);

  async initialize(): Promise<void> {
    const isEmbedded = window.self !== window.top;

    if (!isEmbedded) {
      // Not inside a Teams (or Teams-like) host frame — nothing to hand-shake with.
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
      // Handshake failed (e.g. loaded in a plain iframe that isn't actually Teams) — degrade
      // to the demo identity rather than blocking the whole app on a Teams host that will
      // never respond.
      console.warn('Teams SDK initialization failed, falling back to demo identity.', err);
    } finally {
      this.ready.set(true);
    }
  }
}
