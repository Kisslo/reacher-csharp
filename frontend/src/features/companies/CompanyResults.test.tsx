import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { focusManager } from '@tanstack/react-query';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import type { CodeItem } from '../../api/codes';
import type { Company, CompanyPage } from '../../api/companies';
import { commonText } from '../../texts/common';
import { companyText } from '../../texts/companies';
import { getErrorMessage } from '../../texts/errors';
import { renderWithQuery } from '../../test/render';
import { AnalysisProvider, useAnalysis } from '../analysis/AnalysisContext';
import { analysisText } from '../analysis/analysisText';
import type {
    LighthouseSessionScores,
    SessionScore,
} from '../analysis/sessionScore';
import { CompanyResults } from './CompanyResults';
import { formatAmount as format } from './format';

const search = { sni: '96210', municipalities: ['0180', '0184'] };

const employeeClasses = [
    { code: '2', name: '1-4 anställda' },
    { code: '3', name: '5-9 anställda' },
] satisfies CodeItem[];

const revenueClasses = [
    { code: '1', name: '1-499 tkr' },
    { code: '3', name: '1 000-4 999 tkr' },
] satisfies CodeItem[];

const saxen = {
    orgnr: '5599000015',
    name: 'Saxen & Kammen AB',
    sni: '96210',
    employeeClass: '2',
    revenueClass: '3',
    revenueClassYear: 2025,
    registeredAt: '2014-03-12',
    calledAt: null,
    willing: null,
    note: null,
    workplaces: [
        { name: 'Salongen nr 7', municipality: '0180', city: 'Stockholm' },
        { name: null, municipality: '0184', city: 'Solna' },
    ],
    financials: [
        { periodEnd: '2025-12-31', revenue: 1234000, netResult: -45000 },
        { periodEnd: '2024-12-31', revenue: 1100000, netResult: 90000 },
        { periodEnd: '2023-12-31', revenue: 980000, netResult: null },
    ],
} satisfies Company;

const kamhuset = {
    orgnr: '5599000080',
    name: 'Kamhuset AB',
    sni: '96210',
    employeeClass: '99',
    revenueClass: '1',
    revenueClassYear: 2025,
    registeredAt: null,
    calledAt: null,
    willing: null,
    note: null,
    workplaces: [{ name: null, municipality: '0180', city: 'Stockholm' }],
    financials: [],
} satisfies Company;

function page(overrides: Partial<CompanyPage> = {}): CompanyPage {
    return {
        items: [saxen, kamhuset],
        page: 1,
        pageSize: 50,
        totalCount: 2,
        coverage: [],
        ...overrides,
    };
}

/** Fakes the API and records every /api/companies request. */
function stubApi(companies: () => Response = () => Response.json(page())) {
    const requests: URLSearchParams[] = [];

    vi.stubGlobal(
        'fetch',
        vi.fn(async (input: RequestInfo | URL) => {
            const url = String(input);
            if (url === '/api/codes/employee-classes') {
                return Response.json(employeeClasses);
            }
            if (url === '/api/codes/revenue-classes') {
                return Response.json(revenueClasses);
            }
            if (url.startsWith('/api/companies?')) {
                requests.push(new URLSearchParams(url.split('?')[1]));
                return companies();
            }

            throw new Error(`Unexpected fetch: ${url}`);
        }),
    );

    return requests;
}

/** Testing Library turns a no-break space into a plain space before it compares. */
function formatAmount(amount: number): string {
    return format(amount).replace(/\s/g, ' ');
}

function lastOf<T>(items: T[]): T {
    const last = items.at(-1);
    if (last === undefined) {
        throw new Error('Expected at least one item.');
    }
    return last;
}

async function renderResults() {
    const user = userEvent.setup();
    renderWithQuery(<CompanyResults search={search} />);
    await screen.findByRole('table');
    return user;
}

function companyNames(): string[] {
    return screen
        .getAllByRole('row')
        .slice(1)
        .map((row) => row.children[1]?.textContent ?? '');
}

function rowOf(name: string): HTMLElement {
    const cell = screen.getByRole('cell', { name });
    const row = cell.closest('tr');
    if (row === null) {
        throw new Error(`No row for ${name}.`);
    }
    return row;
}

function analysed(
    reworkScore: number,
    lighthouse: LighthouseSessionScores | null,
): SessionScore {
    return {
        kind: 'scored',
        reworkScore,
        analysedAt: '2026-11-02T14:00:00Z',
        lighthouse,
    };
}

/** The five score cells sit at the end of the row: Behov, then Lighthouse. */
function scoreCells(name: string): string[] {
    return [...rowOf(name).children]
        .slice(-5)
        .map((cell) => cell.textContent ?? '');
}

function headersWithSort(): HTMLElement[] {
    return screen
        .getAllByRole('columnheader')
        .filter((header) => header.hasAttribute('aria-sort'));
}

