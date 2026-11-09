import { apiFetch } from './client';
import type { components } from './generated';

export type Company = components['schemas']['CompanyListItem'];
export type CompanyPage = components['schemas']['CompanySearchResponse'];
export type OutreachUpdate = components['schemas']['OutreachRequest'];

export function fetchCompanies(
    params: URLSearchParams,
    signal?: AbortSignal,
): Promise<CompanyPage> {
    return apiFetch<CompanyPage>(`/api/companies?${params.toString()}`, {
        signal,
    });
}

/**
 * Replaces the whole outreach row. Send the current called flag, willingness
 * and note together: a field left out would be cleared on the server.
 * The response is 204, so the caller keeps the body it sent.
 */
export function updateOutreach(
    orgnr: string,
    body: OutreachUpdate,
    signal?: AbortSignal,
): Promise<void> {
    return apiFetch<void>(
        `/api/companies/${encodeURIComponent(orgnr)}/outreach`,
        {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(body),
            signal,
        },
    );
}
