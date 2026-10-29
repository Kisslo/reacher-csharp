import { useQuery } from '@tanstack/react-query';

import { fetchHealth } from './api/health';
import { commonText } from './texts/common';

function App() {
    const healthQuery = useQuery({
        queryKey: ['health'],
        queryFn: fetchHealth,
    });

    return (
        <div className="app-shell">
            <header className="app-header">
                <h1>Reacher</h1>
            </header>
            <main className="app-main">
                <p>{commonText.welcome}</p>
                <section aria-labelledby="health-heading">
                    <h2 id="health-heading">{commonText.healthCheck}</h2>
                    {healthQuery.isPending && <p>{commonText.healthLoading}</p>}
                    {healthQuery.isSuccess && <p>{commonText.healthSuccess}</p>}
                    {healthQuery.isError && (
                        <div role="alert">
                            <p>
                                {healthQuery.error.message ||
                                    commonText.healthFailure}
                            </p>
                            <button
                                type="button"
                                onClick={() => void healthQuery.refetch()}
                            >
                                {commonText.retry}
                            </button>
                        </div>
                    )}
                </section>
            </main>
        </div>
    );
}

export default App;
