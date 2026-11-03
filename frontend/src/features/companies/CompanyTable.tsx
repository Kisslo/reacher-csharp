import { Fragment, useId, useMemo, useState } from 'react';
import type { ReactNode } from 'react';
import { useQuery } from '@tanstack/react-query';

import type { Company } from '../../api/companies';
import { analysisText } from '../analysis/analysisText';
import { companyText } from '../../texts/companies';
import { employeeClassesQuery } from '../search/codeQueries';
import type { CompanySort, CompanyView } from './companyView';
import { formatAmount, toAmount } from './format';

/** Team 2 adds its action and score columns through this (T2-08, T2-09). */
export type CompanyColumn = {
    key: string;
    header: ReactNode;
    cell: (company: Company) => ReactNode;
};

type CompanyTableProps = {
    companies: Company[];
    view: CompanyView;
    onSort: (sort: CompanySort) => void;
    extraColumns: CompanyColumn[];
    busy: boolean;
    scoreLookup?: (orgnr: string) => { reworkScore: number } | undefined;
    scoreOrder?: 'asc' | 'desc' | null;
    onScoreSort?: () => void;
};

export function CompanyTable({
    companies,
    view,
    onSort,
    extraColumns,
    busy,
    scoreLookup,
    scoreOrder,
    onScoreSort,
}: CompanyTableProps) {
    // While Behov is the sort, no company column stays selected.
    const companySortActive = scoreOrder !== 'asc' && scoreOrder !== 'desc';
    const employeeClasses = useQuery(employeeClassesQuery());
    const employeeNames = useMemo(
        () =>
            new Map(
                (employeeClasses.data ?? []).map((item) => [
                    item.code,
                    item.name,
                ]),
            ),
        [employeeClasses.data],
    );

    return (
        <table className="company-table" aria-busy={busy}>
            <caption>{companyText.tableCaption}</caption>
            <thead>
                <tr>
                    <th scope="col" aria-label={companyText.columnDetails} />
                    <SortHeader
                        label={companyText.columnCompany}
                        sort="name"
                        view={view}
                        companySortActive={companySortActive}
                        onSort={onSort}
                    />
                    <th scope="col">{companyText.columnWorkplaces}</th>
                    <SortHeader
                        label={companyText.columnEmployees}
                        sort="employeeClass"
                        view={view}
                        companySortActive={companySortActive}
                        onSort={onSort}
                    />
                    <SortHeader
                        label={companyText.columnRevenue}
                        sort="revenue"
                        view={view}
                        companySortActive={companySortActive}
                        onSort={onSort}
                        numeric
                    />
                    <SortHeader
                        label={companyText.columnNetResult}
                        sort="netResult"
                        view={view}
                        companySortActive={companySortActive}
                        onSort={onSort}
                        numeric
                    />
                    <SortHeader
                        label={companyText.columnRegistered}
                        sort="registeredAt"
                        view={view}
                        companySortActive={companySortActive}
                        onSort={onSort}
                    />
                    {extraColumns.map((column) => (
                        <th key={column.key} scope="col">
                            {column.header}
                        </th>
                    ))}
                    {scoreLookup !== undefined &&
                        scoreOrder !== undefined &&
                        onScoreSort !== undefined && (
                            <th
                                scope="col"
                                className="numeric"
                                aria-sort={
                                    scoreOrder === null
                                        ? 'none'
                                        : scoreOrder === 'asc'
                                          ? 'ascending'
                                          : 'descending'
                                }
                            >
                                <button
                                    type="button"
                                    className="sort-button"
                                    onClick={onScoreSort}
                                >
                                    {analysisText.sortColumn}
                                    {scoreOrder === null
                                        ? ''
                                        : scoreOrder === 'asc'
                                          ? ' ▲'
                                          : ' ▼'}
                                </button>
                            </th>
                        )}
                </tr>
            </thead>
            <tbody>
                {companies.map((company) => (
                    <CompanyRow
                        key={company.orgnr}
                        company={company}
                        employees={
                            employeeNames.get(company.employeeClass) ??
                            company.employeeClass
                        }
                        extraColumns={extraColumns}
                        showScore={scoreLookup !== undefined}
                        score={scoreLookup?.(company.orgnr)?.reworkScore}
                    />
                ))}
            </tbody>
        </table>
    );
}

