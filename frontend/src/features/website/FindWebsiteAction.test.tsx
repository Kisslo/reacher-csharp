import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactElement } from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';

import type { Company, CompanyPage } from '../../api/companies';
import { getErrorMessage } from '../../texts/errors';
import { websiteText } from '../../texts/website';
import { renderWithQuery } from '../../test/render';
import { CompanyResults } from '../companies/CompanyResults';
import { FindWebsiteAction, WebsiteSearchProvider } from './FindWebsiteAction';

const orgnr = '5599000015';

const results = {
    candidates: [
        {
            url: 'https://example.test/company',
            title: 'Example salon',
            confidence: 'high',
            preselected: true,
        },
        {
            url: 'https://other.test/about',
            title: 'Other page',
            confidence: 'medium',
            preselected: false,
        },
        {
            url: 'https://third.test/',
            title: null,
            confidence: 'low',
            preselected: false,
        },
        {
            url: 'https://fourth.test/hidden',
            title: 'Fourth page',
            confidence: 'low',
            preselected: false,
        },
    ],
    remainingMonthlyQuota: 12,
};

type Call = { url: string; method: string };

function stubSearch(response: () => Promise<Response> | Response): Call[] {
    const calls: Call[] = [];

    vi.stubGlobal(
        'fetch',
        vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
            const url = String(input);
            const method = init?.method ?? 'GET';
            calls.push({ url, method });

            if (
                url === `/api/companies/${orgnr}/website-candidates` &&
                method === 'POST'
            ) {
                return response();
            }

            throw new Error(`Unexpected fetch: ${method} ${url}`);
        }),
    );

    return calls;
}

function renderAction(onAnalyze = vi.fn()) {
    const user = userEvent.setup();
    const queryClient = new QueryClient({
        defaultOptions: {
            queries: { retry: false },
            mutations: { retry: false },
        },
    });

    renderWithClient(
        queryClient,
        <WebsiteSearchProvider>
            <FindWebsiteAction orgnr={orgnr} onAnalyze={onAnalyze} />
        </WebsiteSearchProvider>,
    );

    return { user, queryClient, onAnalyze };
}

function renderWithClient(queryClient: QueryClient, ui: ReactElement) {
    return render(
        <QueryClientProvider client={queryClient}>{ui}</QueryClientProvider>,
    );
}

async function openPanel(user: ReturnType<typeof userEvent.setup>) {
    await user.click(screen.getByRole('button', { name: websiteText.find }));
    return screen.findByRole('dialog', { name: websiteText.dialogTitle });
}

