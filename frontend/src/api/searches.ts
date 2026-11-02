import { apiFetch } from './client';
import type { components } from './generated';

export type SearchResponse = components['schemas']['SearchResponse'];
export type Job = components['schemas']['JobResponse'];
export type Coverage = components['schemas']['CoverageItem'];
type CompanySearchResponse = components['schemas']['CompanySearchResponse'];

export type StartSearchRequest = {
    sni: string;
    municipalities: string[];
    refresh: boolean;
};

export function startSearch(
    request: StartSearchRequest,
): Promise<SearchResponse> {
    return apiFetch<SearchResponse>('/api/searches', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(request),
    });
}

export function fetchJob(jobId: string, signal?: AbortSignal): Promise<Job> {
    return apiFetch<Job>(`/api/jobs/${encodeURIComponent(jobId)}`, { signal });
}

/**
 * The contract only has coverage on GET /api/companies, so we ask for one row
 * and keep the coverage part.
 */
export async function fetchCoverage(
    sni: string,
    municipalities: string[],
    signal?: AbortSignal,
): Promise<Coverage[]> {
    const params = new URLSearchParams({ sni, pageSize: '1' });
    for (const municipality of municipalities) {
        params.append('municipality', municipality);
    }

    const response = await apiFetch<CompanySearchResponse>(
        `/api/companies?${params.toString()}`,
        { signal },
    );
    return response.coverage;
}
