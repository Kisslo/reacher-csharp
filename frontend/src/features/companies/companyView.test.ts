import { afterEach, describe, expect, it } from 'vitest';

import { companyText } from '../../texts/companies';
import {
    appendCompanyView,
    clearCompanySort,
    emptyFilters,
    readCompanyViewFromUrl,
    selectCompanySort,
    sortBy,
    validateFilters,
    writeCompanyViewToUrl,
} from './companyView';
import type { CompanyView } from './companyView';

const defaultView: CompanyView = {
    filters: emptyFilters,
    sort: 'name',
    order: 'asc',
    page: 1,
};

afterEach(() => {
    window.history.replaceState(null, '', '/');
});

function setSearch(search: string): void {
    window.history.replaceState(null, '', `/${search}`);
}

describe('readCompanyViewFromUrl', () => {
    it('gives the default view for an empty URL', () => {
        expect(readCompanyViewFromUrl()).toEqual(defaultView);
    });

    it('reads every filter, the sort and the page', () => {
        setSearch(
            '?employeeClass=2&employeeClass=3&employeeClass=2&revenueClass=4' +
                '&registeredFrom=2015-01-01&registeredTo=2024-12-31' +
                '&revenueMin=100000&revenueMax=900000' +
                '&netResultMin=-5000&netResultMax=5000' +
                '&hasFinancials=true&sort=revenue&order=desc&page=3',
        );

        expect(readCompanyViewFromUrl()).toEqual({
            filters: {
                employeeClasses: ['2', '3'],
                revenueClasses: ['4'],
                registeredFrom: '2015-01-01',
                registeredTo: '2024-12-31',
                revenueMin: '100000',
                revenueMax: '900000',
                netResultMin: '-5000',
                netResultMax: '5000',
                hasFinancials: true,
            },
            sort: 'revenue',
            order: 'desc',
            page: 3,
        });
    });

    it('ignores values the API would reject', () => {
        setSearch(
            '?sort=hacked&order=sideways&page=0&revenueMin=abc' +
                '&registeredFrom=yesterday&hasFinancials=maybe',
        );

        expect(readCompanyViewFromUrl()).toEqual(defaultView);
    });
});

describe('appendCompanyView', () => {
    it('leaves defaults out, so a plain search keeps a short URL', () => {
        const params = new URLSearchParams();

        appendCompanyView(params, defaultView);

        expect(params.toString()).toBe('');
    });

    it('never sends hasFinancials=false', () => {
        const params = new URLSearchParams();

        appendCompanyView(params, {
            ...defaultView,
            filters: { ...emptyFilters, hasFinancials: false },
        });

        expect(params.has('hasFinancials')).toBe(false);
    });

    it('keeps 0, which is a real amount and not an empty field', () => {
        const params = new URLSearchParams();

        appendCompanyView(params, {
            ...defaultView,
            filters: { ...emptyFilters, netResultMin: '0' },
        });

        expect(params.get('netResultMin')).toBe('0');
    });
});

describe('writeCompanyViewToUrl', () => {
    it('keeps the search and replaces the old view', () => {
        setSearch('?sni=96210&municipality=0180&page=4&revenueMin=1');

        writeCompanyViewToUrl({ ...defaultView, sort: 'revenue' });

        const params = new URLSearchParams(window.location.search);
        expect(params.get('sni')).toBe('96210');
        expect(params.getAll('municipality')).toEqual(['0180']);
        expect(params.get('sort')).toBe('revenue');
        expect(params.has('page')).toBe(false);
        expect(params.has('revenueMin')).toBe(false);
    });

    it('survives a round trip through the URL', () => {
        const view: CompanyView = {
            filters: {
                ...emptyFilters,
                employeeClasses: ['2', '3'],
                netResultMin: '-100',
                hasFinancials: true,
            },
            sort: 'netResult',
            order: 'desc',
            page: 2,
        };

        writeCompanyViewToUrl(view);

        expect(readCompanyViewFromUrl()).toEqual(view);
    });
});

describe('sortBy', () => {
    it('starts a new column ascending and goes back to page 1', () => {
        const view = sortBy(
            { ...defaultView, sort: 'name', order: 'desc', page: 5 },
            'revenue',
        );

        expect(view).toMatchObject({
            sort: 'revenue',
            order: 'asc',
            page: 1,
        });
    });

    it('flips the order on the same column', () => {
        const once = sortBy(defaultView, 'name');
        const twice = sortBy(once, 'name');

        expect(once.order).toBe('desc');
        expect(twice.order).toBe('asc');
    });
});

describe('clearCompanySort', () => {
    it('drops a column sort and returns to page 1', () => {
        const view = clearCompanySort({
            ...defaultView,
            filters: { ...emptyFilters, hasFinancials: true },
            sort: 'revenue',
            order: 'desc',
            page: 4,
        });

        expect(view.filters.hasFinancials).toBe(true);
        expect(view).toMatchObject({ sort: 'name', order: 'asc', page: 1 });
    });

    it('returns the same view when the company sort is already the default', () => {
        expect(clearCompanySort(defaultView)).toBe(defaultView);
    });
});

describe('selectCompanySort', () => {
    it('starts ascending even when that column is already selected', () => {
        const view = selectCompanySort(
            { ...defaultView, sort: 'name', order: 'desc', page: 3 },
            'name',
        );

        expect(view).toMatchObject({ sort: 'name', order: 'asc', page: 1 });
    });
});

describe('validateFilters', () => {
    it('accepts empty filters and equal limits', () => {
        expect(validateFilters(emptyFilters)).toEqual({});
        expect(
            validateFilters({
                ...emptyFilters,
                revenueMin: '100',
                revenueMax: '100',
            }),
        ).toEqual({});
    });

    it('compares amounts as numbers, not text', () => {
        expect(
            validateFilters({
                ...emptyFilters,
                revenueMin: '9',
                revenueMax: '100',
            }),
        ).toEqual({});
    });

    it('rejects a reversed range', () => {
        expect(
            validateFilters({
                ...emptyFilters,
                registeredFrom: '2024-01-01',
                registeredTo: '2023-01-01',
                revenueMin: '200',
                revenueMax: '100',
                netResultMin: '0',
                netResultMax: '-1',
            }),
        ).toEqual({
            registered: companyText.registeredRangeError,
            revenue: companyText.amountRangeError,
            netResult: companyText.amountRangeError,
        });
    });
});
