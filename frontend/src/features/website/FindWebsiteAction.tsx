import {
    createContext,
    useContext,
    useEffect,
    useId,
    useMemo,
    useRef,
    useState,
} from 'react';
import type { ReactNode } from 'react';
import { useMutation } from '@tanstack/react-query';

import type { WebsiteCandidate } from '../../api/websites';
import { findWebsiteCandidates } from '../../api/websites';
import { getErrorMessage } from '../../texts/errors';
import { websiteText } from '../../texts/website';
import { errorText } from '../search/searchRules';
import { pastedWebsiteUrl, websiteDomain } from './websiteUrl';
import './website.css';

type WebsiteSearch = {
    openOrgnr: string | null;
    searchId: number;
    open: (orgnr: string) => void;
    close: () => void;
};

const WebsiteSearchContext = createContext<WebsiteSearch | null>(null);

/**
 * Tracks which company is open. Only the organisation number is kept here.
 * Candidates live in the dialog, and the dialog unmounts when it closes.
 */
export function WebsiteSearchProvider({ children }: { children: ReactNode }) {
    const [openOrgnr, setOpenOrgnr] = useState<string | null>(null);
    const [searchId, setSearchId] = useState(0);
    const value = useMemo<WebsiteSearch>(
        () => ({
            openOrgnr,
            searchId,
            open: (orgnr) => {
                setOpenOrgnr(orgnr);
                // A new id remounts the dialog, so the same company searches again.
                setSearchId((current) => current + 1);
            },
            close: () => {
                setOpenOrgnr(null);
            },
        }),
        [openOrgnr, searchId],
    );

    return (
        <WebsiteSearchContext.Provider value={value}>
            {children}
        </WebsiteSearchContext.Provider>
    );
}

function useWebsiteSearch(): WebsiteSearch {
    const value = useContext(WebsiteSearchContext);
    if (value === null) {
        throw new Error(
            'FindWebsiteAction must be used within WebsiteSearchProvider.',
        );
    }

    return value;
}

type FindWebsiteActionProps = {
    orgnr: string;
    onAnalyze: (url: string) => void;
};

export function FindWebsiteAction({
    orgnr,
    onAnalyze,
}: FindWebsiteActionProps) {
    const search = useWebsiteSearch();
    const open = search.openOrgnr === orgnr;

    return (
        <div className="website-action">
            <button
                type="button"
                className="find-website"
                onClick={() => search.open(orgnr)}
            >
                {websiteText.find}
            </button>
            {open && (
                <WebsiteDialog
                    key={search.searchId}
                    orgnr={orgnr}
                    onAnalyze={onAnalyze}
                    onClose={search.close}
                />
            )}
        </div>
    );
}

type WebsiteDialogProps = {
    orgnr: string;
    onAnalyze: (url: string) => void;
    onClose: () => void;
};

