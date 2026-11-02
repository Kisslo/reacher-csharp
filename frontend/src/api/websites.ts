import { apiFetch } from './client';
import type { components } from './generated';

export type WebsiteCandidates =
    components['schemas']['WebsiteCandidatesResponse'];
export type WebsiteCandidate =
    components['schemas']['WebsiteCandidateResponse'];

/** One company, one Brave search. The URL is never put in the query string. */
export function findWebsiteCandidates(
    orgnr: string,
    signal?: AbortSignal,
): Promise<WebsiteCandidates> {
    return apiFetch<WebsiteCandidates>(
        `/api/companies/${encodeURIComponent(orgnr)}/website-candidates`,
        { method: 'POST', signal },
    );
}
