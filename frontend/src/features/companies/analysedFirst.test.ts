import { describe, expect, it } from 'vitest';

import type { Company } from '../../api/companies';
import { arrangeAnalysedFirst, compareCompanies } from './analysedFirst';
import type { CompanyComparer } from './analysedFirst';
import type { CompanySort } from './companyView';

function company(
    orgnr: string,
    name: string,
    overrides: Partial<Company> = {},
): Company {
    return {
        orgnr,
        name,
        sni: '96210',
        employeeClass: '2',
        revenueClass: '1',
        revenueClassYear: 2025,
        registeredAt: null,
        calledAt: null,
        willing: null,
        note: null,
        workplaces: [],
        financials: [],
        ...overrides,
    };
}

function withRevenue(
    orgnr: string,
    name: string,
    revenue: number | null,
): Company {
    return company(orgnr, name, {
        financials: [{ periodEnd: '2025-12-31', revenue, netResult: null }],
    });
}

const byName: CompanyComparer = (left, right) =>
    compareCompanies(left, right, 'name', 'asc');

function orgnrs(rows: Company[]): string[] {
    return rows.map((row) => row.orgnr);
}

describe('arrangeAnalysedFirst', () => {
    const alfa = company('5599000001', 'Alfa AB');
    const beta = company('5599000002', 'Beta AB');
    const gamma = company('5599000003', 'Gamma AB');
    const delta = company('5599000004', 'Delta AB');

    it('puts analysed rows first and keeps the rest in server order', () => {
        // The server order is deliberately not by name.
        const pageItems = [gamma, alfa, delta, beta];
        const analysed = new Set([delta.orgnr, beta.orgnr]);

        const result = arrangeAnalysedFirst({
            pageItems,
            seenRows: new Map(),
            isAnalysed: (orgnr) => analysed.has(orgnr),
            compare: byName,
        });

        expect(orgnrs(result.rows)).toEqual([
            beta.orgnr,
            delta.orgnr,
            gamma.orgnr,
            alfa.orgnr,
        ]);
        expect([...result.analysed].sort()).toEqual(
            [beta.orgnr, delta.orgnr].sort(),
        );
    });

    it('pins an analysed row from another page once, above the page', () => {
        const seenRows = new Map(
            [alfa, beta, gamma].map((row) => [row.orgnr, row]),
        );

        const result = arrangeAnalysedFirst({
            pageItems: [delta],
            seenRows,
            isAnalysed: (orgnr) => orgnr === gamma.orgnr,
            compare: byName,
        });

        expect(orgnrs(result.rows)).toEqual([gamma.orgnr, delta.orgnr]);
    });

    it('does not repeat a pinned row that is also on the current page', () => {
        const result = arrangeAnalysedFirst({
            pageItems: [alfa, beta],
            seenRows: new Map([[beta.orgnr, beta]]),
            isAnalysed: (orgnr) => orgnr === beta.orgnr,
            compare: byName,
        });

        expect(orgnrs(result.rows)).toEqual([beta.orgnr, alfa.orgnr]);
    });

    it('uses the current page copy of a row over the remembered one', () => {
        const remembered = { ...beta, calledAt: null };
        const fresh = { ...beta, calledAt: '2026-11-10T09:00:00Z' };

        const result = arrangeAnalysedFirst({
            pageItems: [fresh],
            seenRows: new Map([[beta.orgnr, remembered]]),
            isAnalysed: () => true,
            compare: byName,
        });

        expect(result.rows).toEqual([fresh]);
    });

    it('pins nothing when no company is analysed', () => {
        const pageItems = [gamma, alfa];

        const result = arrangeAnalysedFirst({
            pageItems,
            seenRows: new Map([[delta.orgnr, delta]]),
            isAnalysed: () => false,
            compare: byName,
        });

        expect(result.rows).toEqual(pageItems);
        expect(result.analysed.size).toBe(0);
    });
});

describe('compareCompanies', () => {
    function sorted(
        rows: Company[],
        sort: CompanySort,
        order: 'asc' | 'desc',
    ): string[] {
        return orgnrs(
            [...rows].sort((left, right) =>
                compareCompanies(left, right, sort, order),
            ),
        );
    }

    it('sorts names with Swedish letters after z', () => {
        const rows = [
            company('1', 'Örebro Klipp AB'),
            company('2', 'Zeta AB'),
            company('3', 'Ängens Salong AB'),
            company('4', 'Alfa AB'),
        ];

        expect(sorted(rows, 'name', 'asc')).toEqual(['4', '2', '3', '1']);
        expect(sorted(rows, 'name', 'desc')).toEqual(['1', '3', '2', '4']);
    });

    it('sorts revenue as numbers and keeps a missing value last both ways', () => {
        const rows = [
            withRevenue('1', 'A', 900),
            withRevenue('2', 'B', null),
            withRevenue('3', 'C', 10000),
            company('4', 'D'),
        ];

        expect(sorted(rows, 'revenue', 'asc')).toEqual(['1', '3', '2', '4']);
        expect(sorted(rows, 'revenue', 'desc')).toEqual(['3', '1', '2', '4']);
    });

    it('reads the net result from the latest year only', () => {
        const rows = [
            company('1', 'A', {
                financials: [
                    { periodEnd: '2025-12-31', revenue: 1, netResult: null },
                    { periodEnd: '2024-12-31', revenue: 1, netResult: 500 },
                ],
            }),
            company('2', 'B', {
                financials: [
                    { periodEnd: '2025-12-31', revenue: 1, netResult: -20 },
                ],
            }),
        ];

        expect(sorted(rows, 'netResult', 'desc')).toEqual(['2', '1']);
    });

    it('sorts registration dates and leaves an unknown date last', () => {
        const rows = [
            company('1', 'A', { registeredAt: '2020-05-01' }),
            company('2', 'B', { registeredAt: null }),
            company('3', 'C', { registeredAt: '2014-03-12' }),
        ];

        expect(sorted(rows, 'registeredAt', 'asc')).toEqual(['3', '1', '2']);
        expect(sorted(rows, 'registeredAt', 'desc')).toEqual(['1', '3', '2']);
    });

    it('sorts the employee class code', () => {
        const rows = [
            company('1', 'A', { employeeClass: '3' }),
            company('2', 'B', { employeeClass: '1' }),
        ];

        expect(sorted(rows, 'employeeClass', 'asc')).toEqual(['2', '1']);
    });

    it('breaks a tie on orgnr, ascending in both directions', () => {
        const rows = [
            company('5599000002', 'Samma AB'),
            company('5599000001', 'Samma AB'),
        ];

        expect(sorted(rows, 'name', 'asc')).toEqual([
            '5599000001',
            '5599000002',
        ]);
        expect(sorted(rows, 'name', 'desc')).toEqual([
            '5599000001',
            '5599000002',
        ]);
    });
});
