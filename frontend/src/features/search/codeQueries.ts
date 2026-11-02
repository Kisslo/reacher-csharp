import { queryOptions } from '@tanstack/react-query';

import { fetchMunicipalities, fetchSni } from '../../api/codes';

// Code lists change rarely and the server caches them for 24 hours,
// so one fetch per page load is enough.
export function municipalitiesQuery() {
    return queryOptions({
        queryKey: ['codes', 'municipalities'],
        queryFn: ({ signal }) => fetchMunicipalities(signal),
        staleTime: Infinity,
    });
}

export function sniQuery(code: string) {
    return queryOptions({
        queryKey: ['codes', 'sni', code],
        queryFn: ({ signal }) => fetchSni(code, signal),
        staleTime: Infinity,
        // A 404 means an unknown code. Trying again won't change that.
        retry: false,
    });
}
