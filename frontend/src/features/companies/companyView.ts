import { companyText } from '../../texts/companies';

export const companySorts = [
    'name',
    'revenue',
    'netResult',
    'registeredAt',
    'employeeClass',
] as const;

export type CompanySort = (typeof companySorts)[number];
export type SortOrder = 'asc' | 'desc';

export type CompanyFilters = {
    employeeClasses: string[];
    revenueClasses: string[];
    registeredFrom: string;
    registeredTo: string;
    revenueMin: string;
    revenueMax: string;
    netResultMin: string;
    netResultMax: string;
    hasFinancials: boolean;
};

/** What the user has chosen to look at. Everything here is kept in the page URL. */
export type CompanyView = {
    filters: CompanyFilters;
    sort: CompanySort;
    order: SortOrder;
    page: number;
};

export type FilterErrors = {
    registered?: string;
    revenue?: string;
    netResult?: string;
};

export const emptyFilters: CompanyFilters = {
    employeeClasses: [],
    revenueClasses: [],
    registeredFrom: '',
    registeredTo: '',
    revenueMin: '',
    revenueMax: '',
    netResultMin: '',
    netResultMax: '',
    hasFinancials: false,
};

const defaultSort: CompanySort = 'name';
const defaultOrder: SortOrder = 'asc';

const datePattern = /^\d{4}-\d{2}-\d{2}$/;
const amountPattern = /^-?\d+(\.\d+)?$/;
const pagePattern = /^[1-9]\d{0,8}$/;

/** Every parameter the view owns. `sni` and `municipality` belong to the search form. */
const viewParameters = [
    'employeeClass',
    'revenueClass',
    'registeredFrom',
    'registeredTo',
    'revenueMin',
    'revenueMax',
    'netResultMin',
    'netResultMax',
    'hasFinancials',
    'sort',
    'order',
    'page',
];

/** Anything unknown in a hand-edited URL falls back to its default instead of a 400. */
export function readCompanyViewFromUrl(): CompanyView {
    const params = new URLSearchParams(window.location.search);
    const sort = companySorts.find((name) => name === params.get('sort'));
    const page = params.get('page') ?? '';

    return {
        filters: {
            employeeClasses: readList(params, 'employeeClass'),
            revenueClasses: readList(params, 'revenueClass'),
            registeredFrom: readValue(params, 'registeredFrom', datePattern),
            registeredTo: readValue(params, 'registeredTo', datePattern),
            revenueMin: readValue(params, 'revenueMin', amountPattern),
            revenueMax: readValue(params, 'revenueMax', amountPattern),
            netResultMin: readValue(params, 'netResultMin', amountPattern),
            netResultMax: readValue(params, 'netResultMax', amountPattern),
            hasFinancials: params.get('hasFinancials') === 'true',
        },
        sort: sort ?? defaultSort,
        order: params.get('order') === 'desc' ? 'desc' : defaultOrder,
        page: pagePattern.test(page) ? Number(page) : 1,
    };
}

function readList(params: URLSearchParams, name: string): string[] {
    const values = params
        .getAll(name)
        .map((value) => value.trim())
        .filter((value) => value !== '');

    return [...new Set(values)];
}

function readValue(
    params: URLSearchParams,
    name: string,
    pattern: RegExp,
): string {
    const value = (params.get(name) ?? '').trim();
    return pattern.test(value) ? value : '';
}

/** The page URL and the API use the same parameter names, so both can share this. */
export function appendCompanyView(
    params: URLSearchParams,
    view: CompanyView,
): void {
    const { filters } = view;

    for (const code of filters.employeeClasses) {
        params.append('employeeClass', code);
    }
    for (const code of filters.revenueClasses) {
        params.append('revenueClass', code);
    }

    const values = {
        registeredFrom: filters.registeredFrom,
        registeredTo: filters.registeredTo,
        revenueMin: filters.revenueMin,
        revenueMax: filters.revenueMax,
        netResultMin: filters.netResultMin,
        netResultMax: filters.netResultMax,
    };
    for (const [name, value] of Object.entries(values)) {
        if (value !== '') {
            params.set(name, value);
        }
    }

    if (filters.hasFinancials) {
        params.set('hasFinancials', 'true');
    }
    if (view.sort !== defaultSort) {
        params.set('sort', view.sort);
    }
    if (view.order !== defaultOrder) {
        params.set('order', view.order);
    }
    if (view.page > 1) {
        params.set('page', String(view.page));
    }
}

export function writeCompanyViewToUrl(view: CompanyView): void {
    const params = new URLSearchParams(window.location.search);
    for (const name of viewParameters) {
        params.delete(name);
    }
    appendCompanyView(params, view);

    window.history.replaceState(
        null,
        '',
        `${window.location.pathname}?${params.toString()}`,
    );
}

/** The same column flips the order. A new column starts ascending. Both go back to page 1. */
export function sortBy(view: CompanyView, sort: CompanySort): CompanyView {
    const order = view.sort === sort && view.order === 'asc' ? 'desc' : 'asc';
    return { ...view, sort, order, page: 1 };
}

/**
 * Drops the column sort back to the default, which the URL and the request omit.
 * The same view is returned when there is nothing to clear, so the caller can
 * skip writing the URL again.
 */
export function clearCompanySort(view: CompanyView): CompanyView {
    if (
        view.sort === defaultSort &&
        view.order === defaultOrder &&
        view.page === 1
    ) {
        return view;
    }

    return { ...view, sort: defaultSort, order: defaultOrder, page: 1 };
}

/**
 * Selects a column ascending on page 1, even when the view already has it.
 * Leaving the score sort uses this: that column was not the active sort, so
 * the click must not flip it.
 */
export function selectCompanySort(
    view: CompanyView,
    sort: CompanySort,
): CompanyView {
    return { ...view, sort, order: defaultOrder, page: 1 };
}

/** The same rules as CompanySearchQueryValidator. The server stays the authority. */
export function validateFilters(filters: CompanyFilters): FilterErrors {
    const errors: FilterErrors = {};

    if (
        filters.registeredFrom !== '' &&
        filters.registeredTo !== '' &&
        // yyyy-MM-dd sorts correctly as text.
        filters.registeredFrom > filters.registeredTo
    ) {
        errors.registered = companyText.registeredRangeError;
    }
    if (isReversed(filters.revenueMin, filters.revenueMax)) {
        errors.revenue = companyText.amountRangeError;
    }
    if (isReversed(filters.netResultMin, filters.netResultMax)) {
        errors.netResult = companyText.amountRangeError;
    }

    return errors;
}

function isReversed(min: string, max: string): boolean {
    return min !== '' && max !== '' && Number(min) > Number(max);
}
