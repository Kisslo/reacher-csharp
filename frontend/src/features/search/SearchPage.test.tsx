import { cleanup, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { focusManager } from '@tanstack/react-query';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import type { Job } from '../../api/searches';
import { getErrorMessage } from '../../texts/errors';
import { searchText } from '../../texts/search';
import { renderWithQuery } from '../../test/render';
import { SearchPage } from './SearchPage';

const jobId = '3f1a8c2e-7b4d-4e1a-9c6f-2d8b5a7e0c11';

type Call = { url: string; method: string };

function job(status: string, overrides: Partial<Job> = {}): Job {
    const finished =
        status === 'succeeded' || status === 'failed' || status === 'cancelled';

    return {
        jobId,
        status,
        phase: status === 'queued' ? null : 'companies',
        done: status === 'running' ? 1 : 0,
        total: status === 'running' ? 4 : null,
        error: null,
        startedAt: status === 'queued' ? null : '2026-11-11T09:00:00Z',
        finishedAt: finished ? '2026-11-11T09:05:00Z' : null,
        ...overrides,
    };
}

function stubApi(options: { job: () => Job; cancel?: () => Response }): Call[] {
    const calls: Call[] = [];

    vi.stubGlobal(
        'fetch',
        vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
            const url = String(input);
            const method = init?.method ?? 'GET';
            calls.push({ url, method });

            if (url === '/api/codes/municipalities') {
                return Response.json([{ code: '0180', name: 'Stockholm' }]);
            }
            if (url === '/api/codes/sni/96210') {
                return Response.json({ code: '96210', name: 'Hårvård' });
            }
            if (url === '/api/searches' && method === 'POST') {
                return Response.json({ jobId, status: 'queued' });
            }
            if (url === `/api/jobs/${jobId}` && method === 'GET') {
                return Response.json(options.job());
            }
            if (url === `/api/jobs/${jobId}/cancel` && method === 'POST') {
                return (
                    options.cancel ?? (() => Response.json(job('cancelled')))
                )();
            }
            if (url.startsWith('/api/companies?')) {
                return Response.json({
                    items: [],
                    page: 1,
                    pageSize: 1,
                    totalCount: 0,
                    coverage: [],
                });
            }

            throw new Error(`Unexpected fetch: ${method} ${url}`);
        }),
    );

    return calls;
}

function count(calls: Call[], method: string, url: string): number {
    return calls.filter((call) => call.method === method && call.url === url)
        .length;
}

function renderPage() {
    renderWithQuery(<SearchPage renderResults={() => <p>Resultat</p>} />);
}

describe('SearchPage cancel', () => {
    beforeEach(() => {
        window.history.replaceState(null, '', '/?sni=96210&municipality=0180');
    });

    afterEach(() => {
        cleanup();
        vi.useRealTimers();
        focusManager.setFocused(undefined);
        vi.unstubAllGlobals();
        window.history.replaceState(null, '', '/');
    });

    it('shows Avbryt sökning while queued or running, and hides it once the job has finished', async () => {
        vi.useFakeTimers({ shouldAdvanceTime: true });
        focusManager.setFocused(true);
        const statuses = ['queued', 'running', 'succeeded'];
        let index = 0;
        stubApi({
            job: () => job(statuses[Math.min(index++, statuses.length - 1)]),
        });
        renderPage();

        expect(
            await screen.findByRole('button', { name: searchText.cancel }),
        ).toBeTruthy();
        expect(screen.getByText(searchText.progressQueued)).toBeTruthy();

        await vi.advanceTimersByTimeAsync(2000);
        expect(
            await screen.findByText(searchText.progressCompanies(1, 4)),
        ).toBeTruthy();
        expect(
            screen.getByRole('button', { name: searchText.cancel }),
        ).toBeTruthy();

        await vi.advanceTimersByTimeAsync(2000);
        expect(await screen.findByText('Resultat')).toBeTruthy();
        expect(
            screen.queryByRole('button', { name: searchText.cancel }),
        ).toBeNull();
    });

    it('hides Avbryt sökning when the job has failed', async () => {
        stubApi({ job: () => job('failed') });
        renderPage();

        expect(await screen.findByText(searchText.progressFailed)).toBeTruthy();
        expect(
            screen.queryByRole('button', { name: searchText.cancel }),
        ).toBeNull();
        expect(screen.queryByText('Resultat')).toBeNull();
    });

    it('cancels a running search, stops polling, and does not start a new search', async () => {
        vi.useFakeTimers({ shouldAdvanceTime: true });
        focusManager.setFocused(true);
        const calls = stubApi({ job: () => job('running') });
        const user = userEvent.setup({
            advanceTimers: vi.advanceTimersByTime,
            delay: null,
        });
        renderPage();

        await user.click(
            await screen.findByRole('button', { name: searchText.cancel }),
        );

        expect(
            await screen.findByText(searchText.progressCancelled),
        ).toBeTruthy();
        expect(screen.queryByRole('progressbar')).toBeNull();
        expect(screen.queryByText(searchText.progressFailed)).toBeNull();
        expect(screen.queryByText('Resultat')).toBeNull();
        expect(
            screen.getByRole('button', { name: searchText.submit }),
        ).toBeTruthy();
        expect(count(calls, 'POST', `/api/jobs/${jobId}/cancel`)).toBe(1);
        expect(count(calls, 'POST', '/api/searches')).toBe(1);
        expect(
            calls.some((call) => call.url.startsWith('/api/companies')),
        ).toBe(false);

        const readsAfterCancel = count(calls, 'GET', `/api/jobs/${jobId}`);
        const searchesAfterCancel = count(calls, 'POST', '/api/searches');
        await vi.advanceTimersByTimeAsync(6000);

        expect(count(calls, 'GET', `/api/jobs/${jobId}`)).toBe(
            readsAfterCancel,
        );
        expect(count(calls, 'POST', '/api/searches')).toBe(searchesAfterCancel);
        expect(screen.getByText(searchText.progressCancelled)).toBeTruthy();
        expect(screen.queryByText('Resultat')).toBeNull();
    });

    it('shows the Swedish error when cancel returns 409 job_not_cancellable', async () => {
        stubApi({
            job: () => job('running'),
            cancel: () =>
                new Response(JSON.stringify({ code: 'job_not_cancellable' }), {
                    status: 409,
                    headers: { 'content-type': 'application/problem+json' },
                }),
        });
        const user = userEvent.setup();
        renderPage();

        await user.click(
            await screen.findByRole('button', { name: searchText.cancel }),
        );

        const message = getErrorMessage('job_not_cancellable');
        expect(message).not.toBe(getErrorMessage(undefined));
        expect((await screen.findByRole('alert')).textContent).toBe(message);
        expect(screen.queryByText(searchText.progressFailed)).toBeNull();
        expect(screen.queryByText(searchText.progressCancelled)).toBeNull();
    });
});
