import {
  HttpErrorResponse,
  HttpEvent,
  HttpInterceptorFn,
  HttpRequest,
  HttpResponse,
} from '@angular/common/http';
import { inject } from '@angular/core';
import { Observable, from, of, switchMap, throwError } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { AuthService } from './auth.service';
import { ReauthService } from './reauth.service';

/** Endpoints that manage the session themselves. */
const sessionEndpoints = [
  '/rest/v1/auth/login',
  '/rest/v1/auth/refresh',
  '/rest/v1/auth/logout',
  '/rest/v1/setup/',
];

/**
 * Adds the access token to API calls and recovers from the two expected session errors, for REST (status and
 * problem code) and GraphQL (error code in a 200 or 500 response):
 * `unauthorized` refreshes the token once and retries; `reauthentication_required` asks for the password and retries.
 */
export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const isApi = request.url.startsWith('/rest/') || request.url.startsWith('/graphql');
  if (!isApi || sessionEndpoints.some((endpoint) => request.url.startsWith(endpoint)))
    return next(request);

  const auth = inject(AuthService);
  const reauth = inject(ReauthService);

  const send = (): Observable<HttpEvent<unknown>> => next(withToken(request, auth.accessToken));
  // One recovery per request: a retry that fails the same way is reported, not retried again.
  let recovered = false;

  return send().pipe(
    switchMap((event) => {
      // GraphQL reports errors in the body, often with status 200.
      const code = event instanceof HttpResponse ? graphQLErrorCode(event.body) : null;
      return code ? recover(code, event) : of(event);
    }),
    catchError((error: unknown) => {
      const code = error instanceof HttpErrorResponse ? errorCode(error.error) : null;
      return code ? recover(code, null, error) : throwError(() => error);
    }),
  );

  function recover(
    code: string,
    event: HttpEvent<unknown> | null,
    error?: unknown,
  ): Observable<HttpEvent<unknown>> {
    const fail = (): Observable<HttpEvent<unknown>> =>
      event ? of(event) : throwError(() => error);
    if (recovered) return fail();
    recovered = true;

    if (code === 'unauthorized') {
      return from(auth.refresh()).pipe(
        switchMap((refreshed) => {
          if (refreshed) return send();
          void auth.expire();
          return fail();
        }),
      );
    }

    if (code === 'reauthentication_required') {
      return from(reauth.prompt()).pipe(switchMap((confirmed) => (confirmed ? send() : fail())));
    }

    return fail();
  }
};

function withToken(request: HttpRequest<unknown>, token: string | null): HttpRequest<unknown> {
  return token ? request.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : request;
}

function errorCode(body: unknown): string | null {
  return (body as { code?: string } | null)?.code ?? graphQLErrorCode(body);
}

function graphQLErrorCode(body: unknown): string | null {
  const errors = (body as { errors?: { extensions?: { code?: string } }[] } | null)?.errors;
  return errors?.[0]?.extensions?.code ?? null;
}
