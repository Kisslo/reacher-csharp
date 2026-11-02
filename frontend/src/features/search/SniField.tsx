import { useId, useState } from 'react';
import { useQuery } from '@tanstack/react-query';

import { ApiError } from '../../api/client';
import { searchText } from '../../texts/search';
import { sniQuery } from './codeQueries';
import { errorText, isSniFormat } from './searchRules';

type SniFieldProps = {
    value: string;
    onChange: (value: string) => void;
    error: string | undefined;
};

export function SniField({ value, onChange, error }: SniFieldProps) {
    const id = useId();
    const hintId = `${id}-hint`;
    const statusId = `${id}-status`;

    // The code as it was when the field last lost focus. The lookup runs on blur,
    // not on every key press.
    const [checked, setChecked] = useState(value.trim());
    const isCurrent = checked === value.trim();
    const lookup = useQuery({
        ...sniQuery(checked),
        enabled: isSniFormat(checked),
    });

    let message = '';
    let isError = false;
    if (error !== undefined) {
        message = error;
        isError = true;
    } else if (!isCurrent || checked === '') {
        message = '';
    } else if (!isSniFormat(checked)) {
        message = searchText.sniFormat;
        isError = true;
    } else if (lookup.isPending) {
        message = searchText.sniChecking;
    } else if (lookup.isError) {
        message =
            lookup.error instanceof ApiError &&
            lookup.error.code === 'sni_not_found'
                ? searchText.sniUnknown
                : errorText(lookup.error);
        isError = true;
    } else {
        message = lookup.data.name;
    }

    return (
        <div className="field">
            <label htmlFor={id}>{searchText.sniLabel}</label>
            <input
                id={id}
                type="text"
                inputMode="numeric"
                autoComplete="off"
                maxLength={5}
                value={value}
                aria-invalid={isError}
                aria-describedby={`${hintId} ${statusId}`}
                onChange={(event) => onChange(event.target.value)}
                onBlur={() => setChecked(value.trim())}
            />
            <p id={hintId} className="field-hint">
                {searchText.sniHint}
            </p>
            <p
                id={statusId}
                className={isError ? 'field-error' : 'field-status'}
                aria-live="polite"
            >
                {message}
            </p>
        </div>
    );
}
