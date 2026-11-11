import type { Job } from '../../api/searches';
import { commonText } from '../../texts/common';
import { searchText } from '../../texts/search';

type IngestProgressProps = {
    job: Job | undefined;
    failed: boolean;
    onRetry: () => void;
    onCancel: () => void;
    cancelPending: boolean;
    cancelError: string | undefined;
};

export function IngestProgress({
    job,
    failed,
    onRetry,
    onCancel,
    cancelPending,
    cancelError,
}: IngestProgressProps) {
    if (job?.status === 'cancelled') {
        return (
            <div className="ingest-progress">
                <p role="status">{searchText.progressCancelled}</p>
            </div>
        );
    }

    if (failed) {
        return (
            <div role="alert" className="ingest-progress">
                <p>{searchText.progressFailed}</p>
                <button type="button" onClick={onRetry}>
                    {commonText.retry}
                </button>
            </div>
        );
    }

    // .NET's OpenAPI types int32 as number | string, so convert before doing maths.
    const done = job === undefined ? 0 : Number(job.done);
    const total =
        job === undefined || job.total === null ? null : Number(job.total);
    const showBar = job?.status === 'running' && total !== null && total > 0;
    const canCancel = job?.status === 'queued' || job?.status === 'running';

    return (
        <div className="ingest-progress">
            <p role="status">{progressText(job, done, total)}</p>
            {showBar ? (
                <progress
                    value={done}
                    max={total}
                    aria-label={searchText.progressLabel}
                />
            ) : (
                <progress aria-label={searchText.progressLabel} />
            )}
            {canCancel && (
                <button
                    type="button"
                    onClick={onCancel}
                    disabled={cancelPending}
                >
                    {searchText.cancel}
                </button>
            )}
            {cancelError !== undefined && (
                <p role="alert" className="field-error">
                    {cancelError}
                </p>
            )}
        </div>
    );
}

function progressText(
    job: Job | undefined,
    done: number,
    total: number | null,
): string {
    if (job === undefined || job.status === 'queued') {
        return searchText.progressQueued;
    }

    switch (job.phase) {
        case 'companies':
            return searchText.progressCompanies(done, total);
        case 'financials':
            return searchText.progressFinancials(done, total);
        default:
            return searchText.progressWorkplaces;
    }
}
