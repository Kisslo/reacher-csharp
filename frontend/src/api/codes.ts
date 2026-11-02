import { apiFetch } from './client';
import type { components } from './generated';

export type CodeItem = components['schemas']['CodeResponse'];

export function fetchMunicipalities(signal?: AbortSignal): Promise<CodeItem[]> {
    return apiFetch<CodeItem[]>('/api/codes/municipalities', { signal });
}

export function fetchSni(
    code: string,
    signal?: AbortSignal,
): Promise<CodeItem> {
    return apiFetch<CodeItem>(`/api/codes/sni/${encodeURIComponent(code)}`, {
        signal,
    });
}
