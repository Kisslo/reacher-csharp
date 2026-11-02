import { apiFetch } from './client';
import type { components } from './generated';

export type Company = components['schemas']['CompanyListItem'];
export type CompanyPage = components['schemas']['CompanySearchResponse'];

export function fetchCompanies(
    params: URLSearchParams,
    signal?: AbortSignal,
): Promise<CompanyPage> {
    return apiFetch<CompanyPage>(`/api/companies?${params.toString()}`, {
        signal,
    });
}
