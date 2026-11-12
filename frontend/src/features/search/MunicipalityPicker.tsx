import { useEffect, useId, useMemo, useState } from 'react';
import type { KeyboardEvent } from 'react';
import { useQuery } from '@tanstack/react-query';

import { commonText } from '../../texts/common';
import { searchText } from '../../texts/search';
import { municipalitiesQuery } from './codeQueries';
import {
    errorText,
    filterMunicipalities,
    maxMunicipalities,
} from './searchRules';

type MunicipalityPickerProps = {
    selected: string[];
    onChange: (codes: string[]) => void;
    error: string | undefined;
};

/** Multi-select combobox following the WAI-ARIA combobox pattern. */
export function MunicipalityPicker({
    selected,
    onChange,
    error,
}: MunicipalityPickerProps) {
    const id = useId();
    const inputId = `${id}-input`;
    const listId = `${id}-list`;
    const hintId = `${id}-hint`;
    const countId = `${id}-count`;
    const errorId = `${id}-error`;
    const optionId = (code: string) => `${id}-option-${code}`;

    const query = useQuery(municipalitiesQuery());
    const [filter, setFilter] = useState('');
    const [open, setOpen] = useState(false);
    const [activeIndex, setActiveIndex] = useState(0);

    const names = useMemo(
        () =>
            new Map(
                (query.data ?? []).map((municipality) => [
                    municipality.code,
                    municipality.name,
                ]),
            ),
        [query.data],
    );
    const matches = useMemo(
        () => filterMunicipalities(query.data ?? [], filter),
        [query.data, filter],
    );

    const isFull = selected.length >= maxMunicipalities;
    const safeIndex =
        matches.length === 0 ? -1 : Math.min(activeIndex, matches.length - 1);
    const active = safeIndex === -1 ? undefined : matches[safeIndex];
    const activeId = active === undefined ? undefined : optionId(active.code);

    // Keep the highlighted option visible when arrow keys move past the edge.
    useEffect(() => {
        if (open && activeId !== undefined) {
            document
                .getElementById(activeId)
                ?.scrollIntoView({ block: 'nearest' });
        }
    }, [open, activeId]);

    function toggle(code: string) {
        if (selected.includes(code)) {
            onChange(selected.filter((selectedCode) => selectedCode !== code));
        } else if (isFull) {
            // Nothing changes when the list is full, so keep what the user typed.
            return;
        } else {
            onChange([...selected, code]);
        }

        // Start the next search from an empty box. The list stays open and focus stays in the input.
        setFilter('');
        setActiveIndex(0);
    }

    function handleKeyDown(event: KeyboardEvent<HTMLInputElement>) {
        switch (event.key) {
            case 'ArrowDown':
                event.preventDefault();
                if (!open) {
                    setOpen(true);
                    return;
                }
                setActiveIndex(Math.min(safeIndex + 1, matches.length - 1));
                return;
            case 'ArrowUp':
                event.preventDefault();
                setActiveIndex(Math.max(safeIndex - 1, 0));
                return;
            case 'Enter':
                // Enter picks an option while the list is open, and submits the form otherwise.
                if (open && active !== undefined) {
                    event.preventDefault();
                    toggle(active.code);
                }
                return;
            case 'Escape':
                // WAI-ARIA combobox: the first Escape closes the list, the next one clears the text.
                if (open) {
                    event.preventDefault();
                    setOpen(false);
                } else if (filter !== '') {
                    event.preventDefault();
                    setFilter('');
                    setActiveIndex(0);
                }
                return;
            case 'Backspace':
                if (filter === '' && selected.length > 0) {
                    onChange(selected.slice(0, -1));
                }
                return;
        }
    }

    const describedBy = [hintId, countId, error === undefined ? null : errorId]
        .filter((part) => part !== null)
        .join(' ');

    return (
        <div
            className="field"
            onBlur={(event) => {
                // Close only when focus leaves the whole picker, not when it moves inside it.
                if (!event.currentTarget.contains(event.relatedTarget)) {
                    setOpen(false);
                }
            }}
        >
            <label htmlFor={inputId}>{searchText.municipalitiesLabel}</label>
            <p id={hintId} className="field-hint">
                {searchText.municipalitiesHint}
            </p>

            {selected.length > 0 && (
                <ul
                    className="chip-list"
                    aria-label={searchText.selectedMunicipalities}
                >
                    {selected.map((code) => {
                        const name = names.get(code) ?? code;
                        return (
                            <li key={code} className="chip">
                                {name}
                                <button
                                    type="button"
                                    className="chip-remove"
                                    aria-label={searchText.removeMunicipality(
                                        name,
                                    )}
                                    onClick={() =>
                                        onChange(
                                            selected.filter(
                                                (selectedCode) =>
                                                    selectedCode !== code,
                                            ),
                                        )
                                    }
                                >
                                    ×
                                </button>
                            </li>
                        );
                    })}
                </ul>
            )}

            <div className="combobox">
                <input
                    id={inputId}
                    type="text"
                    role="combobox"
                    autoComplete="off"
                    aria-expanded={open}
                    aria-controls={listId}
                    aria-autocomplete="list"
                    aria-activedescendant={open ? activeId : undefined}
                    aria-describedby={describedBy}
                    aria-invalid={error !== undefined}
                    disabled={!query.isSuccess}
                    value={filter}
                    onChange={(event) => {
                        setFilter(event.target.value);
                        setActiveIndex(0);
                        setOpen(true);
                    }}
                    onClick={() => setOpen(true)}
                    onKeyDown={handleKeyDown}
                />

                {open && matches.length > 0 && (
                    <ul
                        id={listId}
                        role="listbox"
                        aria-multiselectable="true"
                        aria-label={searchText.municipalitiesLabel}
                        className="listbox"
                        // Keeps focus in the input when an option is clicked.
                        onMouseDown={(event) => event.preventDefault()}
                    >
                        {matches.map((municipality, index) => {
                            const isSelected = selected.includes(
                                municipality.code,
                            );
                            return (
                                <li
                                    key={municipality.code}
                                    id={optionId(municipality.code)}
                                    role="option"
                                    aria-selected={isSelected}
                                    aria-disabled={!isSelected && isFull}
                                    className={
                                        index === safeIndex
                                            ? 'option option-active'
                                            : 'option'
                                    }
                                    onClick={() => toggle(municipality.code)}
                                    onMouseMove={() => setActiveIndex(index)}
                                >
                                    <span
                                        className="option-check"
                                        aria-hidden="true"
                                    >
                                        {isSelected ? '✓' : ''}
                                    </span>
                                    {municipality.name}
                                </li>
                            );
                        })}
                    </ul>
                )}

                {open && query.isSuccess && matches.length === 0 && (
                    <p className="listbox listbox-empty">
                        {searchText.municipalitiesEmpty}
                    </p>
                )}
            </div>

            <p id={countId} className="field-status" aria-live="polite">
                {searchText.selectedCount(selected.length, maxMunicipalities)}
                {isFull && ` ${searchText.municipalitiesMax}`}
            </p>

            {query.isPending && (
                <p className="field-status">
                    {searchText.municipalitiesLoading}
                </p>
            )}
            {query.isError && (
                <div role="alert" className="field-error">
                    <p>{errorText(query.error)}</p>
                    <button type="button" onClick={() => void query.refetch()}>
                        {commonText.retry}
                    </button>
                </div>
            )}

            {error !== undefined && (
                <p id={errorId} className="field-error">
                    {error}
                </p>
            )}
        </div>
    );
}
