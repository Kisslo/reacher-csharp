import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import type { CodeItem } from '../../api/codes';
import type { Company, CompanyPage } from '../../api/companies';
import { commonText } from '../../texts/common';
import { companyText } from '../../texts/companies';
import { renderWithQuery } from '../../test/render';
import { analysisText } from '../analysis/analysisText';
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
                '/?sni=96210&employeeClass=2&revenueMin=5000&page=2',
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
            const scores = new Map([['5599000015', { reworkScore: 80 }]]);

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
            expect(
                screen.getByText(
                    'Sortering på behov gäller bara företag du analyserat under den här sessionen',
                ),
            ).toBeTruthy();

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
            ).toHaveLength(4);
        });

        it('replaces the company sort with Behov, and a column turns Behov off', async () => {
            const requests = stubApi();
            const user = userEvent.setup();
            // Opposite of the API order, so a leftover company sort cannot pass.
            const scores = new Map([
                ['5599000015', { reworkScore: 80 }],
                ['5599000080', { reworkScore: 20 }],
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

            expect(scoreHeader().getAttribute('aria-sort')).toBe('none');
            expect(
                screen
                    .getByRole('columnheader', {
                        name: new RegExp(`^${companyText.columnCompany}`),
                    })
                    .getAttribute('aria-sort'),
            ).toBe('ascending');
            expect(window.location.search).not.toContain('order=');
            expect(companyNames()).toEqual([
                'Saxen & Kammen AB',
                'Kamhuset AB',
            ]);

            await user.click(screen.getByRole('button', { name: revenue }));

            await waitFor(() => {
                expect(lastOf(requests).get('sort')).toBe('revenue');
            });
            expect(lastOf(requests).has('order')).toBe(false);
            expect(scoreHeader().getAttribute('aria-sort')).toBe('none');
            expect(revenueHeader().getAttribute('aria-sort')).toBe('ascending');
            expect(companyNames()).toEqual([
                'Saxen & Kammen AB',
                'Kamhuset AB',
            ]);
        });
    });
});
