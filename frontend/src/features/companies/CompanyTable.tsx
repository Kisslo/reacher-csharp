import { Fragment, useId, useMemo, useState } from 'react';
import type { ReactNode } from 'react';
import { useQuery } from '@tanstack/react-query';

import type { Company } from '../../api/companies';
import {
    scoreColumnLabel,
    scoreColumns,
    scoreValue,
} from '../analysis/sessionScore';
import type {
    ScoreColumn,
    ScoreSort,
    SessionScore,
} from '../analysis/sessionScore';
import { companyText } from '../../texts/companies';
import { employeeClassesQuery } from '../search/codeQueries';
import type { CompanySort, CompanyView } from './companyView';
import {
    CalledCheckbox,
    OutreachNote,
    WillingSelect,
    useCompanyOutreach,
} from './CompanyOutreach';
import { formatAmount, toAmount } from './format';

/** Details through Samarbete. Score and action columns are added on top. */
const fixedColumns = 9;

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
    scoreLookup?: (orgnr: string) => SessionScore | undefined;
    scoreSort?: ScoreSort | null;
    onScoreSort?: (column: ScoreColumn) => void;
    /** Orgnrs pinned at the top because they were analysed this session (T2-12). */
    analysed?: ReadonlySet<string>;
    onRowChange?: (company: Company) => void;
};

/** Analyserad, then Behov and the four Lighthouse columns. */
const sessionColumnCount = scoreColumns.length + 1;

export function CompanyTable({
    companies,
    view,
    onSort,
    extraColumns,
    busy,
    scoreLookup,
    scoreSort,
    onScoreSort,
    analysed,
    onRowChange,
}: CompanyTableProps) {
    // While a score column is the sort, no company column stays selected.
    const companySortActive = scoreSort === null || scoreSort === undefined;
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
                    <th scope="col" className="outreach">
                        {companyText.columnCalled}
                    </th>
                    <th scope="col" className="outreach">
                        {companyText.columnWilling}
                    </th>
                    {extraColumns.map((column) => (
                        <th key={column.key} scope="col">
                            {column.header}
                        </th>
                    ))}
                    {scoreLookup !== undefined && (
                        <th scope="col">{companyText.columnAnalysed}</th>
                    )}
                    {scoreLookup !== undefined &&
                        scoreSort !== undefined &&
                        onScoreSort !== undefined &&
                        scoreColumns.map((column) => (
                            <ScoreHeader
                                key={column}
                                column={column}
                                active={scoreSort?.column === column}
                                order={
                                    scoreSort?.column === column
                                        ? scoreSort.order
                                        : null
                                }
                                onSort={onScoreSort}
                            />
                        ))}
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
                        score={scoreLookup?.(company.orgnr)}
                        pinned={analysed?.has(company.orgnr) ?? false}
                        onRowChange={onRowChange}
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

function ScoreHeader({
    column,
    active,
    order,
    onSort,
}: {
    column: ScoreColumn;
    active: boolean;
    order: 'asc' | 'desc' | null;
    onSort: (column: ScoreColumn) => void;
}) {
    return (
        <th
            scope="col"
            className="numeric"
            aria-sort={
                active
                    ? order === 'asc'
                        ? 'ascending'
                        : 'descending'
                    : undefined
            }
        >
            <button
                type="button"
                className="sort-button"
                onClick={() => onSort(column)}
            >
                {scoreColumnLabel[column]}
                {active && (
                    <span aria-hidden="true">
                        {order === 'asc' ? ' ▲' : ' ▼'}
                    </span>
                )}
            </button>
        </th>
    );
}

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
    score?: SessionScore;
    pinned: boolean;
    onRowChange?: (company: Company) => void;
};

function CompanyRow({
    company,
    employees,
    extraColumns,
    showScore,
    score,
    pinned,
    onRowChange,
}: CompanyRowProps) {
    const [expanded, setExpanded] = useState(false);
    const detailsId = useId();
    const outreach = useCompanyOutreach(company, onRowChange);
    const columnCount =
        fixedColumns +
        extraColumns.length +
        (showScore ? sessionColumnCount : 0);
    // Newest first. A missing figure stays missing, even if an older year has one,
    // because that is what the revenue and result filters compare against.
    const latest = company.financials.at(0);

    return (
        <Fragment>
            <tr className={pinned ? 'analysed-row' : undefined}>
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
                <td className="outreach">
                    <CalledCheckbox
                        company={company}
                        pending={outreach.pending}
                        onCalled={outreach.saveCalled}
                    />
                </td>
                <td className="outreach">
                    <WillingSelect
                        company={company}
                        pending={outreach.pending}
                        onWilling={outreach.saveWilling}
                    />
                </td>
                {extraColumns.map((column) => (
                    <td key={column.key}>{column.cell(company)}</td>
                ))}
                {showScore && (
                    // Only whether it was analysed. Never the website or its address.
                    <td>
                        {score === undefined
                            ? companyText.missingValue
                            : score.kind === 'noWebsite'
                              ? companyText.analysedNoWebsite
                              : companyText.analysedYes}
                    </td>
                )}
                {showScore &&
                    scoreColumns.map((column) => {
                        const value = scoreValue(score, column);
                        return (
                            <td key={column} className="numeric">
                                {value === undefined
                                    ? companyText.missingValue
                                    : value}
                            </td>
                        );
                    })}
            </tr>
            {outreach.error !== null && (
                <tr className="outreach-error">
                    <td colSpan={columnCount}>
                        <p role="alert">{outreach.error}</p>
                    </td>
                </tr>
            )}
            {expanded && (
                <tr id={detailsId} className="details-row">
                    <td colSpan={columnCount}>
                        <OutreachNote
                            companyName={company.name}
                            note={outreach.noteDraft}
                            pending={outreach.pending}
                            onNote={outreach.setNoteDraft}
                            onSave={outreach.saveNote}
                        />
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
