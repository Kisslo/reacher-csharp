import { queryOptions } from '@tanstack/react-query';

import {
    fetchEmployeeClasses,
    fetchMunicipalities,
    fetchRevenueClasses,
    fetchSni,
    fetchSniList,
} from '../../api/codes';

// Code lists change rarely and the server caches them for 24 hours,
// so one fetch per page load is enough.
export function municipalitiesQuery() {
    return queryOptions({
        queryKey: ['codes', 'municipalities'],
        queryFn: ({ signal }) => fetchMunicipalities(signal),
        staleTime: Infinity,
    });
}

export function employeeClassesQuery() {
    return queryOptions({
        queryKey: ['codes', 'employee-classes'],
        queryFn: ({ signal }) => fetchEmployeeClasses(signal),
        staleTime: Infinity,
    });
}

export function revenueClassesQuery() {
    return queryOptions({
        queryKey: ['codes', 'revenue-classes'],
        queryFn: ({ signal }) => fetchRevenueClasses(signal),
        staleTime: Infinity,
    });
}

export function sniListQuery() {
    return queryOptions({
        queryKey: ['codes', 'sni'],
        queryFn: ({ signal }) => fetchSniList(signal),
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
