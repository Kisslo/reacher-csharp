import { keepPreviousData, queryOptions } from '@tanstack/react-query';

import { fetchCompanies } from '../../api/companies';
import type { SearchSelection } from '../search/searchUrl';
import { appendCompanyView } from './companyView';
import type { CompanyView } from './companyView';

const pageSize = 50;

export function companiesQuery(search: SearchSelection, view: CompanyView) {
    return queryOptions({
        queryKey: ['companies', search.sni, search.municipalities, view],
        queryFn: ({ signal }) => {
            const params = new URLSearchParams({
                sni: search.sni,
                pageSize: String(pageSize),
            });
            for (const municipality of search.municipalities) {
                params.append('municipality', municipality);
            }
            appendCompanyView(params, view);

            return fetchCompanies(params, signal);
        },
        // Keeps the old rows on screen while the next page or sort loads.
        placeholderData: keepPreviousData,
    });
}