describe('FindWebsiteAction', () => {
    afterEach(() => {
        vi.unstubAllGlobals();
        localStorage.clear();
        sessionStorage.clear();
        window.history.replaceState(null, '', '/');
    });

    it('shows three candidates, confidence, and the preselected label', async () => {
        stubSearch(() => Response.json(results));
        const { user } = renderAction();

        const dialog = await openPanel(user);

        expect(within(dialog).getByText(websiteText.quotaNote)).toBeTruthy();
        expect(within(dialog).getByText('example.test')).toBeTruthy();
        expect(within(dialog).getByText('Example salon')).toBeTruthy();
        expect(
            within(dialog).getByText(websiteText.confidence.high),
        ).toBeTruthy();
        expect(within(dialog).getByText(websiteText.bestMatch)).toBeTruthy();
        expect(within(dialog).getByText('other.test')).toBeTruthy();
        expect(
            within(dialog).getByText(websiteText.confidence.medium),
        ).toBeTruthy();
        expect(within(dialog).getByText('third.test')).toBeTruthy();
        expect(
            within(dialog).getByText(websiteText.confidence.low),
        ).toBeTruthy();
        expect(within(dialog).queryByText('Fourth page')).toBeNull();
        expect(
            within(dialog).queryByText('https://example.test/company'),
        ).toBeNull();
        expect(
            within(dialog).getByText(websiteText.quotaRemaining(12)),
        ).toBeTruthy();
    });

    it('opens the candidate in a new tab and passes it to analysis', async () => {
        const calls = stubSearch(() => Response.json(results));
        const onAnalyze = vi.fn();
        const { user } = renderAction(onAnalyze);
        const dialog = await openPanel(user);
        const title = within(dialog).getByText('Example salon');
        const candidate = title.closest('li');
        if (candidate === null) {
            throw new Error('Example salon was not in a candidate.');
        }

        const link = within(candidate).getByRole('link', {
            name: websiteText.open,
        });
        expect(link.getAttribute('href')).toBe('https://example.test/company');
        expect(link.getAttribute('target')).toBe('_blank');
        expect(link.getAttribute('rel')).toBe('noopener noreferrer');

        await user.click(
            within(candidate).getByRole('button', {
                name: websiteText.analyze,
            }),
        );

        expect(onAnalyze).toHaveBeenCalledTimes(1);
        expect(onAnalyze).toHaveBeenCalledWith('https://example.test/company');
        expect(
            calls.some((call) => call.url.includes('/api/website-analysis')),
        ).toBe(false);
    });

    it('shows the empty message and still offers a pasted address', async () => {
        stubSearch(() =>
            Response.json({ candidates: [], remainingMonthlyQuota: 4 }),
        );
        const onAnalyze = vi.fn();
        const { user } = renderAction(onAnalyze);
        const dialog = await openPanel(user);

        expect(within(dialog).getByText(websiteText.empty)).toBeTruthy();
        expect(
            within(dialog).getByLabelText(websiteText.pasteLabel),
        ).toBeTruthy();

        const field = within(dialog).getByLabelText(websiteText.pasteLabel);
        await user.type(field, 'not a url');
        expect(
            within(dialog).getByText(getErrorMessage('invalid_url')),
        ).toBeTruthy();
        expect(
            within(dialog).queryByRole('link', { name: websiteText.open }),
        ).toBeNull();

        await user.clear(field);
        await user.type(field, 'https://pasted.test/start');
        const actions = within(dialog).getByRole('group', {
            name: websiteText.pasteActions,
        });
        const link = within(actions).getByRole('link', {
            name: websiteText.open,
        });
        expect(link.getAttribute('href')).toBe('https://pasted.test/start');
        expect(link.getAttribute('rel')).toBe('noopener noreferrer');

        await user.click(
            within(actions).getByRole('button', { name: websiteText.analyze }),
        );
        expect(onAnalyze).toHaveBeenCalledWith('https://pasted.test/start');
    });

    it('shows the monthly quota error in Swedish', async () => {
        stubSearch(
            () =>
                new Response(JSON.stringify({ code: 'brave_quota_exceeded' }), {
                    status: 429,
                    headers: { 'content-type': 'application/problem+json' },
                }),
        );
        const { user } = renderAction();
        const dialog = await openPanel(user);

        expect((await within(dialog).findByRole('alert')).textContent).toBe(
            'Sökgränsen för månaden är nådd.',
        );
    });

    it('shows a Swedish loading state until the search returns', async () => {
        let release: (response: Response) => void = () => {
            throw new Error('The search was not waiting.');
        };
        const pending = new Promise<Response>((resolve) => {
            release = resolve;
        });
        stubSearch(() => pending);
        const { user } = renderAction();

        const dialog = await openPanel(user);
        expect(within(dialog).getByRole('status').textContent).toBe(
            websiteText.finding,
        );

        release(Response.json(results));
        expect(await within(dialog).findByText('example.test')).toBeTruthy();
    });

    it('searches again for the same company and drops the result on close', async () => {
        const calls = stubSearch(() => Response.json(results));
        const { user, queryClient } = renderAction();

        const dialog = await openPanel(user);
        expect(await within(dialog).findByText('example.test')).toBeTruthy();
        expect(calls).toHaveLength(1);
        expect(calls[0]?.url.includes('example.test')).toBe(false);

        await user.click(
            screen.getByRole('button', { name: websiteText.find }),
        );
        expect(await screen.findByText('example.test')).toBeTruthy();
        expect(calls).toHaveLength(2);

        await user.click(
            screen.getByRole('button', { name: websiteText.close }),
        );

        expect(screen.queryByRole('dialog')).toBeNull();
        expect(screen.queryByText('example.test')).toBeNull();
        expect(localStorage.length).toBe(0);
        expect(sessionStorage.length).toBe(0);
        expect(window.location.search).toBe('');
        expect(window.location.href).not.toContain('example.test');
        await waitFor(() => {
            expect(queryClient.getMutationCache().getAll()).toHaveLength(0);
        });
    });

    it('keeps only the company that was opened last', async () => {
        const calls: string[] = [];
        vi.stubGlobal(
            'fetch',
            vi.fn(async (input: RequestInfo | URL) => {
                const url = String(input);
                calls.push(url);
                const requested = url.includes('5599000080')
                    ? '5599000080'
                    : orgnr;
                return Response.json({
                    candidates: [
                        {
                            url: `https://${requested}.test/`,
                            title: requested,
                            confidence: 'high',
                            preselected: true,
                        },
                    ],
                    remainingMonthlyQuota: 3,
                });
            }),
        );
        const user = userEvent.setup();
        renderWithQuery(
            <WebsiteSearchProvider>
                <FindWebsiteAction orgnr={orgnr} onAnalyze={() => undefined} />
                <FindWebsiteAction
                    orgnr="5599000080"
                    onAnalyze={() => undefined}
                />
            </WebsiteSearchProvider>,
        );

        await user.click(
            screen.getAllByRole('button', { name: websiteText.find })[0]!,
        );
        expect(await screen.findByText(`${orgnr}.test`)).toBeTruthy();

        await user.click(
            screen.getAllByRole('button', { name: websiteText.find })[1]!,
        );
        expect(await screen.findByText('5599000080.test')).toBeTruthy();
        expect(screen.queryByText(`${orgnr}.test`)).toBeNull();
        expect(screen.getAllByRole('dialog')).toHaveLength(1);
        expect(calls).toHaveLength(2);
    });
});

