import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { from, switchMap } from 'rxjs';
import { AuthService } from '../services/auth.service';

/** Attaches Entra Bearer JWT or pilot admin token for protected API routes. */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  if (!auth.isLoggedIn()) {
    return next(req);
  }

  const isApi = req.url.includes('/api/');
  if (!isApi) {
    return next(req);
  }

  // Auth config / login must stay anonymous
  if (req.url.includes('/auth/config') || req.url.includes('/auth/login')) {
    return next(req);
  }

  return from(auth.getAccessToken()).pipe(
    switchMap(token => {
      if (!token) {
        return next(req);
      }

      if (auth.authMode() === 'entra') {
        return next(
          req.clone({
            setHeaders: { Authorization: `Bearer ${token}` }
          })
        );
      }

      const needsAdmin =
        req.url.includes('/admin/') ||
        req.url.includes('/dashboard/') ||
        req.url.includes('/auth/me');
      if (needsAdmin) {
        return next(
          req.clone({
            setHeaders: { 'X-Bd-Admin-Token': token }
          })
        );
      }

      return next(req);
    })
  );
};