type SortHeaderProps = {
    label: string;
    sort: CompanySort;
    view: CompanyView;
    companySortActive: boolean;
    onSort: (sort: CompanySort) => void;
    numeric?: boolean;
};

function SortHeader({
    label,
    sort,
    view,
    companySortActive,
    onSort,
    numeric,
}: SortHeaderProps) {
    const isActive = companySortActive && view.sort === sort;
    const ariaSort = view.order === 'asc' ? 'ascending' : 'descending';

    return (
        <th
            scope="col"
            className={numeric ? 'numeric' : undefined}
            aria-sort={isActive ? ariaSort : undefined}
        >
            <button
                type="button"
                className="sort-button"
                onClick={() => onSort(sort)}
            >
                {label}
                {isActive && (
                    <span aria-hidden="true">
                        {view.order === 'asc' ? ' ▲' : ' ▼'}
                    </span>
                )}
            </button>
        </th>
    );
}

type CompanyRowProps = {
    company: Company;
    employees: string;
    extraColumns: CompanyColumn[];
    showScore: boolean;
    score?: number;
};

function CompanyRow({
    company,
    employees,
    extraColumns,
    showScore,
    score,
}: CompanyRowProps) {
    const [expanded, setExpanded] = useState(false);
    const detailsId = useId();
    // Newest first. A missing figure stays missing, even if an older year has one,
    // because that is what the revenue and result filters compare against.
    const latest = company.financials.at(0);

    return (
        <Fragment>
            <tr>
                <td>
                    <button
                        type="button"
                        className="row-toggle"
                        aria-expanded={expanded}
                        aria-controls={expanded ? detailsId : undefined}
                        aria-label={
                            expanded
                                ? companyText.hideDetails(company.name)
                                : companyText.showDetails(company.name)
                        }
                        onClick={() => setExpanded(!expanded)}
                    >
                        <span aria-hidden="true">{expanded ? '▾' : '▸'}</span>
                    </button>
                </td>
                <td>{company.name}</td>
                <td>
                    <ul className="workplace-list">
                        {company.workplaces.map((workplace, index) => (
                            <li key={index}>
                                {workplace.name === null
                                    ? workplace.city
                                    : `${workplace.name}, ${workplace.city}`}
                            </li>
                        ))}
                    </ul>
                </td>
                <td>{employees}</td>
                <td className="numeric">
                    <Amount value={latest?.revenue ?? null} />
                </td>
                <td className="numeric">
                    <Amount value={latest?.netResult ?? null} />
                </td>
                <td className="nowrap">
                    {company.registeredAt ?? companyText.missingValue}
                </td>
                {extraColumns.map((column) => (
                    <td key={column.key}>{column.cell(company)}</td>
                ))}
                {showScore && (
                    <td className="numeric">
                        {score === undefined ? companyText.missingValue : score}
                    </td>
                )}
            </tr>
            {expanded && (
                <tr id={detailsId} className="details-row">
                    <td colSpan={7 + extraColumns.length + (showScore ? 1 : 0)}>
                        <Financials company={company} />
                    </td>
                </tr>
            )}
        </Fragment>
    );
}

function Financials({ company }: { company: Company }) {
    if (company.financials.length === 0) {
        return <p>{companyText.financialsEmpty}</p>;
    }

    return (
        <table
            className="financials-table"
            aria-label={companyText.financialsLabel(company.name)}
        >
            <thead>
                <tr>
                    <th scope="col">{companyText.columnPeriodEnd}</th>
                    <th scope="col" className="numeric">
                        {companyText.columnRevenue}
                    </th>
                    <th scope="col" className="numeric">
                        {companyText.columnNetResult}
                    </th>
                </tr>
            </thead>
            <tbody>
                {company.financials.map((year) => (
                    <tr key={year.periodEnd}>
                        <td>{year.periodEnd}</td>
                        <td className="numeric">
                            <Amount value={year.revenue} />
                        </td>
                        <td className="numeric">
                            <Amount value={year.netResult} />
                        </td>
                    </tr>
                ))}
            </tbody>
        </table>
    );
}

function Amount({ value }: { value: number | string | null }) {
    const amount = toAmount(value);
    if (amount === null) {
        return companyText.missingValue;
    }

    return (
        <span className={amount < 0 ? 'amount-negative' : undefined}>
            {formatAmount(amount)}
        </span>
    );
}
