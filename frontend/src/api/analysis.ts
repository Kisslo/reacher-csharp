import { apiFetch } from './client';
import type { components } from './generated';

export type WebsiteAnalysis = components['schemas']['WebsiteAnalysisResponse'];

export function analyseWebsite(
    url: string,
    signal?: AbortSignal,
): Promise<WebsiteAnalysis> {
    return apiFetch<WebsiteAnalysis>('/api/website-analysis', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ url }),
        signal,
    });
}
