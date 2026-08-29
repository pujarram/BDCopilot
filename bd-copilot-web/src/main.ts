import { bootstrapApplication } from '@angular/platform-browser';
import { appConfig } from './app/app.config';
import { App } from './app/app';
import { initializeFluentDesignSystem } from './app/fluent-setup';

initializeFluentDesignSystem();

bootstrapApplication(App, appConfig)
  .catch((err) => console.error(err));
