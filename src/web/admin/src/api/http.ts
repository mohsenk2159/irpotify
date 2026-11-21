/**
 * Low-level typed HTTP client.
 *
 * Design constraints:
 * - access tokens are supplied by the caller (kept in memory, never in localStorage);
 * - a 401 triggers exactly one single-flight refresh, then a single retry;
 * - problem+json, `{ error }` payloads and plain text are all normalised into ApiError;
 * - tokens, authorization headers and response bodies are never logged.
 */

export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  instance?: string;
  traceId?: string;
  error?: string;
  rejected?: string[];
  [key: string]: unknown;
}

export class ApiError extends Error {
  readonly status: number;
  readonly problem: ProblemDetails | null;
  readonly isNetworkError: boolean;
  readonly retryAfterSeconds: number | null;

  constructor(
    status: number,
    message: string,
    problem: ProblemDetails | null = null,
    extra: { cause?: unknown; isNetworkError?: boolean; retryAfterSeconds?: number | null } = {},
  ) {
    super(message, extra.cause === undefined ? undefined : { cause: extra.cause });
    this.name = 'ApiError';
    this.status = status;
    this.problem = problem;
    this.isNetworkError = extra.isNetworkError ?? false;
    this.retryAfterSeconds = extra.retryAfterSeconds ?? null;
  }

  get isUnauthorized(): boolean {
    return this.status === 401;
  }

  get isForbidden(): boolean {
    return this.status === 403;
  }

  get isNotFound(): boolean {
    return this.status === 404;
  }
}

export type FetchLike = (input: string, init: RequestInit) => Promise<Response>;

export interface SessionLike {
  accessToken: string;
  refreshToken: string;
}

export interface HttpClientOptions {
  baseUrl?: string;
  fetchImpl?: FetchLike;
  getAccessToken: () => string | null;
  getRefreshToken: () => string | null;
  /** Performs the actual refresh call. Must not go through 401 retry logic. */
  performRefresh?: (refreshToken: string) => Promise<SessionLike>;
  onSessionRefreshed?: (session: SessionLike) => void;
  onSessionExpired?: () => void;
}

export type QueryValue = string | number | boolean | null | undefined;

export interface RequestOptions {
  method?: 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE';
  query?: Record<string, QueryValue>;
  json?: unknown;
  formData?: FormData;
  headers?: Record<string, string>;
  signal?: AbortSignal;
  /** Attach the access token (default true). */
  auth?: boolean;
  /** Retry once after a successful refresh (default true). */
  retryOnUnauthorized?: boolean;
}

const STATUS_MESSAGES: Record<number, string> = {
  400: 'The request was rejected.',
  401: 'Authentication is required.',
  403: 'You do not have permission to perform this action.',
  404: 'The requested resource was not found.',
  409: 'The request conflicts with the current state of the server.',
  413: 'The uploaded content is too large.',
  415: 'The request content type is not supported.',
  422: 'The server could not process the request.',
  423: 'The account is locked.',
  429: 'Too many requests. Please wait and try again.',
  500: 'The server encountered an unexpected error.',
  502: 'The server is temporarily unavailable.',
  503: 'The service is unavailable.',
};

export function statusMessage(status: number): string {
  return STATUS_MESSAGES[status] ?? `Request failed with status ${status}.`;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null;
}

/** Extracts a human-readable message from problem+json or an `{ error }` payload. */
export function messageFromPayload(payload: unknown, status: number): string {
  if (isRecord(payload)) {
    const detail = payload['detail'];
    if (typeof detail === 'string' && detail.trim().length > 0) return detail;
    const error = payload['error'];
    if (typeof error === 'string' && error.trim().length > 0) return error;
    const title = payload['title'];
    const rejected = payload['rejected'];
    if (typeof title === 'string' && title.trim().length > 0) {
      if (Array.isArray(rejected) && rejected.length > 0) {
        return `${title} (${rejected.filter((item): item is string => typeof item === 'string').join(', ')})`;
      }
      return title;
    }
  }
  if (typeof payload === 'string' && payload.trim().length > 0 && payload.length <= 500) return payload.trim();
  return statusMessage(status);
}

function parseRetryAfter(response: Response): number | null {
  const raw = response.headers.get('retry-after');
  if (!raw) return null;
  const seconds = Number(raw);
  if (Number.isFinite(seconds) && seconds >= 0) return Math.round(seconds);
  const date = Date.parse(raw);
  if (Number.isNaN(date)) return null;
  return Math.max(0, Math.round((date - Date.now()) / 1000));
}

function isAbortError(error: unknown): boolean {
  return error instanceof DOMException ? error.name === 'AbortError' : isRecord(error) && error['name'] === 'AbortError';
}

