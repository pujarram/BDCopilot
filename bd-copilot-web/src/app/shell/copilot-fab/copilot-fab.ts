import { Component, inject } from '@angular/core';
import { CopilotPanelStateService } from '../../core/services/copilot-panel-state.service';
import { TietoIconComponent } from '../../shared/tieto-icon.component';

@Component({
  selector: 'app-copilot-fab',
  imports: [TietoIconComponent],
  styleUrl: './copilot-fab.scss',
  template: `
    @if (!panelState.open()) {
      <button
        type="button"
        class="copilot-fab"
        aria-label="Open BD Copilot chat"
        (click)="panelState.openPanel()">
        <tieto-icon name="copilot" />
      </button>
    }
  `
})
export class CopilotFab {
  protected readonly panelState = inject(CopilotPanelStateService);
}
