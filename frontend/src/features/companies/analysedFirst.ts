import type { Company } from '../../api/companies';
import type { CompanySort, SortOrder } from './companyView';
import { toAmount } from './format';

export type CompanyComparer = (left: Company, right: Company) => number;

type ArrangeInput = {
    /** The current page, in the server's order. */
    pageItems: readonly Company[];
    /** Every row this search has shown, so a company analysed on another page can be pinned. */
    seenRows: ReadonlyMap<string, Company>;
    isAnalysed: (orgnr: string) => boolean;
    compare: CompanyComparer;
};

export type ArrangedRows = {
    rows: Company[];
    analysed: ReadonlySet<string>;
};

/**
 * Analysed companies from this search come first, on every page, sorted with
 * `compare`. The rest of the page follows in the server's order: they have no
 * session score, and the server has already sorted them by the company sort.
 * Each company appears once, so a pinned row is left out of the page body.
 */
export function arrangeAnalysedFirst({
    pageItems,
    seenRows,
    isAnalysed,
    compare,
}: ArrangeInput): ArrangedRows {
    const pinned = new Map<string, Company>();
    for (const company of seenRows.values()) {
        if (isAnalysed(company.orgnr)) {
            pinned.set(company.orgnr, company);
        }
    }
    // The current page is the newest copy of a row, so it replaces the remembered one.
    for (const company of pageItems) {
        if (isAnalysed(company.orgnr)) {
            pinned.set(company.orgnr, company);
        }
    }

    const rest = pageItems.filter((company) => !pinned.has(company.orgnr));

    return {
        rows: [...[...pinned.values()].sort(compare), ...rest],
        analysed: new Set(pinned.keys()),
    };
}

/**
 * The same order as the server's ORDER BY in CompanySearch.sql: a missing value
 * last in both directions, then the value, then orgnr. Names use Swedish rules
 * in the browser, which can differ slightly from Postgres' collation. That only
 * matters inside the analysed group. Employee class follows
 * ScbCodeTables.ByCode (shorter code first, then the code). Class 0
 * ("uppgift saknas") is a missing value.
 */
export function compareCompanies(
    left: Company,
    right: Company,
    sort: CompanySort,
    order: SortOrder,
): number {
    const leftValue = sortValue(left, sort);
    const rightValue = sortValue(right, sort);

    if (leftValue === null || rightValue === null) {
        if (leftValue !== rightValue) {
            return leftValue === null ? 1 : -1;
        }
        return compareOrgnr(left, right);
    }

    const result = compareValues(leftValue, rightValue, sort);
    if (result !== 0) {
        return order === 'asc' ? result : -result;
    }

    return compareOrgnr(left, right);
}

export function compareOrgnr(left: Company, right: Company): number {
    return left.orgnr < right.orgnr ? -1 : left.orgnr > right.orgnr ? 1 : 0;
}

function sortValue(
    company: Company,
    sort: CompanySort,
): string | number | null {
    // Newest first, the same year the row shows and the filters compare against.
    const latest = company.financials.at(0);

    switch (sort) {
        case 'name':
            return company.name;
        case 'revenue':
            return toAmount(latest?.revenue ?? null);
        case 'netResult':
            return toAmount(latest?.netResult ?? null);
        case 'registeredAt':
            return company.registeredAt;
        case 'employeeClass':
            // Class 0 is "uppgift saknas": missing, so last either way.
            return company.employeeClass === '0' ? null : company.employeeClass;
    }
}

function compareValues(
    left: string | number,
    right: string | number,
    sort: CompanySort,
): number {
    if (typeof left === 'number' && typeof right === 'number') {
        return left - right;
    }

    const leftText = String(left);
    const rightText = String(right);
    if (sort === 'employeeClass') {
        // "10" is longer than "2", so it is the larger class. Same rule as
        // length(employee_class), employee_class in CompanySearch.sql.
        const byLength = leftText.length - rightText.length;
        if (byLength !== 0) {
            return byLength;
        }

        return leftText < rightText ? -1 : leftText > rightText ? 1 : 0;
    }

    return leftText.localeCompare(rightText, 'sv');
}
