import { useCallback, useEffect, useState } from 'react';
import { useIsMutating, useQuery } from '@tanstack/react-query';

import type { Company, CompanyPage } from '../../api/companies';
import { commonText } from '../../texts/common';
import { companyText } from '../../texts/companies';
import { errorText } from '../search/searchRules';
import type { SearchSelection } from '../search/searchUrl';
import {
    arrangeAnalysedFirst,
    compareCompanies,
    compareOrgnr,
} from './analysedFirst';
import type { CompanyComparer } from './analysedFirst';
import { companiesQuery } from './companyQueries';
import { CompanyFilters } from './CompanyFilters';
import { outreachMutationKey } from './CompanyOutreach';
import { CompanyTable } from './CompanyTable';
import type { CompanyColumn } from './CompanyTable';
import {
    clearCompanySort,
    readCompanyViewFromUrl,
    selectCompanySort,
    sortBy,
    writeCompanyViewToUrl,
} from './companyView';
import type { CompanySort, CompanyView } from './companyView';
import { formatCount } from './format';
import { analysisText } from '../analysis/analysisText';
import {
    compareScoreValues,
    nextScoreSort,
    scoreValue,
} from '../analysis/sessionScore';
import type {
    ScoreColumn,
    ScoreSort,
    SessionScore,
} from '../analysis/sessionScore';
import { Pagination } from './Pagination';
import './companies.css';

/** How often the list reloads while its search is still running (T1-14). */
const liveRefetchMs = 5000;

type CompanyResultsProps = {
    search: SearchSelection;
    /** True while the ingest job for this search is queued or running. */
    live?: boolean;
    extraColumns?: CompanyColumn[];
    scoreLookup?: (orgnr: string) => SessionScore | undefined;
};

export function CompanyResults({
    search,
    live = false,
    extraColumns = [],
    scoreLookup,
}: CompanyResultsProps) {
    const [view, setView] = useState(readCompanyViewFromUrl);
    const [scoreSort, setScoreSort] = useState<ScoreSort | null>(null);
    const savingOutreach =
        useIsMutating({ mutationKey: outreachMutationKey }) > 0;
    const companies = useQuery({
        ...companiesQuery(search, view),
        // The ingest commits companies in batches while it runs (D19), so
        // reload the current page until the job ends. Pause during an outreach
        // save, so a reload cannot briefly show the old value over the new one.
        refetchInterval: live && !savingOutreach ? liveRefetchMs : false,
    });
    // Every row this search has shown, in memory only. The list is remounted for
    // a new search, so this is "this search" and a reload clears it. Rows the
    // session has analysed are pinned from here on every page (T2-12).
    const [seenRows, setSeenRows] = useState<ReadonlyMap<string, Company>>(
        () => new Map(),
    );

    useEffect(() => {
        const items = companies.data?.items;
        if (items === undefined) {
            return;
        }
        setSeenRows((current) => {
            const next = new Map(current);
            for (const company of items) {
                next.set(company.orgnr, company);
            }
            return next;
        });
    }, [companies.data]);

    // A pinned row from another page is not in the cached page that outreach
    // updates, so it is kept in step here.
    const updateSeenRow = useCallback((company: Company) => {
        setSeenRows((current) =>
            current.has(company.orgnr)
                ? new Map(current).set(company.orgnr, company)
                : current,
        );
    }, []);

    function changeView(next: CompanyView) {
        setView(next);
        writeCompanyViewToUrl(next);
    }

    // The five score columns share one session sort, so it replaces the company
    // sort instead of sitting on top of it. The column and direction stay in
    // memory: a reload drops the scores, and the URL only remembers company sorts.
    function activateScoreSort(column: ScoreColumn) {
        setScoreSort((current) => nextScoreSort(current, column));
        const cleared = clearCompanySort(view);
        if (cleared !== view) {
            changeView(cleared);
        }
    }

    function activateCompanySort(sort: CompanySort) {
        const companySortActive = scoreSort === null;
        setScoreSort(null);
        changeView(
            companySortActive
                ? sortBy(view, sort)
                : selectCompanySort(view, sort),
        );
    }

    return (
        <div className="companies">
            <CompanyFilters
                filters={view.filters}
                onApply={(filters) => changeView({ ...view, filters, page: 1 })}
            />

            {companies.isPending && <p role="status">{companyText.loading}</p>}

            {companies.isError && (
                <div role="alert" className="companies-error">
                    <p>{errorText(companies.error)}</p>
                    <button
                        type="button"
                        onClick={() => void companies.refetch()}
                    >
                        {commonText.retry}
                    </button>
                </div>
            )}

            {companies.isSuccess && (
                <CompanyList
                    response={companies.data}
                    view={view}
                    busy={companies.isPlaceholderData}
                    extraColumns={extraColumns}
                    onChange={changeView}
                    onSort={activateCompanySort}
                    scoreLookup={scoreLookup}
                    scoreSort={scoreSort}
                    onScoreSort={activateScoreSort}
                    seenRows={seenRows}
                    onRowChange={updateSeenRow}
                />
            )}
        </div>
    );
}