describe('CompanyResults', () => {
    afterEach(() => {
        vi.unstubAllGlobals();
        window.history.replaceState(null, '', '/');
    });

    describe('the table', () => {
        beforeEach(() => {
            stubApi();
        });

        it('shows the Swedish columns', async () => {
            await renderResults();

            for (const name of [
                companyText.columnCompany,
                companyText.columnWorkplaces,
                companyText.columnEmployees,
                companyText.columnRevenue,
                companyText.columnNetResult,
                companyText.columnRegistered,
                companyText.columnCalled,
                companyText.columnWilling,
            ]) {
                expect(screen.getByRole('columnheader', { name })).toBeTruthy();
            }
        });

        it('shows a company with its latest year', async () => {
            await renderResults();

            const row = within(rowOf('Saxen & Kammen AB'));
            expect(row.getByText('Salongen nr 7, Stockholm')).toBeTruthy();
            expect(row.getByText('Solna')).toBeTruthy();
            expect(row.getByText('1-4 anställda')).toBeTruthy();
            expect(row.getByText(formatAmount(1234000))).toBeTruthy();
            expect(row.getByText('2014-03-12')).toBeTruthy();
            expect(row.queryByText(formatAmount(1100000))).toBeNull();
        });

        it('marks a negative result', async () => {
            await renderResults();

            const result = screen.getByText(formatAmount(-45000));
            expect(result.className).toBe('amount-negative');
        });

        it('shows a dash for missing figures, never 0', async () => {
            await renderResults();

            const row = within(rowOf('Kamhuset AB'));
            expect(row.getAllByText(companyText.missingValue)).toHaveLength(3);
            expect(row.queryByText(formatAmount(0))).toBeNull();
        });

        it('falls back to the code when a class has no name', async () => {
            await renderResults();

            expect(within(rowOf('Kamhuset AB')).getByText('99')).toBeTruthy();
        });

        it('shows the total number of hits', async () => {
            await renderResults();

            expect(screen.getByText(companyText.hits('2'))).toBeTruthy();
        });

        it('expands a row to every year, and collapses it again', async () => {
            const user = await renderResults();
            const toggle = screen.getByRole('button', {
                name: companyText.showDetails('Saxen & Kammen AB'),
            });

            await user.click(toggle);

            const years = screen.getByRole('table', {
                name: companyText.financialsLabel('Saxen & Kammen AB'),
            });
            expect(within(years).getAllByRole('row')).toHaveLength(4);
            expect(within(years).getByText('2023-12-31')).toBeTruthy();
            expect(within(years).getByText(formatAmount(1100000))).toBeTruthy();
            expect(toggle.getAttribute('aria-expanded')).toBe('true');

            await user.click(toggle);

            expect(
                screen.queryByRole('table', {
                    name: companyText.financialsLabel('Saxen & Kammen AB'),
                }),
            ).toBeNull();
        });

        it('says so when an expanded company has no report', async () => {
            const user = await renderResults();

            await user.click(
                screen.getByRole('button', {
                    name: companyText.showDetails('Kamhuset AB'),
                }),
            );

            expect(screen.getByText(companyText.financialsEmpty)).toBeTruthy();
        });
    });

    describe('the request', () => {
        it('asks for the search, 50 per page', async () => {
            const requests = stubApi();

            await renderResults();

            const request = lastOf(requests);
            expect(request.get('sni')).toBe('96210');
            expect(request.getAll('municipality')).toEqual(['0180', '0184']);
            expect(request.get('pageSize')).toBe('50');
        });

        it('starts from the filters, sort and page in the URL', async () => {
            window.history.replaceState(
                null,
                '',
                '/?employeeClass=3&revenueMin=5000&hasFinancials=true&sort=revenue&order=desc&page=2',
            );
            const requests = stubApi();

            await renderResults();

            const request = lastOf(requests);
            expect(request.getAll('employeeClass')).toEqual(['3']);
            expect(request.get('revenueMin')).toBe('5000');
            expect(request.get('hasFinancials')).toBe('true');
            expect(request.get('sort')).toBe('revenue');
            expect(request.get('order')).toBe('desc');
            expect(request.get('page')).toBe('2');

            expect(
                (
                    screen.getByRole('checkbox', {
                        name: '5-9 anställda',
                    }) as HTMLInputElement
                ).checked,
            ).toBe(true);
            expect(
                (
                    screen.getByLabelText(
                        companyText.filterRevenueMin,
                    ) as HTMLInputElement
                ).value,
            ).toBe('5000');
        });
    });

    describe('filters', () => {
        it('applies every kind of filter and writes it to the URL', async () => {
            const requests = stubApi();
            const user = await renderResults();
            await screen.findByRole('checkbox', { name: '1-4 anställda' });

            await user.click(
                screen.getByRole('checkbox', { name: '1-4 anställda' }),
            );
            await user.click(
                screen.getByRole('checkbox', { name: '5-9 anställda' }),
            );
            await user.click(
                screen.getByRole('checkbox', { name: '1-499 tkr' }),
            );
            await user.type(
                screen.getByLabelText(companyText.filterRegisteredFrom),
                '2015-01-01',
            );
            await user.type(
                screen.getByLabelText(companyText.filterRegisteredTo),
                '2024-12-31',
            );
            await user.type(
                screen.getByLabelText(companyText.filterRevenueMin),
                '100000',
            );
            await user.type(
                screen.getByLabelText(companyText.filterRevenueMax),
                '900000',
            );
            await user.type(
                screen.getByLabelText(companyText.filterNetResultMin),
                '-5000',
            );
            await user.type(
                screen.getByLabelText(companyText.filterNetResultMax),
                '5000',
            );
            await user.click(
                screen.getByRole('checkbox', {
                    name: companyText.filterHasFinancials,
                }),
            );
            await user.click(
                screen.getByRole('button', { name: companyText.filterApply }),
            );

            await waitFor(() => {
                expect(requests).toHaveLength(2);
            });
            const request = lastOf(requests);
            const page = new URLSearchParams(window.location.search);
            for (const params of [request, page]) {
                expect(params.getAll('employeeClass')).toEqual(['2', '3']);
                expect(params.getAll('revenueClass')).toEqual(['1']);
                expect(params.get('registeredFrom')).toBe('2015-01-01');
                expect(params.get('registeredTo')).toBe('2024-12-31');
                expect(params.get('revenueMin')).toBe('100000');
                expect(params.get('revenueMax')).toBe('900000');
                expect(params.get('netResultMin')).toBe('-5000');
                expect(params.get('netResultMax')).toBe('5000');
                expect(params.get('hasFinancials')).toBe('true');
            }
        });

        it('sends nothing until the filters are applied', async () => {
            const requests = stubApi();
            const user = await renderResults();

            await user.type(
                screen.getByLabelText(companyText.filterRevenueMin),
                '100000',
            );

            expect(requests).toHaveLength(1);
        });

        it('goes back to page 1 when a filter changes', async () => {
            window.history.replaceState(null, '', '/?page=3');
            const requests = stubApi(() =>
                Response.json(page({ page: 3, totalCount: 150 })),
            );
            const user = await renderResults();
            expect(lastOf(requests).get('page')).toBe('3');

            await user.type(
                screen.getByLabelText(companyText.filterRevenueMin),
                '100000',
            );
            await user.click(
                screen.getByRole('button', { name: companyText.filterApply }),
            );

            await waitFor(() => {
                expect(lastOf(requests).get('revenueMin')).toBe('100000');
            });
            expect(lastOf(requests).has('page')).toBe(false);
            expect(window.location.search).not.toContain('page=');
        });

        it('shows an error and sends nothing for a reversed range', async () => {
            const requests = stubApi();
            const user = await renderResults();

            await user.type(
                screen.getByLabelText(companyText.filterRevenueMin),
                '200',
            );
            await user.type(
                screen.getByLabelText(companyText.filterRevenueMax),
                '100',
            );
            await user.click(
                screen.getByRole('button', { name: companyText.filterApply }),
            );

            expect(screen.getByText(companyText.amountRangeError)).toBeTruthy();
            expect(requests).toHaveLength(1);
        });

        it('clears every filter', async () => {
            window.history.replaceState(
                null,
                '',
                '/?sni=96210&employeeClass=2&revenueMin=5000&called=true&willing=false&page=2',
            );
            const requests = stubApi();
            const user = await renderResults();

            await user.click(
                screen.getByRole('button', { name: companyText.filterClear }),
            );

            await waitFor(() => {
                expect(lastOf(requests).has('employeeClass')).toBe(false);
            });
            expect(lastOf(requests).has('revenueMin')).toBe(false);
            expect(lastOf(requests).has('called')).toBe(false);
            expect(lastOf(requests).has('willing')).toBe(false);
            expect(window.location.search).toBe('?sni=96210');
            expect(
                (
                    screen.getByLabelText(
                        companyText.filterRevenueMin,
                    ) as HTMLInputElement
                ).value,
            ).toBe('');
        });
    });

    describe('sorting', () => {
        it('sorts by a column, then flips the order', async () => {
            const requests = stubApi();
            const user = await renderResults();
            const name = new RegExp(companyText.columnRevenue);

            await user.click(screen.getByRole('button', { name }));

            await waitFor(() => {
                expect(lastOf(requests).get('sort')).toBe('revenue');
            });
            expect(lastOf(requests).has('order')).toBe(false);
            expect(
                screen
                    .getByRole('columnheader', { name })
                    .getAttribute('aria-sort'),
            ).toBe('ascending');

            await user.click(screen.getByRole('button', { name }));

            await waitFor(() => {
                expect(lastOf(requests).get('order')).toBe('desc');
            });
            expect(
                screen
                    .getByRole('columnheader', { name })
                    .getAttribute('aria-sort'),
            ).toBe('descending');
            expect(window.location.search).toContain('sort=revenue');
        });

        it('offers every sort the API has', async () => {
            const requests = stubApi();
            const user = await renderResults();
            const columns = [
                [companyText.columnEmployees, 'employeeClass'],
                [companyText.columnNetResult, 'netResult'],
                [companyText.columnRegistered, 'registeredAt'],
                [companyText.columnRevenue, 'revenue'],
            ];

            for (const [label, sort] of columns) {
                await user.click(
                    screen.getByRole('button', { name: new RegExp(label) }),
                );
                await waitFor(() => {
                    expect(lastOf(requests).get('sort')).toBe(sort);
                });
            }

            await user.click(
                screen.getByRole('button', {
                    name: new RegExp(`^${companyText.columnCompany}`),
                }),
            );
            await waitFor(() => {
                expect(lastOf(requests).has('sort')).toBe(false);
            });
        });

        it('goes back to page 1', async () => {
            window.history.replaceState(null, '', '/?page=3');
            const requests = stubApi(() =>
                Response.json(page({ page: 3, totalCount: 150 })),
            );
            const user = await renderResults();

            await user.click(
                screen.getByRole('button', {
                    name: new RegExp(companyText.columnRevenue),
                }),
            );

            await waitFor(() => {
                expect(lastOf(requests).get('sort')).toBe('revenue');
            });
            expect(lastOf(requests).has('page')).toBe(false);
        });
    });

    describe('pagination', () => {
        it('moves between pages and keeps the page in the URL', async () => {
            const requests = stubApi(() =>
                Response.json(page({ totalCount: 120 })),
            );
            const user = await renderResults();
            const previous = screen.getByRole('button', {
                name: companyText.previousPage,
            });

            expect(screen.getByText('Sida 1 av 3')).toBeTruthy();
            expect((previous as HTMLButtonElement).disabled).toBe(true);

            await user.click(
                screen.getByRole('button', { name: companyText.nextPage }),
            );

            await waitFor(() => {
                expect(lastOf(requests).get('page')).toBe('2');
            });
            expect(screen.getByText('Sida 2 av 3')).toBeTruthy();
            expect(window.location.search).toContain('page=2');

            await user.click(previous);

            await waitFor(() => {
                expect(lastOf(requests).has('page')).toBe(false);
            });
        });

        it('cannot go past the last page', async () => {
            window.history.replaceState(null, '', '/?page=3');
            stubApi(() => Response.json(page({ page: 3, totalCount: 120 })));

            await renderResults();

            const next = screen.getByRole('button', {
                name: companyText.nextPage,
            });
            expect((next as HTMLButtonElement).disabled).toBe(true);
        });

        it('leads back from a bookmarked page past the end', async () => {
            window.history.replaceState(null, '', '/?page=99');
            const requests = stubApi(() =>
                Response.json(page({ items: [], page: 99, totalCount: 120 })),
            );
            const user = await renderResults();

            await user.click(
                screen.getByRole('button', { name: companyText.previousPage }),
            );

            await waitFor(() => {
                expect(lastOf(requests).get('page')).toBe('3');
            });
        });
    });

    describe('other states', () => {
        it('shows the empty state and keeps the filters', async () => {
            stubApi(() => Response.json(page({ items: [], totalCount: 0 })));

            renderWithQuery(<CompanyResults search={search} />);

            expect(await screen.findByText(companyText.empty)).toBeTruthy();
            expect(screen.queryByRole('table')).toBeNull();
            expect(
                screen.getByRole('button', { name: companyText.filterClear }),
            ).toBeTruthy();
        });

        it('shows the server error in Swedish and can retry', async () => {
            let fail = true;
            const requests = stubApi(() =>
                fail
                    ? Response.json(
                          { code: 'invalid_search_query' },
                          {
                              status: 400,
                              headers: {
                                  'Content-Type': 'application/problem+json',
                              },
                          },
                      )
                    : Response.json(page()),
            );
            const user = userEvent.setup();

            renderWithQuery(<CompanyResults search={search} />);

            const alert = await screen.findByRole('alert');
            expect(alert.textContent).toContain('Filtren är inte giltiga');

            fail = false;
            await user.click(
                within(alert).getByRole('button', { name: commonText.retry }),
            );

            expect(await screen.findByRole('table')).toBeTruthy();
            expect(requests).toHaveLength(2);
        });

        it('renders the columns Team 2 passes in', async () => {
            stubApi();
            renderWithQuery(
                <CompanyResults
                    search={search}
                    extraColumns={[
                        {
                            key: 'actions',
                            header: 'Åtgärder',
                            cell: (company) => `Knapp för ${company.orgnr}`,
                        },
                    ]}
                />,
            );

            expect(
                await screen.findByRole('columnheader', { name: 'Åtgärder' }),
            ).toBeTruthy();
            expect(screen.getByText('Knapp för 5599000015')).toBeTruthy();
        });

        it('sorts the current page by analysed score and leaves missing scores last', async () => {
            stubApi();
            const user = userEvent.setup();
            const scores = new Map([
                [
                    '5599000015',
                    analysed(80, {
                        performance: 61,
                        accessibility: 82,
                        bestPractices: 74,
                        seo: 90,
                    }),
                ],
            ]);

            renderWithQuery(
                <CompanyResults
                    search={search}
                    scoreLookup={(orgnr) => scores.get(orgnr)}
                />,
            );

            await screen.findByRole('table');
            expect(
                screen.getByRole('columnheader', { name: /Behov/ }),
            ).toBeTruthy();
            expect(screen.getByText(analysisText.sortExplanation)).toBeTruthy();
            for (const name of [
                analysisText.performance,
                analysisText.accessibility,
                analysisText.bestPractices,
                analysisText.seo,
            ]) {
                expect(screen.getByRole('columnheader', { name })).toBeTruthy();
            }

            const scoreHeader = screen.getByRole('button', { name: /Behov/ });
            await user.click(scoreHeader);

            expect(companyNames()).toEqual([
                'Saxen & Kammen AB',
                'Kamhuset AB',
            ]);
            expect(
                within(rowOf('Kamhuset AB')).getAllByText(
                    companyText.missingValue,
                ),
            ).toHaveLength(9);
        });

        it('replaces the company sort with Behov, and a column turns Behov off', async () => {
            const requests = stubApi();
            const user = userEvent.setup();
            // Opposite of the API order, so a leftover company sort cannot pass.
            const scores = new Map([
                ['5599000015', analysed(80, null)],
                ['5599000080', analysed(20, null)],
            ]);

            renderWithQuery(
                <CompanyResults
                    search={search}
                    scoreLookup={(orgnr) => scores.get(orgnr)}
                />,
            );

            await screen.findByRole('table');
            const revenue = new RegExp(companyText.columnRevenue);
            const revenueHeader = () =>
                screen.getByRole('columnheader', { name: revenue });
            const scoreHeader = () =>
                screen.getByRole('columnheader', {
                    name: new RegExp(analysisText.sortColumn),
                });

            await user.click(screen.getByRole('button', { name: revenue }));
            await waitFor(() => {
                expect(lastOf(requests).get('sort')).toBe('revenue');
            });
            await user.click(screen.getByRole('button', { name: revenue }));

            await waitFor(() => {
                expect(lastOf(requests).get('order')).toBe('desc');
            });
            expect(revenueHeader().getAttribute('aria-sort')).toBe(
                'descending',
            );
            expect(window.location.search).toContain('sort=revenue');

            await user.click(
                screen.getByRole('button', {
                    name: new RegExp(analysisText.sortColumn),
                }),
            );

            await waitFor(() => {
                expect(lastOf(requests).has('sort')).toBe(false);
            });
            expect(lastOf(requests).has('order')).toBe(false);
            expect(window.location.search).not.toContain('sort=');
            expect(window.location.search).not.toContain('order=');
            expect(scoreHeader().getAttribute('aria-sort')).toBe('ascending');
            expect(revenueHeader().getAttribute('aria-sort')).toBeNull();
            expect(
                screen
                    .getByRole('columnheader', {
                        name: new RegExp(`^${companyText.columnCompany}`),
                    })
                    .getAttribute('aria-sort'),
            ).toBeNull();
            expect(companyNames()).toEqual([
                'Kamhuset AB',
                'Saxen & Kammen AB',
            ]);

            await user.click(
                screen.getByRole('button', {
                    name: new RegExp(`^${companyText.columnCompany}`),
                }),
            );

            expect(scoreHeader().getAttribute('aria-sort')).toBeNull();
            expect(
                screen
                    .getByRole('columnheader', {
                        name: new RegExp(`^${companyText.columnCompany}`),
                    })
                    .getAttribute('aria-sort'),
            ).toBe('ascending');
            expect(window.location.search).not.toContain('order=');
            // Both are analysed, so the browser sorts them by name inside the
            // pinned group. The fake API ignores the sort.
            expect(companyNames()).toEqual([
                'Kamhuset AB',
                'Saxen & Kammen AB',
            ]);

            await user.click(screen.getByRole('button', { name: revenue }));

            await waitFor(() => {
                expect(lastOf(requests).get('sort')).toBe('revenue');
            });
            expect(lastOf(requests).has('order')).toBe(false);
            expect(scoreHeader().getAttribute('aria-sort')).toBeNull();
            expect(revenueHeader().getAttribute('aria-sort')).toBe('ascending');
            expect(companyNames()).toEqual([
                'Saxen & Kammen AB',
                'Kamhuset AB',
            ]);
        });

        it('sorts each Lighthouse column and keeps a missing score last', async () => {
            const tredje = {
                ...kamhuset,
                orgnr: '5599000031',
                name: 'Tredje AB',
                registeredAt: '2020-01-01',
                employeeClass: '2',
                financials: [
                    {
                        periodEnd: '2025-12-31',
                        revenue: 1000,
                        netResult: 100,
                    },
                ],
            } satisfies Company;
            const requests = stubApi(() =>
                Response.json(
                    page({
                        items: [saxen, kamhuset, tredje],
                        totalCount: 3,
                    }),
                ),
            );
            const user = userEvent.setup();
            const scores = new Map<string, SessionScore>([
                [
                    saxen.orgnr,
                    analysed(30, {
                        performance: 10,
                        accessibility: 20,
                        bestPractices: 40,
                        seo: 80,
                    }),
                ],
                [
                    kamhuset.orgnr,
                    analysed(60, {
                        performance: 80,
                        accessibility: 90,
                        bestPractices: 70,
                        seo: 10,
                    }),
                ],
                [tredje.orgnr, analysed(90, null)],
            ]);

            renderWithQuery(
                <CompanyResults
                    search={search}
                    scoreLookup={(orgnr) => scores.get(orgnr)}
                />,
            );

            await screen.findByRole('table');
            expect(scoreCells('Saxen & Kammen AB')).toEqual([
                '30',
                '10',
                '20',
                '40',
                '80',
            ]);
            expect(scoreCells('Tredje AB')).toEqual([
                '90',
                companyText.missingValue,
                companyText.missingValue,
                companyText.missingValue,
                companyText.missingValue,
            ]);

            await user.click(
                screen.getByRole('button', {
                    name: companyText.showDetails('Saxen & Kammen AB'),
                }),
            );
            expect(
                screen
                    .getByRole('table', {
                        name: companyText.financialsLabel('Saxen & Kammen AB'),
                    })
                    .closest('td')
                    ?.getAttribute('colspan'),
            ).toBe('15');
            await user.click(
                screen.getByRole('button', {
                    name: companyText.hideDetails('Saxen & Kammen AB'),
                }),
            );

            const column = (name: string) =>
                screen.getByRole('columnheader', { name });

            await user.click(
                screen.getByRole('button', {
                    name: analysisText.performance,
                }),
            );
            expect(companyNames()).toEqual([
                'Saxen & Kammen AB',
                'Kamhuset AB',
                'Tredje AB',
            ]);
            expect(headersWithSort()).toEqual([
                column(analysisText.performance),
            ]);
            expect(
                column(analysisText.performance).getAttribute('aria-sort'),
            ).toBe('ascending');

            await user.click(
                screen.getByRole('button', {
                    name: analysisText.performance,
                }),
            );
            expect(companyNames()).toEqual([
                'Kamhuset AB',
                'Saxen & Kammen AB',
                'Tredje AB',
            ]);
            expect(
                column(analysisText.performance).getAttribute('aria-sort'),
            ).toBe('descending');

            await user.click(
                screen.getByRole('button', {
                    name: analysisText.accessibility,
                }),
            );
            expect(companyNames()).toEqual([
                'Saxen & Kammen AB',
                'Kamhuset AB',
                'Tredje AB',
            ]);
            expect(headersWithSort()).toEqual([
                column(analysisText.accessibility),
            ]);
            expect(
                column(analysisText.accessibility).getAttribute('aria-sort'),
            ).toBe('ascending');
            expect(
                column(analysisText.performance).getAttribute('aria-sort'),
            ).toBeNull();

            await user.click(
                screen.getByRole('button', {
                    name: analysisText.bestPractices,
                }),
            );
            expect(companyNames()).toEqual([
                'Saxen & Kammen AB',
                'Kamhuset AB',
                'Tredje AB',
            ]);
            expect(
                column(analysisText.bestPractices).getAttribute('aria-sort'),
            ).toBe('ascending');

            await user.click(
                screen.getByRole('button', { name: analysisText.seo }),
            );
            expect(companyNames()).toEqual([
                'Kamhuset AB',
                'Saxen & Kammen AB',
                'Tredje AB',
            ]);
            expect(column(analysisText.seo).getAttribute('aria-sort')).toBe(
                'ascending',
            );
            expect(window.location.search).not.toContain('seo');
            expect(window.location.search).not.toContain('https');

            await user.click(
                screen.getByRole('button', {
                    name: new RegExp(`^${companyText.columnCompany}`),
                }),
            );
            expect(
                column(analysisText.seo).getAttribute('aria-sort'),
            ).toBeNull();
            expect(headersWithSort()).toEqual([
                column(companyText.columnCompany),
            ]);
            // All three are analysed, so the browser sorts them by name.
            expect(companyNames()).toEqual([
                'Kamhuset AB',
                'Saxen & Kammen AB',
                'Tredje AB',
            ]);

            const revenue = new RegExp(companyText.columnRevenue);
            await user.click(screen.getByRole('button', { name: revenue }));
            await user.click(screen.getByRole('button', { name: revenue }));
            await waitFor(() => {
                expect(lastOf(requests).get('order')).toBe('desc');
            });

            await user.click(
                screen.getByRole('button', { name: analysisText.seo }),
            );
            await waitFor(() => {
                expect(lastOf(requests).has('sort')).toBe(false);
            });
            expect(lastOf(requests).has('order')).toBe(false);
            expect(window.location.search).not.toContain('sort=');
            expect(
                screen
                    .getByRole('columnheader', { name: revenue })
                    .getAttribute('aria-sort'),
            ).toBeNull();
            expect(headersWithSort()).toEqual([column(analysisText.seo)]);
            expect(companyNames()).toEqual([
                'Kamhuset AB',
                'Saxen & Kammen AB',
                'Tredje AB',
            ]);
        });
    });

    describe('outreach', () => {
        const savedNote = 'Ring nästa vecka';

        function outreachCompany(overrides: Partial<Company> = {}): Company {
            return {
                ...saxen,
                calledAt: '2026-11-01T10:00:00Z',
                willing: true,
                note: savedNote,
                ...overrides,
            };
        }

        function stubOutreach(options?: { rejectNote?: boolean }) {
            let items = [outreachCompany(), kamhuset];
            const puts: {
                called: boolean;
                willing: boolean | null;
                note: string | null;
            }[] = [];
            const gets: URLSearchParams[] = [];

            vi.stubGlobal(
                'fetch',
                vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
                    const url = String(input);
                    if (url === '/api/codes/employee-classes') {
                        return Response.json(employeeClasses);
                    }
                    if (url === '/api/codes/revenue-classes') {
                        return Response.json(revenueClasses);
                    }
                    if (url.endsWith('/outreach')) {
                        const body = JSON.parse(String(init?.body)) as {
                            called: boolean;
                            willing: boolean | null;
                            note: string | null;
                        };
                        puts.push(body);
                        if (
                            options?.rejectNote === true &&
                            body.note !== savedNote
                        ) {
                            return Response.json(
                                { code: 'note_rejected' },
                                {
                                    status: 400,
                                    headers: {
                                        'Content-Type':
                                            'application/problem+json',
                                    },
                                },
                            );
                        }

                        const orgnr = url.split('/')[3] ?? '';
                        items = items.map((item) =>
                            item.orgnr === orgnr
                                ? {
                                      ...item,
                                      calledAt: body.called
                                          ? (item.calledAt ??
                                            '2026-11-09T12:00:00Z')
                                          : null,
                                      willing: body.willing,
                                      note: body.note,
                                  }
                                : item,
                        );
                        return new Response(null, { status: 204 });
                    }
                    if (url.startsWith('/api/companies?')) {
                        gets.push(new URLSearchParams(url.split('?')[1]));
                        return Response.json(
                            page({ items, totalCount: items.length }),
                        );
                    }

                    throw new Error(`Unexpected fetch: ${url}`);
                }),
            );

            return { puts, gets };
        }

        function calledBox(): HTMLInputElement {
            return screen.getByRole('checkbox', {
                name: companyText.calledLabel('Saxen & Kammen AB'),
            }) as HTMLInputElement;
        }

        function willingBox(): HTMLSelectElement {
            return screen.getByRole('combobox', {
                name: companyText.willingLabel('Saxen & Kammen AB'),
            }) as HTMLSelectElement;
        }

        it('checks and unchecks Kontaktad without clearing willingness or the note', async () => {
            const { puts } = stubOutreach();
            const user = await renderResults();

            expect(calledBox().checked).toBe(true);
            await user.click(calledBox());

            await waitFor(() => {
                expect(calledBox().checked).toBe(false);
                expect(calledBox().disabled).toBe(false);
            });
            expect(lastOf(puts)).toEqual({
                called: false,
                willing: true,
                note: savedNote,
            });
            expect(willingBox().value).toBe('true');

            await user.click(calledBox());

            await waitFor(() => {
                expect(calledBox().checked).toBe(true);
                expect(calledBox().disabled).toBe(false);
            });
            expect(lastOf(puts)).toEqual({
                called: true,
                willing: true,
                note: savedNote,
            });
            expect(window.location.search).not.toContain(savedNote);
        });

        it('sets Ja, Nej and empty on Samarbete without changing Kontaktad', async () => {
            const { puts } = stubOutreach();
            const user = await renderResults();
            const choices = [
                [companyText.willingNo, 'false'],
                [companyText.willingUnset, ''],
                [companyText.willingYes, 'true'],
            ] as const;

            for (const [label, value] of choices) {
                await user.selectOptions(willingBox(), label);

                await waitFor(() => {
                    expect(willingBox().value).toBe(value);
                    expect(willingBox().disabled).toBe(false);
                });
                expect(lastOf(puts)).toEqual({
                    called: true,
                    willing: value === '' ? null : value === 'true',
                    note: savedNote,
                });
                expect(calledBox().checked).toBe(true);
            }
        });

        it('saves a note and keeps it out of the URL', async () => {
            const { puts } = stubOutreach();
            const user = await renderResults();
            const nextNote = 'Skicka mail med info';

            await user.click(
                screen.getByRole('button', {
                    name: companyText.showDetails('Saxen & Kammen AB'),
                }),
            );
            const field = screen.getByLabelText(
                companyText.noteLabel('Saxen & Kammen AB'),
            );
            expect((field as HTMLTextAreaElement).placeholder).toBe(
                companyText.notePlaceholder,
            );
            expect(companyText.notePlaceholder).toContain('Ring nästa vecka');
            expect(companyText.notePlaceholder).toContain(
                'Skicka mail med info',
            );

            await user.clear(field);
            await user.type(field, nextNote);
            await user.click(
                screen.getByRole('button', { name: companyText.noteSave }),
            );

            await waitFor(() => {
                expect(lastOf(puts).note).toBe(nextNote);
            });
            expect(lastOf(puts)).toEqual({
                called: true,
                willing: true,
                note: nextNote,
            });
            expect((field as HTMLTextAreaElement).value).toBe(nextNote);
            expect(calledBox().checked).toBe(true);
            expect(willingBox().value).toBe('true');
            expect(window.location.search).not.toContain('note');
            expect(window.location.search).not.toContain('Skicka');
        });

        it('filters called and willingness in the request and the page URL', async () => {
            const { gets } = stubOutreach();
            const user = await renderResults();

            await user.selectOptions(
                screen.getByLabelText(companyText.filterCalled),
                companyText.filterCalledOnly,
            );
            await user.selectOptions(
                screen.getByLabelText(companyText.filterWilling),
                companyText.willingYes,
            );
            await user.click(
                screen.getByRole('button', { name: companyText.filterApply }),
            );

            await waitFor(() => {
                expect(gets.length).toBeGreaterThan(1);
            });
            for (const params of [
                lastOf(gets),
                new URLSearchParams(window.location.search),
            ]) {
                expect(params.get('called')).toBe('true');
                expect(params.get('willing')).toBe('true');
                expect(params.has('note')).toBe(false);
            }

            await user.selectOptions(
                screen.getByLabelText(companyText.filterCalled),
                companyText.filterNotCalled,
            );
            await user.selectOptions(
                screen.getByLabelText(companyText.filterWilling),
                companyText.willingNo,
            );
            await user.click(
                screen.getByRole('button', { name: companyText.filterApply }),
            );

            await waitFor(() => {
                expect(lastOf(gets).get('called')).toBe('false');
            });
            expect(lastOf(gets).get('willing')).toBe('false');
            expect(
                new URLSearchParams(window.location.search).get('willing'),
            ).toBe('false');
        });

        it('shows the Swedish error and keeps the previous note and both markers', async () => {
            const { puts } = stubOutreach({ rejectNote: true });
            const user = await renderResults();

            await user.click(
                screen.getByRole('button', {
                    name: companyText.showDetails('Saxen & Kammen AB'),
                }),
            );
            const field = screen.getByLabelText(
                companyText.noteLabel('Saxen & Kammen AB'),
            );
            await user.clear(field);
            await user.type(field, 'https://example.com');
            await user.click(
                screen.getByRole('button', { name: companyText.noteSave }),
            );

            const alert = await screen.findByRole('alert');
            expect(alert.textContent).toBe(getErrorMessage('note_rejected'));
            expect(calledBox().checked).toBe(true);
            expect(willingBox().value).toBe('true');
            expect((field as HTMLTextAreaElement).value).toBe(savedNote);
            expect(lastOf(puts).note).toBe('https://example.com');
            expect(window.location.search).not.toContain('example.com');
        });

        describe('while the search is running', () => {
            afterEach(() => {
                vi.useRealTimers();
                focusManager.setFocused(undefined);
            });

            async function renderLive() {
                vi.useFakeTimers({ shouldAdvanceTime: true });
                focusManager.setFocused(true);
                const user = userEvent.setup({
                    advanceTimers: vi.advanceTimersByTime,
                    delay: null,
                });
                renderWithQuery(<CompanyResults search={search} live />);
                await screen.findByRole('table');
                return user;
            }

            async function openNote(user: ReturnType<typeof userEvent.setup>) {
                await user.click(
                    screen.getByRole('button', {
                        name: companyText.showDetails('Saxen & Kammen AB'),
                    }),
                );
                return screen.getByLabelText(
                    companyText.noteLabel('Saxen & Kammen AB'),
                ) as HTMLTextAreaElement;
            }

            it('saves Kontaktad, Samarbete and a note on a listed row, and a reload keeps the draft', async () => {
                const { puts, gets } = stubOutreach();
                const user = await renderLive();

                await user.click(calledBox());
                await waitFor(() => {
                    expect(calledBox().checked).toBe(false);
                    expect(calledBox().disabled).toBe(false);
                });
                expect(lastOf(puts)).toEqual({
                    called: false,
                    willing: true,
                    note: savedNote,
                });

                await user.selectOptions(willingBox(), companyText.willingNo);
                await waitFor(() => {
                    expect(willingBox().value).toBe('false');
                    expect(willingBox().disabled).toBe(false);
                });
                expect(lastOf(puts)).toEqual({
                    called: false,
                    willing: false,
                    note: savedNote,
                });

                const field = await openNote(user);
                await user.clear(field);
                await user.type(field, 'Skicka mail');

                const getsBefore = gets.length;
                await vi.advanceTimersByTimeAsync(5000);
                expect(gets.length).toBeGreaterThan(getsBefore);
                expect(field.value).toBe('Skicka mail');

                await user.click(
                    screen.getByRole('button', { name: companyText.noteSave }),
                );
                await waitFor(() => {
                    expect(lastOf(puts).note).toBe('Skicka mail');
                });
                expect(lastOf(puts)).toEqual({
                    called: false,
                    willing: false,
                    note: 'Skicka mail',
                });
            });

            it('keeps the previous values after a failed save and the next reload', async () => {
                stubOutreach({ rejectNote: true });
                const user = await renderLive();

                const field = await openNote(user);
                await user.clear(field);
                await user.type(field, 'https://example.com');
                await user.click(
                    screen.getByRole('button', { name: companyText.noteSave }),
                );

                const alert = await screen.findByRole('alert');
                expect(alert.textContent).toBe(
                    getErrorMessage('note_rejected'),
                );
                await vi.advanceTimersByTimeAsync(5000);

                expect(field.value).toBe(savedNote);
                expect(calledBox().checked).toBe(true);
                expect(willingBox().value).toBe('true');
            });
        });
    });

    describe('while the search is running', () => {
        afterEach(() => {
            vi.useRealTimers();
            focusManager.setFocused(undefined);
        });

        it('shows only the companies stored so far and reloads every 5 seconds', async () => {
            vi.useFakeTimers({ shouldAdvanceTime: true });
            focusManager.setFocused(true);
            let stored: Company[] = [saxen];
            const requests = stubApi(() =>
                Response.json(
                    page({ items: stored, totalCount: stored.length }),
                ),
            );
            renderWithQuery(<CompanyResults search={search} live />);

            expect(
                await screen.findByRole('cell', { name: saxen.name }),
            ).toBeTruthy();
            expect(
                screen.queryByRole('cell', { name: kamhuset.name }),
            ).toBeNull();
            expect(requests).toHaveLength(1);

            stored = [saxen, kamhuset];
            await vi.advanceTimersByTimeAsync(4000);
            expect(requests).toHaveLength(1);

            await vi.advanceTimersByTimeAsync(1000);
            expect(
                await screen.findByRole('cell', { name: kamhuset.name }),
            ).toBeTruthy();
            expect(requests).toHaveLength(2);
            expect(lastOf(requests).toString()).toBe(requests[0]?.toString());
        });

        it('does not reload once the search is no longer running', async () => {
            vi.useFakeTimers({ shouldAdvanceTime: true });
            focusManager.setFocused(true);
            const requests = stubApi();
            renderWithQuery(<CompanyResults search={search} />);
            await screen.findByRole('table');

            await vi.advanceTimersByTimeAsync(15000);

            expect(requests).toHaveLength(1);
        });
    });

    describe('analysed companies', () => {
        // A made-up site. It must never reach the table, the URL or storage.
        const website = 'https://example.test';

        const norra = {
            ...kamhuset,
            orgnr: '5599000049',
            name: 'Norra Klipp AB',
        } satisfies Company;

        const ostra = {
            ...kamhuset,
            orgnr: '5599000056',
            name: 'Östra Frisör AB',
        } satisfies Company;

        /**
         * Two pages of two. The fake ignores the sort, like the other tests, so
         * the rest of a page always comes in this server order.
         */
        function stubTwoPages(reworkScores: number[] = []) {
            let rows: Company[] = [saxen, kamhuset, norra, ostra];
            const puts: { orgnr: string; called: boolean }[] = [];
            let analyses = 0;

            vi.stubGlobal(
                'fetch',
                vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
                    const url = String(input);
                    if (url === '/api/codes/employee-classes') {
                        return Response.json(employeeClasses);
                    }
                    if (url === '/api/codes/revenue-classes') {
                        return Response.json(revenueClasses);
                    }
                    if (url === '/api/website-analysis') {
                        const reworkScore = reworkScores[analyses] ?? 72;
                        analyses += 1;
                        return Response.json({
                            url: website,
                            fetchedAt: '2026-11-11T09:00:00Z',
                            reworkScore,
                            lighthouse: null,
                            checks: {
                                https: true,
                                viewport: true,
                                generator: null,
                                copyrightYear: null,
                                responseTimeMs: 300,
                            },
                        });
                    }
                    if (url.endsWith('/outreach')) {
                        const orgnr = url.split('/')[3] ?? '';
                        const body = JSON.parse(String(init?.body)) as {
                            called: boolean;
                        };
                        puts.push({ orgnr, called: body.called });
                        rows = rows.map((row) =>
                            row.orgnr === orgnr
                                ? {
                                      ...row,
                                      calledAt: body.called
                                          ? '2026-11-11T10:00:00Z'
                                          : null,
                                  }
                                : row,
                        );
                        return new Response(null, { status: 204 });
                    }
                    if (url.startsWith('/api/companies?')) {
                        const params = new URLSearchParams(url.split('?')[1]);
                        const number = Number(params.get('page') ?? '1');
                        return Response.json(
                            page({
                                items: rows.slice((number - 1) * 2, number * 2),
                                page: number,
                                pageSize: 2,
                                totalCount: rows.length,
                            }),
                        );
                    }

                    throw new Error(`Unexpected fetch: ${url}`);
                }),
            );

            return { puts };
        }

        /**
         * Wired like App: the row's buttons analyse the company's website, or
         * mark that it has none.
         */
        function AnalysedResults() {
            const { getScore, markNoWebsite, startAnalysis } = useAnalysis();
            return (
                <CompanyResults
                    search={search}
                    scoreLookup={getScore}
                    extraColumns={[
                        {
                            key: 'analyse',
                            header: 'Analys',
                            cell: (company) => (
                                <>
                                    <button
                                        type="button"
                                        onClick={() =>
                                            startAnalysis(
                                                company.orgnr,
                                                website,
                                            )
                                        }
                                    >
                                        {`Analysera ${company.name}`}
                                    </button>
                                    <button
                                        type="button"
                                        onClick={() =>
                                            markNoWebsite(company.orgnr)
                                        }
                                    >
                                        {`Ingen webbplats ${company.name}`}
                                    </button>
                                </>
                            ),
                        },
                    ]}
                />
            );
        }

        async function renderAnalysed() {
            const user = userEvent.setup();
            const view = renderWithQuery(
                <AnalysisProvider>
                    <AnalysedResults />
                </AnalysisProvider>,
            );
            await screen.findByRole('cell', { name: saxen.name });
            return { user, view };
        }

        type User = ReturnType<typeof userEvent.setup>;

        async function analyse(user: User, name: string) {
            await user.click(
                screen.getByRole('button', { name: `Analysera ${name}` }),
            );
            await screen.findByText(analysisText.score);
            await user.click(
                screen.getByRole('button', { name: analysisText.close }),
            );
        }

        async function goToPage(user: User, label: string, name: string) {
            await user.click(screen.getByRole('button', { name: label }));
            await screen.findByRole('cell', { name });
        }

        /** The Analyserad cell of a row, found by the header's position. */
        function analysedMark(name: string): string {
            const index = screen
                .getAllByRole('columnheader')
                .findIndex(
                    (header) =>
                        header.textContent === companyText.columnAnalysed,
                );
            expect(index).toBeGreaterThan(0);
            return rowOf(name).children[index]?.textContent ?? '';
        }

        function expectNothingStored() {
            expect(screen.getByRole('table').textContent).not.toContain(
                'example.test',
            );
            expect(window.location.href).not.toContain('example.test');
            expect(localStorage.length).toBe(0);
            expect(sessionStorage.length).toBe(0);
        }

        afterEach(() => {
            localStorage.clear();
            sessionStorage.clear();
        });

        it('marks an analysed company and pins it once above every page', async () => {
            stubTwoPages();
            const { user, view } = await renderAnalysed();

            expect(
                screen.getByRole('columnheader', {
                    name: companyText.columnAnalysed,
                }),
            ).toBeTruthy();
            expect(analysedMark(saxen.name)).toBe(companyText.missingValue);

            await goToPage(user, companyText.nextPage, norra.name);
            await analyse(user, ostra.name);

            expect(analysedMark(ostra.name)).toBe(companyText.analysedYes);
            expect(analysedMark(norra.name)).toBe(companyText.missingValue);
            expect(rowOf(ostra.name).className).toBe('analysed-row');
            expect(companyNames()).toEqual([ostra.name, norra.name]);

            await goToPage(user, companyText.previousPage, saxen.name);

            expect(companyNames()).toEqual([
                ostra.name,
                saxen.name,
                kamhuset.name,
            ]);
            expect(analysedMark(ostra.name)).toBe(companyText.analysedYes);
            expect(analysedMark(saxen.name)).toBe(companyText.missingValue);

            await goToPage(user, companyText.nextPage, norra.name);

            expect(companyNames()).toEqual([ostra.name, norra.name]);
            expectNothingStored();

            // A reload starts from an empty session: no mark and no pin.
            view.unmount();
            window.history.replaceState(null, '', '/');
            await renderAnalysed();

            expect(companyNames()).toEqual([saxen.name, kamhuset.name]);
            expect(analysedMark(saxen.name)).toBe(companyText.missingValue);
            expect(analysedMark(kamhuset.name)).toBe(companyText.missingValue);
            expectNothingStored();
        });

        it('sorts inside the analysed group and never lifts the rest above it', async () => {
            // Saxen gets the lower Behov, so the Behov and name orders differ.
            stubTwoPages([30, 80]);
            const { user } = await renderAnalysed();
            const behov = () =>
                screen.getByRole('button', {
                    name: new RegExp(analysisText.sortColumn),
                });
            const companyColumn = () =>
                screen.getByRole('button', {
                    name: new RegExp(`^${companyText.columnCompany}`),
                });

            await analyse(user, saxen.name);
            await goToPage(user, companyText.nextPage, norra.name);
            await analyse(user, norra.name);

            // Name ascending inside the pins. Östra follows in server order.
            expect(companyNames()).toEqual([
                norra.name,
                saxen.name,
                ostra.name,
            ]);

            await goToPage(user, companyText.previousPage, kamhuset.name);
            // Kamhuset comes first by name, but it is not analysed.
            expect(companyNames()).toEqual([
                norra.name,
                saxen.name,
                kamhuset.name,
            ]);

            await user.click(behov());
            expect(companyNames()).toEqual([
                saxen.name,
                norra.name,
                kamhuset.name,
            ]);

            await user.click(behov());
            expect(companyNames()).toEqual([
                norra.name,
                saxen.name,
                kamhuset.name,
            ]);

            // Leaving Behov selects the company column ascending.
            await user.click(companyColumn());
            expect(companyNames()).toEqual([
                norra.name,
                saxen.name,
                kamhuset.name,
            ]);

            await user.click(companyColumn());
            await waitFor(() => {
                expect(companyNames()).toEqual([
                    saxen.name,
                    norra.name,
                    kamhuset.name,
                ]);
            });
            expectNothingStored();
        });

        it('marks and pins a company without a website, with no scores', async () => {
            stubTwoPages();
            const { user } = await renderAnalysed();

            await analyse(user, saxen.name);
            await goToPage(user, companyText.nextPage, norra.name);
            await user.click(
                screen.getByRole('button', {
                    name: `Ingen webbplats ${ostra.name}`,
                }),
            );

            expect(analysedMark(ostra.name)).toBe(
                companyText.analysedNoWebsite,
            );
            expect(scoreCells(ostra.name)).toEqual(
                Array(5).fill(companyText.missingValue),
            );

            await goToPage(user, companyText.previousPage, kamhuset.name);
            expect(companyNames()).toEqual([
                saxen.name,
                ostra.name,
                kamhuset.name,
            ]);

            // No Behov value, so it stays last in the pinned group both ways.
            const behov = () =>
                screen.getByRole('button', {
                    name: new RegExp(analysisText.sortColumn),
                });
            await user.click(behov());
            expect(companyNames()).toEqual([
                saxen.name,
                ostra.name,
                kamhuset.name,
            ]);
            await user.click(behov());
            expect(companyNames()).toEqual([
                saxen.name,
                ostra.name,
                kamhuset.name,
            ]);
            expectNothingStored();
        });

        it('keeps Kontaktad on a pinned company from another page', async () => {
            const { puts } = stubTwoPages();
            const { user } = await renderAnalysed();

            await goToPage(user, companyText.nextPage, norra.name);
            await analyse(user, ostra.name);
            await goToPage(user, companyText.previousPage, saxen.name);

            const called = () =>
                screen.getByRole('checkbox', {
                    name: companyText.calledLabel(ostra.name),
                }) as HTMLInputElement;
            await user.click(called());

            await waitFor(() => {
                expect(called().checked).toBe(true);
                expect(called().disabled).toBe(false);
            });
            expect(puts).toEqual([{ orgnr: ostra.orgnr, called: true }]);
            expect(companyNames()[0]).toBe(ostra.name);
        });
    });
});
