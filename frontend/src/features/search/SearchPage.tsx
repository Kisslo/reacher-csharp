import { useEffect, useRef, useState } from 'react';
import type { FormEvent, ReactNode } from 'react';
import {
    skipToken,
    useMutation,
    useQuery,
    useQueryClient,
} from '@tanstack/react-query';

import { ApiError } from '../../api/client';
import {
    cancelJob,
    fetchCoverage,
    fetchJob,
    startSearch,
} from '../../api/searches';
import type { Coverage, StartSearchRequest } from '../../api/searches';
import { searchText } from '../../texts/search';
import { sniQuery } from './codeQueries';
import { IngestProgress } from './IngestProgress';
import { MunicipalityPicker } from './MunicipalityPicker';
import { SniPicker } from './SniPicker';
import { errorText, validateSearch } from './searchRules';
import type { SearchErrors } from './searchRules';
import { readSearchFromUrl, writeSearchToUrl } from './searchUrl';
import type { SearchSelection } from './searchUrl';
import './search.css';

const pollIntervalMs = 2000;

const dateFormat = new Intl.DateTimeFormat('sv-SE', {
    dateStyle: 'medium',
    timeStyle: 'short',
    timeZone: 'Europe/Stockholm',
});

type Stage =
    | { kind: 'idle' }
    | { kind: 'ingesting'; search: SearchSelection; jobId: string }
    | { kind: 'ready'; search: SearchSelection };

type SearchPageProps = {
    /**
     * Renders the company table. `live` is true while the ingest job is queued
     * or running, so the table keeps reloading.
     */
    renderResults: (search: SearchSelection, live: boolean) => ReactNode;
};

