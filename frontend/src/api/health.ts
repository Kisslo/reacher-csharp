import { apiFetch } from './client';

export function fetchHealth(): Promise<string> {
    return apiFetch<string>('/health');
}
