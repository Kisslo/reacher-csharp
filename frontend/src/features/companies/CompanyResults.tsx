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
    readCompanyViewFromUrl,
    sortBy,
    writeCompanyViewToUrl,
} from './companyView';
import type { CompanyView } from './companyView';
import { formatCount } from './format';
import { Pagination } from './Pagination';
import './companies.css';

type CompanyResultsProps = {
    search: SearchSelection;
    extraColumns?: CompanyColumn[];
};

export function CompanyResults({
    search,
    extraColumns = [],
}: CompanyResultsProps) {
    const [view, setView] = useState(readCompanyViewFromUrl);
    const companies = useQuery(companiesQuery(search, view));

    function changeView(next: CompanyView) {
        setView(next);
        writeCompanyViewToUrl(next);
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
};

function CompanyList({
    response,
    view,
    busy,
    extraColumns,
    onChange,
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

    return (
        <>
            <p role="status">{companyText.hits(formatCount(totalCount))}</p>
            <CompanyTable
                companies={response.items}
                view={view}
                busy={busy}
                extraColumns={extraColumns}
                onSort={(sort) => onChange(sortBy(view, sort))}
            />
            <Pagination
                page={view.page}
                lastPage={lastPage}
                onChange={(page) => onChange({ ...view, page })}
            />
        </>
    );
}