type CompanyListProps = {
    response: CompanyPage;
    view: CompanyView;
    busy: boolean;
    extraColumns: CompanyColumn[];
    onChange: (view: CompanyView) => void;
    onSort: (sort: CompanySort) => void;
    scoreLookup?: (orgnr: string) => SessionScore | undefined;
    scoreSort: ScoreSort | null;
    onScoreSort: (column: ScoreColumn) => void;
    seenRows: ReadonlyMap<string, Company>;
    onRowChange: (company: Company) => void;
};

function CompanyList({
    response,
    view,
    busy,
    extraColumns,
    onChange,
    onSort,
    scoreLookup,
    scoreSort,
    onScoreSort,
    seenRows,
    onRowChange,
}: CompanyListProps) {
    // .NET's OpenAPI types int32 as number | string, so convert before doing maths.
    const totalCount = Number(response.totalCount);
    const lastPage = Math.max(
        1,
        Math.ceil(totalCount / Number(response.pageSize)),
    );

    if (totalCount === 0) {
        return <p role="status">{companyText.empty}</p>;
    }

    // Without a score store nothing is analysed, so the page keeps the server's order.
    const { rows, analysed } = arrangeAnalysedFirst({
        pageItems: response.items,
        seenRows,
        isAnalysed: (orgnr) => scoreLookup?.(orgnr) !== undefined,
        compare: analysedComparer(view, scoreSort, scoreLookup),
    });

    return (
        <>
            <p role="status">{companyText.hits(formatCount(totalCount))}</p>
            {scoreLookup !== undefined && (
                <p className="score-sort-note">
                    {analysisText.sortExplanation}
                </p>
            )}
            <CompanyTable
                companies={rows}
                analysed={analysed}
                onRowChange={onRowChange}
                view={view}
                busy={busy}
                extraColumns={extraColumns}
                onSort={onSort}
                scoreLookup={scoreLookup}
                scoreSort={scoreSort}
                onScoreSort={onScoreSort}
            />
            <Pagination
                page={view.page}
                lastPage={lastPage}
                onChange={(page) => onChange({ ...view, page })}
            />
        </>
    );
}

/** The active sort, score or company, orders the analysed group. */
function analysedComparer(
    view: CompanyView,
    scoreSort: ScoreSort | null,
    scoreLookup: ((orgnr: string) => SessionScore | undefined) | undefined,
): CompanyComparer {
    if (scoreSort === null || scoreLookup === undefined) {
        return (left, right) =>
            compareCompanies(left, right, view.sort, view.order);
    }

    return (left, right) =>
        compareScoreValues(
            scoreValue(scoreLookup(left.orgnr), scoreSort.column),
            scoreValue(scoreLookup(right.orgnr), scoreSort.column),
            scoreSort.order,
        ) || compareOrgnr(left, right);
}
