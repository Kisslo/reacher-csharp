import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render } from '@testing-library/react';
import type { ReactElement } from 'react';

/** A new client per render, so tests do not share a cache. Retries are off. */
export function renderWithQuery(ui: ReactElement) {
    const queryClient = new QueryClient({
        defaultOptions: {
            queries: {
                retry: false,
            },
        },
    });

    return render(
        <QueryClientProvider client={queryClient}>{ui}</QueryClientProvider>,
    );
}
