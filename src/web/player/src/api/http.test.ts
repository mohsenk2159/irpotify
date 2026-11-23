import { describe, expect, it, vi } from 'vitest';
import { ApiError, buildUrl, createHttpClient, messageFromPayload } from './http';

interface Captured {
  url: string;
  init: RequestInit;
}

function jsonResponse(body: unknown, status = 200, contentType = 'application/json'): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': contentType } });
}

function makeClient(fetchImpl: (url: string, init: RequestInit) => Promise<Response>, tokens = { access: 'access-1', refresh: 'refresh-1' }) {
  const calls: Captured[] = [];
  const sessions: { accessToken: string; refreshToken: string }[] = [];
  const expired = vi.fn();
  let refreshCalls = 0;
  const client = createHttpClient({
    baseUrl: '/',
    getAccessToken: () => tokens.access,
    getRefreshToken: () => tokens.refresh,
    performRefresh: async (refreshToken) => {
      refreshCalls += 1;
      const response = await fetchImpl('/api/auth/refresh', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ refreshToken }),
      });
      if (!response.ok) throw new ApiError(response.status, 'Refresh failed.');
      const body = (await response.json()) as { accessToken: string; refreshToken: string };
      tokens.access = body.accessToken;
      tokens.refresh = body.refreshToken;
      return body;
    },
    onSessionRefreshed: (session) => sessions.push(session),
    onSessionExpired: expired,
    fetchImpl: async (url, init) => {
      calls.push({ url, init });
      return fetchImpl(url, init);
    },
  });
  return { client, calls, sessions, expired, tokens, refreshCalls: () => refreshCalls };
}

describe('ApiError and payload parsing', () => {
  it('parses problem+json into a typed error', async () => {
    const { client } = makeClient(async () =>
      jsonResponse({ type: 'https://httpstatuses.com/403', title: 'Access denied.', status: 403, traceId: 'trace-1' }, 403, 'application/problem+json'),
    );

    const failure = client.request('/api/admin/audit');
    await expect(failure).rejects.toBeInstanceOf(ApiError);
    await failure.catch((error: ApiError) => {
      expect(error.status).toBe(403);
      expect(error.message).toBe('Access denied.');
      expect(error.problem?.traceId).toBe('trace-1');
      expect(error.isForbidden).toBe(true);
    });
  });

  it('uses the error field for minimal API failures', () => {
    expect(messageFromPayload({ error: 'Password must be at least 12 characters.' }, 400)).toBe(
      'Password must be at least 12 characters.',
    );
  });

  it('lists rejected settings keys in the message', () => {
    expect(messageFromPayload({ title: 'Unknown settings keys.', rejected: ['bad.key'] }, 400)).toBe(
      'Unknown settings keys. (bad.key)',
    );
  });

  it('falls back to plain text and then to the status message', () => {
    expect(messageFromPayload('Bad gateway', 502)).toBe('Bad gateway');
    expect(messageFromPayload(null, 500)).toBe('The server encountered an unexpected error.');
    expect(messageFromPayload('', 418)).toBe('Request failed with status 418.');
  });

  it('returns undefined for 204 responses', async () => {
    const { client } = makeClient(async () => new Response(null, { status: 204 }));
    await expect(client.request('/api/favorites/00000000-0000-0000-0000-000000000000', { method: 'POST' })).resolves.toBeUndefined();
  });

  it('builds query strings without empty values', () => {
    expect(buildUrl('', '/api/tracks', { q: 'moon', page: 2, favorites: false, albumId: undefined, genreId: null })).toBe(
      '/api/tracks?q=moon&page=2&favorites=false',
    );
  });
});

describe('request shape', () => {
  it('sends bearer auth, JSON body and query parameters', async () => {
    const { client, calls } = makeClient(async () => jsonResponse({ ok: true }));
    await client.request('/api/albums', {
      method: 'POST',
      query: { notify: true },
      json: { title: 'Night Drive' },
    });

    const call = calls[0];
    expect(call).toBeDefined();
    expect(call?.url).toBe('/api/albums?notify=true');
    const headers = call?.init.headers as Record<string, string>;
    expect(headers['Authorization']).toBe('Bearer access-1');
    expect(headers['Content-Type']).toBe('application/json');
    expect(call?.init.body).toBe(JSON.stringify({ title: 'Night Drive' }));
  });

  it('throws a network ApiError when fetch rejects', async () => {
    const { client } = makeClient(async () => {
      throw new TypeError('fetch failed');
    });
    const failure = client.request('/api/health');
    await expect(failure).rejects.toMatchObject({ status: 0, isNetworkError: true });
  });

  it('propagates aborts untouched', async () => {
    const { client } = makeClient(async () => {
      throw new DOMException('The operation was aborted.', 'AbortError');
    });
    const controller = new AbortController();
    controller.abort();
    await expect(client.request('/api/tracks', { signal: controller.signal })).rejects.toMatchObject({ name: 'AbortError' });
  });
});

describe('single-flight refresh', () => {
  it('refreshes once for two concurrent 401 responses and retries both', async () => {
    let protectedCalls = 0;
    const { client, expired, sessions, refreshCalls } = makeClient(async (url, init) => {
      const headers = init.headers as Record<string, string>;
      if (url.startsWith('/api/auth/refresh')) {
        return jsonResponse({ accessToken: 'access-2', refreshToken: 'refresh-2' });
      }
      protectedCalls += 1;
      return headers['Authorization'] === 'Bearer access-2'
        ? jsonResponse({ ok: true })
        : jsonResponse({ error: 'Authentication is required.' }, 401);
    });

    const [first, second] = await Promise.all([
      client.request<{ ok: boolean }>('/api/tracks'),
      client.request<{ ok: boolean }>('/api/albums'),
    ]);

    expect(first.ok).toBe(true);
    expect(second.ok).toBe(true);
    expect(protectedCalls).toBe(4);
    expect(refreshCalls()).toBe(1);
    expect(sessions).toHaveLength(1);
    expect(sessions[0]?.accessToken).toBe('access-2');
    expect(expired).not.toHaveBeenCalled();
  });

  it('expires the session when refresh fails and does not loop', async () => {
    let protectedCalls = 0;
    const { client, expired, sessions, refreshCalls } = makeClient(async (url) => {
      if (url.startsWith('/api/auth/refresh')) return jsonResponse({ error: 'Invalid refresh token.' }, 401);
      protectedCalls += 1;
      return jsonResponse({ error: 'Authentication is required.' }, 401);
    });

    await expect(client.request('/api/tracks')).rejects.toMatchObject({ status: 401 });
    expect(protectedCalls).toBe(1);
    expect(refreshCalls()).toBe(1);
    expect(sessions).toHaveLength(0);
    expect(expired).toHaveBeenCalledTimes(1);
  });

  it('does not attempt refresh for anonymous requests', async () => {
    let refreshCalls = 0;
    const { client, expired } = makeClient(async (url) => {
      if (url.startsWith('/api/auth/refresh')) refreshCalls += 1;
      return jsonResponse({ error: 'Invalid email or password.' }, 401);
    });

    await expect(client.request('/api/auth/login', { method: 'POST', json: {}, auth: false })).rejects.toMatchObject({
      status: 401,
      message: 'Invalid email or password.',
    });
    expect(refreshCalls).toBe(0);
    expect(expired).not.toHaveBeenCalled();
  });
});