function WebsiteDialog({ orgnr, onAnalyze, onClose }: WebsiteDialogProps) {
    const dialogRef = useRef<HTMLDialogElement>(null);
    const titleId = useId();
    const pasteId = useId();
    const pasteErrorId = useId();
    const [paste, setPaste] = useState('');
    const candidates = useMutation({
        mutationFn: () => findWebsiteCandidates(orgnr),
        // The dialog unmounts on close. gcTime 0 drops the Brave response then.
        gcTime: 0,
    });
    const { mutate } = candidates;
    // StrictMode runs effects twice in development. mutate() is not idempotent:
    // each call spends Brave quota, so the ref keeps one click to one POST.
    const started = useRef(false);

    useEffect(() => {
        dialogRef.current?.focus();
    }, []);

    useEffect(() => {
        // The open attribute is owned by React. showModal() would set it in the
        // DOM, and the next render would remove it and close the dialog.
        function onKey(event: KeyboardEvent) {
            if (event.key === 'Escape') {
                event.preventDefault();
                onClose();
            }
        }

        document.addEventListener('keydown', onKey);
        return () => document.removeEventListener('keydown', onKey);
    }, [onClose]);

    useEffect(() => {
        if (started.current) {
            return;
        }
        started.current = true;
        mutate();
    }, [mutate]);

    const pasted = pastedWebsiteUrl(paste);
    const pasteError =
        paste.trim() === '' || pasted !== undefined
            ? undefined
            : getErrorMessage('invalid_url');
    const shown = (candidates.data?.candidates ?? []).slice(0, 3);
    const waiting = candidates.isPending || candidates.isIdle;
    const quota = quotaCount(candidates.data?.remainingMonthlyQuota);

    return (
        <>
            <div className="website-backdrop" onClick={onClose} />
            <dialog
                ref={dialogRef}
                className="website-dialog"
                aria-labelledby={titleId}
                open
                tabIndex={-1}
            >
                <div className="website-dialog-header">
                    <h3 id={titleId}>{websiteText.dialogTitle}</h3>
                    <button type="button" onClick={onClose}>
                        {websiteText.close}
                    </button>
                </div>
                <p className="website-note">{websiteText.quotaNote}</p>

                {waiting && (
                    <p role="status" className="website-status">
                        {websiteText.finding}
                    </p>
                )}
                {candidates.isError && (
                    <p role="alert" className="field-error">
                        {errorText(candidates.error)}
                    </p>
                )}
                {candidates.isSuccess && shown.length === 0 && (
                    <p role="status" className="website-status">
                        {websiteText.empty}
                    </p>
                )}
                {candidates.isSuccess && shown.length > 0 && (
                    <ul
                        className="website-candidates"
                        aria-label={websiteText.candidatesLabel}
                    >
                        {shown.map((candidate, index) => (
                            <Candidate
                                key={index}
                                candidate={candidate}
                                onAnalyze={onAnalyze}
                            />
                        ))}
                    </ul>
                )}
                {quota !== undefined && (
                    <p className="website-quota">
                        {websiteText.quotaRemaining(quota)}
                    </p>
                )}

                <div className="website-paste">
                    <label htmlFor={pasteId}>{websiteText.pasteLabel}</label>
                    <input
                        id={pasteId}
                        type="text"
                        inputMode="url"
                        autoComplete="off"
                        spellCheck={false}
                        value={paste}
                        aria-invalid={
                            pasteError === undefined ? undefined : true
                        }
                        aria-describedby={
                            pasteError === undefined ? undefined : pasteErrorId
                        }
                        onChange={(event) => setPaste(event.target.value)}
                    />
                    {pasteError !== undefined && (
                        <p
                            id={pasteErrorId}
                            role="alert"
                            className="field-error"
                        >
                            {pasteError}
                        </p>
                    )}
                    {pasted !== undefined && (
                        <div
                            className="website-candidate-actions"
                            role="group"
                            aria-label={websiteText.pasteActions}
                        >
                            <WebsiteLink url={pasted} />
                            <AnalyzeButton url={pasted} onAnalyze={onAnalyze} />
                        </div>
                    )}
                </div>
            </dialog>
        </>
    );
}

function Candidate({
    candidate,
    onAnalyze,
}: {
    candidate: WebsiteCandidate;
    onAnalyze: (url: string) => void;
}) {
    const domain = websiteDomain(candidate.url);
    const title = candidate.title?.trim() ?? '';

    return (
        <li>
            <p className="website-candidate-heading">
                {domain === '' ? websiteText.unknownDomain : domain}
                {title !== '' && (
                    <span className="website-candidate-name">{title}</span>
                )}
            </p>
            <p className="website-candidate-confidence">
                {websiteText.confidence[candidate.confidence]}
                {candidate.preselected && (
                    <span className="website-best">
                        {websiteText.bestMatch}
                    </span>
                )}
            </p>
            <p className="website-candidate-actions">
                <WebsiteLink url={candidate.url} />
                <AnalyzeButton url={candidate.url} onAnalyze={onAnalyze} />
            </p>
        </li>
    );
}

function WebsiteLink({ url }: { url: string }) {
    return (
        <a href={url} target="_blank" rel="noopener noreferrer">
            {websiteText.open}
        </a>
    );
}

function AnalyzeButton({
    url,
    onAnalyze,
}: {
    url: string;
    onAnalyze: (url: string) => void;
}) {
    return (
        <button type="button" onClick={() => onAnalyze(url)}>
            {websiteText.analyze}
        </button>
    );
}

/** OpenAPI types the counter as number | string. Hide it when it is not a count. */
function quotaCount(value: number | string | undefined): number | undefined {
    if (value === undefined) {
        return undefined;
    }

    const count = typeof value === 'number' ? value : Number(value);
    if (!Number.isInteger(count) || count < 0) {
        return undefined;
    }

    return count;
}
