import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { renderWithQuery } from '../../test/render';
import { AnalysisProvider, useAnalysis } from './AnalysisContext';
import { analysisText } from './analysisText';

const response = {
    url: 'https://example.test',
    fetchedAt: '2026-11-02T14:00:00Z',
    reworkScore: 72,
    lighthouse: {
        performance: 61,
        accessibility: 82,
        bestPractices: 74,
        seo: 90,
    },
    checks: {
        https: true,
        viewport: false,
        generator: 'Example CMS',
        copyrightYear: 2024,
        responseTimeMs: 480,
    },
};

function Trigger() {
    const { startAnalysis } = useAnalysis();
    return (
        <button
            type="button"
            onClick={() => startAnalysis('5599000015', 'https://example.test')}
        >
            Analysera
        </button>
    );
}

function StoredScore() {
    const { getScore } = useAnalysis();
    const score = getScore('5599000015');
    return <p>{score === undefined ? 'empty' : JSON.stringify(score)}</p>;
}

describe('AnalysisProvider', () => {
    afterEach(() => {
        vi.unstubAllGlobals();
        localStorage.clear();
        sessionStorage.clear();
    });

    it('posts the URL and renders the complete analysis result', async () => {
        const fetchMock = vi.fn(async () => Response.json(response));
        vi.stubGlobal('fetch', fetchMock);
        const user = userEvent.setup();

        renderWithQuery(
            <AnalysisProvider>
                <Trigger />
            </AnalysisProvider>,
        );

        await user.click(screen.getByRole('button', { name: 'Analysera' }));

        expect(await screen.findByText(analysisText.score)).toBeTruthy();
        expect(fetchMock).toHaveBeenCalledWith(
            '/api/website-analysis',
            expect.objectContaining({
                method: 'POST',
                body: JSON.stringify({ url: 'https://example.test' }),
            }),
        );
        expect(screen.getByText('72/100')).toBeTruthy();
        expect(screen.getByText('Prestanda')).toBeTruthy();
        expect(screen.getByText('Example CMS')).toBeTruthy();
        expect(screen.getByText('480 ms')).toBeTruthy();
        expect(localStorage.length).toBe(0);
        expect(sessionStorage.length).toBe(0);
    });

    it('shows the loading message and translated API errors', async () => {
        let release: (response: Response) => void = () => undefined;
        const pending = new Promise<Response>((resolve) => {
            release = resolve;
        });
        vi.stubGlobal(
            'fetch',
            vi.fn(() => pending),
        );
        const user = userEvent.setup();

        renderWithQuery(
            <AnalysisProvider>
                <Trigger />
            </AnalysisProvider>,
        );

        await user.click(screen.getByRole('button', { name: 'Analysera' }));
        expect(screen.getByText(analysisText.loading)).toBeTruthy();

        release(
            new Response(JSON.stringify({ code: 'analysis_timeout' }), {
                status: 504,
                headers: { 'content-type': 'application/problem+json' },
            }),
        );

        expect(
            await screen.findByText('Analysen tog för lång tid. Försök igen.'),
        ).toBeTruthy();
    });

    it('translates invalid URL errors', async () => {
        vi.stubGlobal(
            'fetch',
            vi.fn(
                async () =>
                    new Response(JSON.stringify({ code: 'invalid_url' }), {
                        status: 400,
                        headers: { 'content-type': 'application/problem+json' },
                    }),
            ),
        );
        const user = userEvent.setup();

        renderWithQuery(
            <AnalysisProvider>
                <Trigger />
            </AnalysisProvider>,
        );

        await user.click(screen.getByRole('button', { name: 'Analysera' }));

        expect(
            await screen.findByText('Webbadressen är inte giltig.'),
        ).toBeTruthy();
    });

    it('explains when Lighthouse is unavailable', async () => {
        vi.stubGlobal(
            'fetch',
            vi.fn(async () => Response.json({ ...response, lighthouse: null })),
        );
        const user = userEvent.setup();

        renderWithQuery(
            <AnalysisProvider>
                <Trigger />
            </AnalysisProvider>,
        );

        await user.click(screen.getByRole('button', { name: 'Analysera' }));

        expect((await screen.findByRole('note')).textContent).toBe(
            analysisText.checksOnly,
        );
    });

    it('keeps the Lighthouse scores in memory and drops them on a new mount', async () => {
        vi.stubGlobal(
            'fetch',
            vi.fn(async () =>
                Response.json({
                    ...response,
                    reworkScore: '72',
                    lighthouse: {
                        performance: '61',
                        accessibility: '82',
                        bestPractices: '74',
                        seo: '90',
                    },
                }),
            ),
        );
        const user = userEvent.setup();
        const view = renderWithQuery(
            <AnalysisProvider>
                <Trigger />
                <StoredScore />
            </AnalysisProvider>,
        );

        await user.click(screen.getByRole('button', { name: 'Analysera' }));

        const stored = await screen.findByText(/"performance":61/);
        const score = JSON.parse(stored.textContent ?? '');
        expect(score).toEqual({
            kind: 'scored',
            reworkScore: 72,
            analysedAt: '2026-11-02T14:00:00Z',
            lighthouse: {
                performance: 61,
                accessibility: 82,
                bestPractices: 74,
                seo: 90,
            },
        });
        expect(JSON.stringify(score)).not.toContain('example.test');
        expect(localStorage.length).toBe(0);
        expect(sessionStorage.length).toBe(0);
        expect(window.location.search).not.toContain('example.test');

        view.unmount();
        renderWithQuery(
            <AnalysisProvider>
                <StoredScore />
            </AnalysisProvider>,
        );

        expect(screen.getByText('empty')).toBeTruthy();
        expect(localStorage.length).toBe(0);
        expect(sessionStorage.length).toBe(0);
    });

    it('stores a checks-only analysis with lighthouse null', async () => {
        vi.stubGlobal(
            'fetch',
            vi.fn(async () => Response.json({ ...response, lighthouse: null })),
        );
        const user = userEvent.setup();

        renderWithQuery(
            <AnalysisProvider>
                <Trigger />
                <StoredScore />
            </AnalysisProvider>,
        );

        await user.click(screen.getByRole('button', { name: 'Analysera' }));

        const stored = await screen.findByText(/"lighthouse":null/);
        expect(JSON.parse(stored.textContent ?? '').reworkScore).toBe(72);
        expect(
            JSON.stringify(JSON.parse(stored.textContent ?? '')),
        ).not.toContain('example.test');
    });
});
