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
import { Pagination } from './Pagination';
import './companies.css';

type CompanyResultsProps = {
    search: SearchSelection;
    extraColumns?: CompanyColumn[];
    scoreLookup?: (orgnr: string) => { reworkScore: number } | undefined;
};

export function CompanyResults({
    search,
    extraColumns = [],
    scoreLookup,
}: CompanyResultsProps) {
    const [view, setView] = useState(readCompanyViewFromUrl);
    const [scoreOrder, setScoreOrder] = useState<'asc' | 'desc' | null>(null);
    const companies = useQuery(companiesQuery(search, view));

    function changeView(next: CompanyView) {
        setView(next);
        writeCompanyViewToUrl(next);
    }

    // Behov is a session sort, so it replaces the company sort instead of
    // sitting on top of it. The direction stays in memory: a reload drops the
    // scores, and the URL only remembers company sorts.
    function activateScoreSort() {
        setScoreOrder((order) => (order === 'asc' ? 'desc' : 'asc'));
        const cleared = clearCompanySort(view);
        if (cleared !== view) {
            changeView(cleared);
        }
    }

    function activateCompanySort(sort: CompanySort) {
        const companySortActive = scoreOrder === null;
        setScoreOrder(null);
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
                    scoreOrder={scoreOrder}
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
    scoreLookup?: (orgnr: string) => { reworkScore: number } | undefined;
    scoreOrder: 'asc' | 'desc' | null;
    onScoreSort: () => void;
};

function CompanyList({
    response,
    view,
    busy,
    extraColumns,
    onChange,
    onSort,
    scoreLookup,
    scoreOrder,
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
        scoreLookup === undefined || scoreOrder === null
            ? response.items
            : [...response.items].sort((left, right) => {
                  const leftScore = scoreLookup(left.orgnr)?.reworkScore;
                  const rightScore = scoreLookup(right.orgnr)?.reworkScore;
                  if (leftScore === undefined && rightScore === undefined) {
                      return 0;
                  }
                  if (leftScore === undefined) {
                      return 1;
                  }
                  if (rightScore === undefined) {
                      return -1;
                  }
                  return scoreOrder === 'asc'
                      ? leftScore - rightScore
                      : rightScore - leftScore;
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
                companies={sortedItems}
                view={view}
                busy={busy}
                extraColumns={extraColumns}
                onSort={onSort}
                scoreLookup={scoreLookup}
                scoreOrder={scoreOrder}
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
