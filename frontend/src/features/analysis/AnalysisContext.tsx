import {
    createContext,
    useCallback,
    useContext,
    useMemo,
    useState,
} from 'react';
import type { ReactNode } from 'react';
import { useMutation } from '@tanstack/react-query';

import { analyseWebsite, type WebsiteAnalysis } from '../../api/analysis';
import { analysisText } from './analysisText';
import './analysis.css';

export type SessionScore = {
    reworkScore: number;
    analysedAt: string;
};

type AnalysisState = {
    orgnr: string;
    result?: WebsiteAnalysis;
    error: Error | null;
    status: 'loading' | 'success' | 'error';
};

type AnalysisContextValue = {
    scores: ReadonlyMap<string, SessionScore>;
    analysis: AnalysisState | null;
    startAnalysis: (orgnr: string, url: string) => void;
    closeAnalysis: () => void;
    getScore: (orgnr: string) => SessionScore | undefined;
};

const AnalysisContext = createContext<AnalysisContextValue | null>(null);

export function AnalysisProvider({ children }: { children: ReactNode }) {
    const [scores, setScores] = useState<Map<string, SessionScore>>(
        () => new Map(),
    );
    const [analysis, setAnalysis] = useState<AnalysisState | null>(null);
    const mutation = useMutation({
        mutationFn: ({ url }: { url: string }) => analyseWebsite(url),
    });

    const startAnalysis = useCallback(
        (orgnr: string, url: string) => {
            setAnalysis({ orgnr, error: null, status: 'loading' });
            mutation.mutate(
                { url },
                {
                    onSuccess: (result) => {
                        setScores((current) => {
                            const next = new Map(current);
                            next.set(orgnr, {
                                reworkScore: Number(result.reworkScore),
                                analysedAt: result.fetchedAt,
                            });
                            return next;
                        });
                        setAnalysis({
                            orgnr,
                            result,
                            error: null,
                            status: 'success',
                        });
                    },
                    onError: (error: Error) => {
                        setAnalysis({ orgnr, error, status: 'error' });
                    },
                },
            );
        },
        [mutation],
    );

    const closeAnalysis = useCallback(() => setAnalysis(null), []);
    const getScore = useCallback(
        (orgnr: string) => scores.get(orgnr),
        [scores],
    );
    const value = useMemo(
        () => ({
            scores,
            analysis,
            startAnalysis,
            closeAnalysis,
            getScore,
        }),
        [analysis, closeAnalysis, getScore, scores, startAnalysis],
    );

    return (
        <AnalysisContext.Provider value={value}>
            {children}
            {analysis !== null && (
                <AnalysisView state={analysis} onClose={closeAnalysis} />
            )}
        </AnalysisContext.Provider>
    );
}

export function useAnalysis(): AnalysisContextValue {
    const value = useContext(AnalysisContext);
    if (value === null) {
        throw new Error('useAnalysis must be used within AnalysisProvider.');
    }

    return value;
}

function AnalysisView({
    state,
    onClose,
}: {
    state: AnalysisState;
    onClose: () => void;
}) {
    return (
        <>
            <div className="analysis-backdrop" onClick={onClose} />
            <dialog
                className="analysis-dialog"
                open
                aria-labelledby="analysis-title"
            >
                <div className="analysis-dialog-header">
                    <h2 id="analysis-title">{analysisText.title}</h2>
                    <button type="button" onClick={onClose}>
                        {analysisText.close}
                    </button>
                </div>
                {state.status === 'loading' && (
                    <p role="status">{analysisText.loading}</p>
                )}
                {state.status === 'error' && (
                    <p role="alert">{state.error?.message}</p>
                )}
                {state.status === 'success' && state.result !== undefined && (
                    <AnalysisResult result={state.result} />
                )}
            </dialog>
        </>
    );
}

function AnalysisResult({ result }: { result: WebsiteAnalysis }) {
    const score = Number(result.reworkScore);
    const lighthouse = result.lighthouse;
    const checks = result.checks;

    return (
        <div className="analysis-result">
            <section className={`analysis-score score-${scoreBand(score)}`}>
                <h3>{analysisText.score}</h3>
                <p
                    className="analysis-score-value"
                    aria-label={`${score} av 100`}
                >
                    {score}/100
                </p>
                <p>{analysisText.scoreExplanation}</p>
            </section>
            <section>
                <h3>{analysisText.lighthouse}</h3>
                {lighthouse === null ? (
                    <p role="note">{analysisText.checksOnly}</p>
                ) : (
                    <dl className="analysis-metrics">
                        <Metric
                            label={analysisText.performance}
                            value={lighthouse.performance}
                        />
                        <Metric
                            label={analysisText.accessibility}
                            value={lighthouse.accessibility}
                        />
                        <Metric
                            label={analysisText.bestPractices}
                            value={lighthouse.bestPractices}
                        />
                        <Metric
                            label={analysisText.seo}
                            value={lighthouse.seo}
                        />
                    </dl>
                )}
            </section>
            <section>
                <h3>{analysisText.checks}</h3>
                <dl className="analysis-metrics">
                    <Metric
                        label={analysisText.https}
                        value={
                            checks.https ? analysisText.yes : analysisText.no
                        }
                    />
                    <Metric
                        label={analysisText.viewport}
                        value={
                            checks.viewport ? analysisText.yes : analysisText.no
                        }
                    />
                    <Metric
                        label={analysisText.generator}
                        value={checks.generator ?? analysisText.missing}
                    />
                    <Metric
                        label={analysisText.copyrightYear}
                        value={checks.copyrightYear ?? analysisText.missing}
                    />
                    <Metric
                        label={analysisText.responseTime}
                        value={analysisText.milliseconds(
                            Number(checks.responseTimeMs),
                        )}
                    />
                </dl>
            </section>
        </div>
    );
}

function Metric({ label, value }: { label: string; value: ReactNode }) {
    return (
        <>
            <dt>{label}</dt>
            <dd>{value}</dd>
        </>
    );
}

function scoreBand(score: number): 'low' | 'medium' | 'high' {
    if (score < 34) return 'low';
    if (score < 67) return 'medium';
    return 'high';
}