export function SearchPage({ renderResults }: SearchPageProps) {
    const queryClient = useQueryClient();
    const [initial] = useState(readSearchFromUrl);
    const [sni, setSni] = useState(initial.sni);
    const [municipalities, setMunicipalities] = useState(
        initial.municipalities,
    );
    const [errors, setErrors] = useState<SearchErrors>({});
    const [stage, setStage] = useState<Stage>({ kind: 'idle' });

    const searchMutation = useMutation({
        mutationFn: startSearch,
        onSuccess: (response, request) => {
            const search = {
                sni: request.sni,
                municipalities: request.municipalities,
            };
            setStage(
                response.jobId === null
                    ? { kind: 'ready', search }
                    : { kind: 'ingesting', search, jobId: response.jobId },
            );
        },
    });

    const jobId = stage.kind === 'ingesting' ? stage.jobId : null;
    const jobQuery = useQuery({
        queryKey: ['jobs', jobId],
        queryFn:
            jobId === null
                ? skipToken
                : ({ signal }) => fetchJob(jobId, signal),
        refetchInterval: (query) => {
            const status = query.state.data?.status;
            // succeeded and failed are done. cancelled is done too, but it is
            // not a failure: the progress view says the search was cancelled.
            const finished =
                query.state.status === 'error' ||
                status === 'succeeded' ||
                status === 'failed' ||
                status === 'cancelled';
            return finished ? false : pollIntervalMs;
        },
    });

    const cancelMutation = useMutation({
        mutationFn: cancelJob,
        onSuccess: async (job, cancelledJobId) => {
            // Drop an in-flight poll so it cannot write "running" back over
            // "cancelled" and keep the progress going.
            await queryClient.cancelQueries({
                queryKey: ['jobs', cancelledJobId],
            });
            queryClient.setQueryData(['jobs', cancelledJobId], job);
        },
    });

    const jobSucceeded = jobQuery.data?.status === 'succeeded';
    // The failure view's retry starts a new search. A cancelled job stays on
    // its own message, so cancel does not start one.
    const jobCancelled = jobQuery.data?.status === 'cancelled';
    const jobFailed =
        !jobCancelled &&
        (jobQuery.isError || jobQuery.data?.status === 'failed');
    const readySearch = getReadySearch(stage, jobSucceeded);
    // The table is shown as soon as a search has started, because the ingest
    // saves companies while it runs (D19). It stays after a cancel or failure,
    // so a note the user is typing is not lost.
    const resultsSearch = stage.kind === 'idle' ? null : stage.search;
    const jobRunning =
        stage.kind === 'ingesting' &&
        !jobSucceeded &&
        !jobFailed &&
        !jobCancelled;
    const jobEnded = stage.kind === 'ingesting' && !jobRunning;

    // The last reload can be up to 5 s old when the job ends, so fetch the
    // list once more to show the final batch.
    useEffect(() => {
        if (jobEnded) {
            void queryClient.invalidateQueries({ queryKey: ['companies'] });
        }
    }, [jobEnded, queryClient]);

    const coverageQuery = useQuery({
        queryKey: ['coverage', readySearch?.sni, readySearch?.municipalities],
        queryFn:
            readySearch === null
                ? skipToken
                : ({ signal }) =>
                      fetchCoverage(
                          readySearch.sni,
                          readySearch.municipalities,
                          signal,
                      ),
    });

    async function runSearch(request: StartSearchRequest) {
        const found = validateSearch(request.sni, request.municipalities);

        if (found.sni === undefined) {
            try {
                await queryClient.fetchQuery(sniQuery(request.sni));
            } catch (error) {
                if (
                    error instanceof ApiError &&
                    error.code === 'sni_not_found'
                ) {
                    found.sni = searchText.sniUnknown;
                }
                // Other errors (e.g. SCB down) are left to the server, which answers with its own code.
            }
        }

        setErrors(found);
        if (found.sni !== undefined || found.municipalities !== undefined) {
            return;
        }

        writeSearchToUrl(request);
        cancelMutation.reset();
        searchMutation.mutate(request);
    }

    // A bookmarked or reloaded URL runs its search once.
    // StrictMode runs effects twice in development; the ref keeps it to one POST.
    const startedFromUrl = useRef(false);
    useEffect(() => {
        if (startedFromUrl.current) {
            return;
        }
        startedFromUrl.current = true;
        if (initial.sni !== '' && initial.municipalities.length > 0) {
            void runSearch({ ...initial, refresh: false });
        }
    }, []);

    function handleSubmit(event: FormEvent<HTMLFormElement>) {
        event.preventDefault();
        void runSearch({ sni: sni.trim(), municipalities, refresh: false });
    }

    return (
        <section className="search" aria-labelledby="search-heading">
            <h2 id="search-heading">{searchText.heading}</h2>

            <form className="search-form" noValidate onSubmit={handleSubmit}>
                <SniPicker
                    value={sni}
                    onChange={(value) => {
                        setSni(value);
                        setErrors((current) => ({
                            ...current,
                            sni: undefined,
                        }));
                    }}
                    error={errors.sni}
                />
                <MunicipalityPicker
                    selected={municipalities}
                    onChange={(codes) => {
                        setMunicipalities(codes);
                        setErrors((current) => ({
                            ...current,
                            municipalities: undefined,
                        }));
                    }}
                    error={errors.municipalities}
                />
                <div>
                    <button type="submit" disabled={searchMutation.isPending}>
                        {searchMutation.isPending
                            ? searchText.submitting
                            : searchText.submit}
                    </button>
                </div>
                {searchMutation.isError && (
                    <p role="alert" className="field-error">
                        {errorText(searchMutation.error)}
                    </p>
                )}
            </form>

            {stage.kind === 'ingesting' && !jobSucceeded && (
                <IngestProgress
                    job={jobQuery.data}
                    failed={jobFailed}
                    onRetry={() =>
                        void runSearch({ ...stage.search, refresh: false })
                    }
                    onCancel={() => {
                        if (jobId === null || cancelMutation.isPending) {
                            return;
                        }
                        cancelMutation.mutate(jobId);
                    }}
                    cancelPending={cancelMutation.isPending}
                    cancelError={
                        cancelMutation.error instanceof Error
                            ? errorText(cancelMutation.error)
                            : undefined
                    }
                />
            )}

            {resultsSearch !== null && (
                <div className="search-ready">
                    {readySearch !== null ? (
                        <div className="refresh-row">
                            <p>{lastFetchedText(coverageQuery.data)}</p>
                            <button
                                type="button"
                                disabled={searchMutation.isPending}
                                onClick={() =>
                                    void runSearch({
                                        ...readySearch,
                                        refresh: true,
                                    })
                                }
                            >
                                {searchText.refresh}
                            </button>
                        </div>
                    ) : (
                        <p className="results-incomplete">
                            {jobRunning
                                ? searchText.resultsIncomplete
                                : searchText.resultsPartial}
                        </p>
                    )}
                    {renderResults(resultsSearch, jobRunning)}
                </div>
            )}
        </section>
    );
}

function getReadySearch(
    stage: Stage,
    jobSucceeded: boolean,
): SearchSelection | null {
    if (stage.kind === 'ready') {
        return stage.search;
    }
    if (stage.kind === 'ingesting' && jobSucceeded) {
        return stage.search;
    }
    return null;
}

/** A list is only as fresh as its oldest municipality, so show the oldest time. */
function lastFetchedText(coverage: Coverage[] | undefined): string {
    if (coverage === undefined) {
        return '';
    }

    let oldest: number | null = null;
    for (const item of coverage) {
        if (item.fetchedAt === null) {
            return searchText.lastFetchedUnknown;
        }
        const time = Date.parse(item.fetchedAt);
        if (oldest === null || time < oldest) {
            oldest = time;
        }
    }

    return oldest === null
        ? searchText.lastFetchedUnknown
        : searchText.lastFetched(dateFormat.format(oldest));
}