const search = { sni: '96210', municipalities: ['0180'] };

const company = {
    orgnr,
    name: 'Saxen & Kammen AB',
    sni: '96210',
    employeeClass: '2',
    revenueClass: '3',
    revenueClassYear: 2025,
    registeredAt: '2014-03-12',
    calledAt: null,
    willing: null,
    note: null,
    workplaces: [{ name: null, municipality: '0180', city: 'Stockholm' }],
    financials: [],
} satisfies Company;

function companyPage(): CompanyPage {
    return {
        items: [company],
        page: 1,
        pageSize: 50,
        totalCount: 1,
        coverage: [],
    };
}

describe('the action column', () => {
    afterEach(() => {
        vi.unstubAllGlobals();
        window.history.replaceState(null, '', '/');
    });

    it('puts Hitta webbplats on the company row', async () => {
        vi.stubGlobal(
            'fetch',
            vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
                const url = String(input);
                if (url === '/api/codes/employee-classes') {
                    return Response.json([
                        { code: '2', name: '1-4 anställda' },
                    ]);
                }
                if (url === '/api/codes/revenue-classes') {
                    return Response.json([
                        { code: '3', name: '1 000-4 999 tkr' },
                    ]);
                }
                if (url.startsWith('/api/companies?')) {
                    return Response.json(companyPage());
                }
                if (
                    url === `/api/companies/${orgnr}/website-candidates` &&
                    init?.method === 'POST'
                ) {
                    return Response.json({
                        candidates: [
                            {
                                url: 'https://example.test/company',
                                title: 'Example salon',
                                confidence: 'high',
                                preselected: true,
                            },
                        ],
                        remainingMonthlyQuota: 9,
                    });
                }

                throw new Error(`Unexpected fetch: ${url}`);
            }),
        );

        const user = userEvent.setup();
        renderWithQuery(
            <WebsiteSearchProvider>
                <CompanyResults
                    search={search}
                    extraColumns={[
                        {
                            key: 'actions',
                            header: websiteText.column,
                            cell: (row) => (
                                <FindWebsiteAction
                                    orgnr={row.orgnr}
                                    onAnalyze={() => undefined}
                                />
                            ),
                        },
                    ]}
                />
            </WebsiteSearchProvider>,
        );

        const table = await screen.findByRole('table');
        expect(
            within(table).getByRole('columnheader', {
                name: websiteText.column,
            }),
        ).toBeTruthy();

        const companyCell = within(table).getByRole('cell', {
            name: 'Saxen & Kammen AB',
        });
        const row = companyCell.closest('tr');
        if (row === null) {
            throw new Error('The company row was not rendered.');
        }

        await user.click(
            within(row).getByRole('button', { name: websiteText.find }),
        );

        expect(await screen.findByRole('dialog')).toBeTruthy();
        expect(await screen.findByText('example.test')).toBeTruthy();
    });
});
