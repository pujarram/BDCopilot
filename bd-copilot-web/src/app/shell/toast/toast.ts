import { Component, inject } from '@angular/core';
import { ToastService } from '../../core/services/toast.service';

@Component({
  selector: 'app-toast',
  template: `
    <div class="toast" [class.show]="service.message() !== null">
      {{ service.message() }}
    </div>
  `
})
export class Toast {
  protected readonly service = inject(ToastService);
}
