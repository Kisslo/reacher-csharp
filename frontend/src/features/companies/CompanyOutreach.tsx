import { useEffect, useId, useRef, useState } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';

import type { Company, CompanyPage, OutreachUpdate } from '../../api/companies';
import { updateOutreach } from '../../api/companies';
import { companyText } from '../../texts/companies';
import { errorText } from '../search/searchRules';

type OutreachChange =
    | { kind: 'called'; called: boolean }
    | { kind: 'willing'; willing: boolean | null }
    | { kind: 'note'; note: string };

/**
 * `calledAt` is the time of the call. The checkbox only cares whether it is set.
 * Willingness is a separate answer: null means the company has not said yes or no.
 */
export function isCalled(calledAt: string | null): boolean {
    return calledAt !== null && calledAt.length > 0;
}

function emptyToNull(note: string): string | null {
    const trimmed = note.trim();
    return trimmed === '' ? null : trimmed;
}

/**
 * The PUT replaces the whole row. Copy the fields this change does not touch,
 * so checking Kontaktad cannot wipe the note, and Samarbete cannot un-call.
 */
function toRequest(company: Company, change: OutreachChange): OutreachUpdate {
    return {
        called:
            change.kind === 'called'
                ? change.called
                : isCalled(company.calledAt),
        willing: change.kind === 'willing' ? change.willing : company.willing,
        note:
            change.kind === 'note'
                ? emptyToNull(change.note)
                : emptyToNull(company.note ?? ''),
    };
}

function applyOutreach(company: Company, body: OutreachUpdate): Company {
    const alreadyCalled = isCalled(company.calledAt);
    return {
        ...company,
        calledAt: body.called
            ? alreadyCalled
                ? company.calledAt
                : new Date().toISOString()
            : null,
        willing: body.willing,
        note: body.note,
    };
}

export function useCompanyOutreach(company: Company) {
    const queryClient = useQueryClient();
    const savedNote = company.note ?? '';
    const [noteDraft, setNoteDraft] = useState(savedNote);
    const [error, setError] = useState<string | null>(null);
    const savedNoteRef = useRef(savedNote);

    // Keep an untouched field in step with the server. A draft the user has
    // started stays put, including while a checkbox save is in flight.
    useEffect(() => {
        if (savedNoteRef.current === savedNote) {
            return;
        }

        const previous = savedNoteRef.current;
        savedNoteRef.current = savedNote;
        setNoteDraft((current) => (current === previous ? savedNote : current));
    }, [savedNote]);

    const mutation = useMutation({
        mutationFn: (body: OutreachUpdate) =>
            updateOutreach(company.orgnr, body),
        onMutate: async (body) => {
            setError(null);
            const previousNote = company.note ?? '';
            await queryClient.cancelQueries({ queryKey: ['companies'] });
            const snapshots = queryClient.getQueriesData<CompanyPage>({
                queryKey: ['companies'],
            });
            queryClient.setQueriesData<CompanyPage>(
                { queryKey: ['companies'] },
                (current) => {
                    if (current === undefined) {
                        return current;
                    }

                    return {
                        ...current,
                        items: current.items.map((item) =>
                            item.orgnr === company.orgnr
                                ? applyOutreach(item, body)
                                : item,
                        ),
                    };
                },
            );

            return { snapshots, previousNote };
        },
        onError: (saveError, body, context) => {
            for (const [key, data] of context?.snapshots ?? []) {
                queryClient.setQueryData(key, data);
            }
            // A rejected note must not replace the one that was already saved.
            if (
                context !== undefined &&
                body.note !== emptyToNull(context.previousNote)
            ) {
                setNoteDraft(context.previousNote);
            }
            setError(errorText(saveError));
        },
        onSuccess: (_data, body) => {
            // Trim the field when this save stored the draft. Leave a different
            // draft alone: toggling Kontaktad sends the saved note, not the typing.
            setNoteDraft((current) =>
                emptyToNull(current) === body.note
                    ? (body.note ?? '')
                    : current,
            );
        },
        onSettled: async () => {
            await queryClient.invalidateQueries({ queryKey: ['companies'] });
        },
    });

    return {
        pending: mutation.isPending,
        error,
        noteDraft,
        setNoteDraft,
        saveCalled: (called: boolean) =>
            mutation.mutate(toRequest(company, { kind: 'called', called })),
        saveWilling: (willing: boolean | null) =>
            mutation.mutate(toRequest(company, { kind: 'willing', willing })),
        saveNote: () =>
            mutation.mutate(
                toRequest(company, { kind: 'note', note: noteDraft }),
            ),
    };
}

export function CalledCheckbox({
    company,
    pending,
    onCalled,
}: {
    company: Company;
    pending: boolean;
    onCalled: (called: boolean) => void;
}) {
    return (
        <input
            type="checkbox"
            className="outreach-called"
            checked={isCalled(company.calledAt)}
            disabled={pending}
            aria-label={companyText.calledLabel(company.name)}
            onChange={(event) => onCalled(event.target.checked)}
        />
    );
}

function willingValue(willing: boolean | null): string {
    if (willing === true) {
        return 'true';
    }
    if (willing === false) {
        return 'false';
    }
    return '';
}

function willingFromValue(value: string): boolean | null {
    if (value === 'true') {
        return true;
    }
    if (value === 'false') {
        return false;
    }
    return null;
}

export function WillingSelect({
    company,
    pending,
    onWilling,
}: {
    company: Company;
    pending: boolean;
    onWilling: (willing: boolean | null) => void;
}) {
    return (
        <select
            className="outreach-willing"
            aria-label={companyText.willingLabel(company.name)}
            value={willingValue(company.willing)}
            disabled={pending}
            onChange={(event) =>
                onWilling(willingFromValue(event.target.value))
            }
        >
            <option value="">{companyText.willingUnset}</option>
            <option value="true">{companyText.willingYes}</option>
            <option value="false">{companyText.willingNo}</option>
        </select>
    );
}

export function OutreachNote({
    companyName,
    note,
    pending,
    onNote,
    onSave,
}: {
    companyName: string;
    note: string;
    pending: boolean;
    onNote: (note: string) => void;
    onSave: () => void;
}) {
    const id = useId();

    return (
        <div className="outreach-note">
            <label htmlFor={id}>{companyText.noteLabel(companyName)}</label>
            <textarea
                id={id}
                rows={2}
                maxLength={500}
                placeholder={companyText.notePlaceholder}
                value={note}
                disabled={pending}
                onChange={(event) => onNote(event.target.value)}
            />
            <button type="button" disabled={pending} onClick={onSave}>
                {companyText.noteSave}
            </button>
        </div>
    );
}
