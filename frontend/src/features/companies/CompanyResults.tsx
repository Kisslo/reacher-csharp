import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';

import type { CompanyPage } from '../../api/companies';
import { commonText } from '../../texts/common';
import { companyText } from '../../texts/companies';
import { errorText } from '../search/searchRules';
import type { SearchSelection } from '../search/searchUrl';
import { companiesQuery } from './companyQueries';
import { CompanyFilters } from './CompanyFilters';
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

type CompanyResultsProps = {
    search: SearchSelection;
    extraColumns?: CompanyColumn[];
    scoreLookup?: (orgnr: string) => SessionScore | undefined;
};

export function CompanyResults({
    search,
    extraColumns = [],
    scoreLookup,
}: CompanyResultsProps) {
    const [view, setView] = useState(readCompanyViewFromUrl);
    const [scoreSort, setScoreSort] = useState<ScoreSort | null>(null);
    const companies = useQuery(companiesQuery(search, view));

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

    const sortedItems =
        scoreLookup === undefined || scoreSort === null
            ? response.items
            : [...response.items].sort((left, right) =>
                  compareScoreValues(
                      scoreValue(scoreLookup(left.orgnr), scoreSort.column),
                      scoreValue(scoreLookup(right.orgnr), scoreSort.column),
                      scoreSort.order,
                  ),
              );

    return (
        <>
            <p role="status">{companyText.hits(formatCount(totalCount))}</p>
            {scoreLookup !== undefined && (
                <p className="score-sort-note">
                    {analysisText.sortExplanation}
                </p>
            )}
            <CompanyTable
                companies={sortedItems}
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