async function readPayload(response: Response): Promise<{ payload: unknown; isJson: boolean }> {
  const contentType = response.headers.get('content-type') ?? '';
  const isJson = contentType.toLowerCase().includes('json');
  if (response.status === 204 || response.status === 205) return { payload: null, isJson };
  const text = await response.text().catch(() => '');
  if (!text) return { payload: null, isJson };
  if (isJson) {
    try {
      return { payload: JSON.parse(text) as unknown, isJson };
    } catch {
      return { payload: text, isJson: false };
    }
  }
  return { payload: text, isJson };
}

export function buildUrl(baseUrl: string, path: string, query?: Record<string, QueryValue>): string {
  const prefix = baseUrl.endsWith('/') ? baseUrl.slice(0, -1) : baseUrl;
  const url = `${prefix}${path.startsWith('/') ? path : `/${path}`}`;
  if (!query) return url;
  const params = new URLSearchParams();
  for (const [key, value] of Object.entries(query)) {
    if (value === undefined || value === null || value === '') continue;
    params.append(key, String(value));
  }
  const queryString = params.toString();
  return queryString ? `${url}?${queryString}` : url;
}

export interface HttpClient {
  request<T>(path: string, options?: RequestOptions): Promise<T>;
  requestBlob(path: string, options?: RequestOptions): Promise<Blob>;
  refreshAccessToken(): Promise<string | null>;
}

export function createHttpClient(options: HttpClientOptions): HttpClient {
  const baseUrl = options.baseUrl ?? '';
  const fetchImpl: FetchLike =
    options.fetchImpl ?? ((input, init) => fetch(input, init));
  const getAccessToken = options.getAccessToken;

  let refreshInFlight: Promise<string | null> | null = null;

  function expireSession(): null {
    options.onSessionExpired?.();
    return null;
  }

  async function refreshAccessToken(): Promise<string | null> {
    if (refreshInFlight) return refreshInFlight;
    const refreshToken = options.getRefreshToken();
    if (!refreshToken || !options.performRefresh) {
      return expireSession();
    }
    const performRefresh = options.performRefresh;
    refreshInFlight = (async () => {
      try {
        const session = await performRefresh(refreshToken);
        options.onSessionRefreshed?.(session);
        return session.accessToken;
      } catch {
        return expireSession();
      } finally {
        refreshInFlight = null;
      }
    })();
    return refreshInFlight;
  }

  async function send(path: string, requestOptions: RequestOptions, accessToken: string | null): Promise<Response> {
    const headers: Record<string, string> = { Accept: 'application/json', ...(requestOptions.headers ?? {}) };
    if (accessToken) headers['Authorization'] = `Bearer ${accessToken}`;
    let body: BodyInit | undefined;
    if (requestOptions.formData) {
      body = requestOptions.formData;
    } else if (requestOptions.json !== undefined) {
      headers['Content-Type'] = 'application/json';
      body = JSON.stringify(requestOptions.json);
    }
    try {
      return await fetchImpl(buildUrl(baseUrl, path, requestOptions.query), {
        method: requestOptions.method ?? 'GET',
        headers,
        body,
        signal: requestOptions.signal,
        credentials: 'same-origin',
        cache: 'no-store',
      });
    } catch (error) {
      if (isAbortError(error)) throw error;
      throw new ApiError(0, 'The dashboard could not reach the MusicHub API.', null, {
        cause: error,
        isNetworkError: true,
      });
    }
  }

  async function request<T>(path: string, requestOptions: RequestOptions = {}): Promise<T> {
    const auth = requestOptions.auth ?? true;
    const retry = requestOptions.retryOnUnauthorized ?? true;
    let response = await send(path, requestOptions, auth ? getAccessToken() : null);

    if (response.status === 401 && auth && retry) {
      const refreshed = await refreshAccessToken();
      if (refreshed) {
        response = await send(path, requestOptions, refreshed);
      }
    }

    const { payload, isJson } = await readPayload(response);
    if (!response.ok) {
      const problem = isJson && isRecord(payload) ? (payload as ProblemDetails) : null;
      throw new ApiError(response.status, messageFromPayload(payload, response.status), problem, {
        retryAfterSeconds: parseRetryAfter(response),
      });
    }
    if (response.status === 204 || response.status === 205) return undefined as T;
    return payload as T;
  }

  async function requestBlob(path: string, requestOptions: RequestOptions = {}): Promise<Blob> {
    const auth = requestOptions.auth ?? true;
    const retry = requestOptions.retryOnUnauthorized ?? true;
    let response = await send(path, requestOptions, auth ? getAccessToken() : null);
    if (response.status === 401 && auth && retry) {
      const refreshed = await refreshAccessToken();
      if (refreshed) response = await send(path, requestOptions, refreshed);
    }
    if (!response.ok) {
      const { payload, isJson } = await readPayload(response);
      const problem = isJson && isRecord(payload) ? (payload as ProblemDetails) : null;
      throw new ApiError(response.status, messageFromPayload(payload, response.status), problem, {
        retryAfterSeconds: parseRetryAfter(response),
      });
    }
    return response.blob();
  }

  return { request, requestBlob, refreshAccessToken };
}
