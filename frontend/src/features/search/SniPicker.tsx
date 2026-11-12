import { useEffect, useId, useMemo, useRef, useState } from 'react';
import type { KeyboardEvent } from 'react';
import { useQuery } from '@tanstack/react-query';

import { commonText } from '../../texts/common';
import { searchText } from '../../texts/search';
import { sniListQuery } from './codeQueries';
import {
    errorText,
    filterSni,
    isSniFormat,
    sniCodeFromText,
} from './searchRules';

type SniPickerProps = {
    /** The form value: a 5-digit code once one is picked or typed. */
    value: string;
    onChange: (value: string) => void;
    error: string | undefined;
};

/** Single-select combobox following the WAI-ARIA combobox pattern. */
export function SniPicker({ value, onChange, error }: SniPickerProps) {
    const id = useId();
    const inputId = `${id}-input`;
    const listId = `${id}-list`;
    const hintId = `${id}-hint`;
    const statusId = `${id}-status`;
    const optionId = (code: string) => `${id}-option-${code}`;

    const query = useQuery(sniListQuery());
    // What the user has typed. null shows "code name" for the current value
    // instead, so a picked code or one from the page URL reads as a label.
    const [text, setText] = useState<string | null>(null);
    const [open, setOpen] = useState(false);
    const [activeIndex, setActiveIndex] = useState(0);
    // A click that focuses the input would otherwise undo select() on mouse-up.
    const focusedByMouse = useRef(false);

    const names = useMemo(
        () => new Map((query.data ?? []).map((sni) => [sni.code, sni.name])),
        [query.data],
    );
    // While the label is shown, the whole list is offered.
    const matches = useMemo(
        () => filterSni(query.data ?? [], text ?? ''),
        [query.data, text],
    );

    const name = names.get(value);
    const shownText =
        text ??
        (name === undefined ? value : searchText.sniOption(value, name));

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

    function openList() {
        if (open) {
            return;
        }
        // Start on the picked code, so the list opens where the user left it.
        if (text === null) {
            const picked = matches.findIndex((sni) => sni.code === value);
            setActiveIndex(Math.max(picked, 0));
        }
        setOpen(true);
    }

    function pick(code: string) {
        onChange(code);
        setText(null);
        setOpen(false);
    }

    function clear() {
        setText('');
        setActiveIndex(0);
        onChange('');
    }

    function handleKeyDown(event: KeyboardEvent<HTMLInputElement>) {
        switch (event.key) {
            case 'ArrowDown':
                event.preventDefault();
                if (!open) {
                    openList();
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
                    pick(active.code);
                }
                return;
            case 'Escape':
                // First press closes the list, a second press clears the text.
                if (open) {
                    event.preventDefault();
                    setOpen(false);
                } else if (shownText !== '') {
                    event.preventDefault();
                    clear();
                }
                return;
        }
    }

    // A 5-digit code that is not in SCB's list. Hidden while the user is still
    // choosing, so it does not flash up mid-typing.
    const isUnknown =
        !open && query.isSuccess && isSniFormat(value) && name === undefined;

    let message = '';
    let isError = false;
    if (error !== undefined) {
        message = error;
        isError = true;
    } else if (isUnknown) {
        message = searchText.sniUnknown;
        isError = true;
    }

    return (
        <div
            className="field"
            onBlur={(event) => {
                // Close only when focus leaves the whole picker, not when it moves inside it.
                if (event.currentTarget.contains(event.relatedTarget)) {
                    return;
                }
                setOpen(false);
                if (name !== undefined) {
                    setText(null);
                }
            }}
        >
            <label htmlFor={inputId}>{searchText.sniLabel}</label>
            <p id={hintId} className="field-hint">
                {searchText.sniHint}
            </p>

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
                    aria-describedby={`${hintId} ${statusId}`}
                    aria-invalid={isError}
                    disabled={!query.isSuccess}
                    value={shownText}
                    onChange={(event) => {
                        const typed = event.target.value;
                        setText(typed);
                        setActiveIndex(0);
                        setOpen(true);
                        // A typed 5-digit code counts as picked. Anything else
                        // stays as typed, so submitting it shows the format error.
                        onChange(sniCodeFromText(typed) ?? typed.trim());
                    }}
                    onMouseDown={(event) => {
                        focusedByMouse.current =
                            document.activeElement !== event.currentTarget;
                    }}
                    onFocus={(event) => event.currentTarget.select()}
                    onMouseUp={(event) => {
                        if (focusedByMouse.current) {
                            focusedByMouse.current = false;
                            event.preventDefault();
                        }
                    }}
                    onClick={openList}
                    onKeyDown={handleKeyDown}
                />

                {open && matches.length > 0 && (
                    <ul
                        id={listId}
                        role="listbox"
                        aria-label={searchText.sniLabel}
                        className="listbox"
                        // Keeps focus in the input when an option is clicked.
                        onMouseDown={(event) => event.preventDefault()}
                    >
                        {matches.map((sni, index) => {
                            const isSelected = sni.code === value;
                            return (
                                <li
                                    key={sni.code}
                                    id={optionId(sni.code)}
                                    role="option"
                                    aria-selected={isSelected}
                                    className={
                                        index === safeIndex
                                            ? 'option option-active'
                                            : 'option'
                                    }
                                    onClick={() => pick(sni.code)}
                                    onMouseMove={() => setActiveIndex(index)}
                                >
                                    <span
                                        className="option-check"
                                        aria-hidden="true"
                                    >
                                        {isSelected ? '✓' : ''}
                                    </span>
                                    {searchText.sniOption(sni.code, sni.name)}
                                </li>
                            );
                        })}
                    </ul>
                )}

                {open && query.isSuccess && matches.length === 0 && (
                    <p className="listbox listbox-empty">
                        {searchText.sniEmpty}
                    </p>
                )}
            </div>

            <p
                id={statusId}
                className={isError ? 'field-error' : 'field-status'}
                aria-live="polite"
            >
                {message}
            </p>

            {query.isPending && (
                <p className="field-status">{searchText.sniLoading}</p>
            )}
            {query.isError && (
                <div role="alert" className="field-error">
                    <p>{errorText(query.error)}</p>
                    <button type="button" onClick={() => void query.refetch()}>
                        {commonText.retry}
                    </button>
                </div>
            )}
        </div>
    );
}
