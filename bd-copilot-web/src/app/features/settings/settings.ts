import { CUSTOM_ELEMENTS_SCHEMA, Component, OnInit, inject, signal } from '@angular/core';
import { ApiService } from '../../core/services/api.service';
import { AiProviderStatus } from '../../core/models/api-models';

@Component({
  selector: 'app-settings',
  templateUrl: './settings.html',
  schemas: [CUSTOM_ELEMENTS_SCHEMA]
})
export class Settings implements OnInit {
  private readonly api = inject(ApiService);

  protected readonly status = signal<AiProviderStatus | null>(null);
  protected readonly loading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);

  ngOnInit(): void {
    this.api.getAiProviderStatus().subscribe({
      next: status => { this.status.set(status); this.loading.set(false); },
      error: err => {
        this.errorMessage.set('Could not reach BD Copilot API — see the browser console for details.');
        console.error(err);
        this.loading.set(false);
      }
    });
  }
}
