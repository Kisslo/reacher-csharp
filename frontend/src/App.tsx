import { useQuery } from '@tanstack/react-query';

import { fetchHealth } from './api/health';
import { CompanyResults } from './features/companies/CompanyResults';
import {
    AnalysisProvider,
    useAnalysis,
} from './features/analysis/AnalysisContext';
import { SearchPage } from './features/search/SearchPage';
import { searchKey } from './features/search/searchUrl';
import {
    FindWebsiteAction,
    WebsiteSearchProvider,
} from './features/website/FindWebsiteAction';
import { commonText } from './texts/common';
import { websiteText } from './texts/website';

function App() {
    return (
        <div className="app-shell">
            <header className="app-header">
                <h1>Reacher</h1>
            </header>
            <AnalysisProvider>
                <AppContent />
            </AnalysisProvider>
        </div>
    );
}

function AppContent() {
    const { getScore, markNoWebsite, startAnalysis } = useAnalysis();
    const healthQuery = useQuery({
        queryKey: ['health'],
        queryFn: fetchHealth,
    });

    return (
        <main className="app-main">
            <p>{commonText.welcome}</p>
            <SearchPage
                renderResults={(search) => (
                    // A new search remounts the list, so it reads its view from the URL again.
                    <WebsiteSearchProvider key={searchKey(search)}>
                        <CompanyResults
                            key={searchKey(search)}
                            search={search}
                            extraColumns={[
                                {
                                    key: 'actions',
                                    header: websiteText.column,
                                    cell: (company) => (
                                        <FindWebsiteAction
                                            orgnr={company.orgnr}
                                            onAnalyze={(url) =>
                                                startAnalysis(
                                                    company.orgnr,
                                                    url,
                                                )
                                            }
                                            onNoWebsite={() =>
                                                markNoWebsite(company.orgnr)
                                            }
                                        />
                                    ),
                                },
                            ]}
                            scoreLookup={getScore}
                        />
                    </WebsiteSearchProvider>
                )}
            />
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
    );
}

export default App;
