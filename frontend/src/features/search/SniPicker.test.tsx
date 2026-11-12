import { useState } from 'react';
import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';

import type { CodeItem } from '../../api/codes';
import { commonText } from '../../texts/common';
import { searchText } from '../../texts/search';
import { renderWithQuery } from '../../test/render';
import { SniPicker } from './SniPicker';

const sniCodes = [
    { code: '62100', name: 'Dataprogrammering' },
    { code: '96210', name: 'Hårvård' },
    { code: '96220', name: 'Skönhetsvård' },
] satisfies CodeItem[];

/** Answers the list request with `responses` in order, then keeps the last. */
function stubSniList(...responses: (() => Response)[]): void {
    let index = 0;
    vi.stubGlobal(
        'fetch',
        vi.fn(async (input: RequestInfo | URL) => {
            const url = String(input);
            if (url === '/api/codes/sni') {
                const respond =
                    responses[Math.min(index++, responses.length - 1)];
                return respond();
            }

            throw new Error(`Unexpected fetch: ${url}`);
        }),
    );
}

function Harness({ initial = '' }: { initial?: string }) {
    const [value, setValue] = useState(initial);
    return (
        <>
            <SniPicker value={value} onChange={setValue} error={undefined} />
            <output data-testid="value">{value}</output>
            <button type="button">Nästa fält</button>
        </>
    );
}

function formValue(): string {
    return screen.getByTestId('value').textContent ?? '';
}

async function renderPicker(initial = '') {
    const user = userEvent.setup();
    renderWithQuery(<Harness initial={initial} />);
    const input = await screen.findByRole<HTMLInputElement>('combobox');
    await waitFor(() => {
        expect(input.disabled).toBe(false);
    });
    return { user, input };
}

function optionTexts(): string[] {
    return screen
        .getAllByRole('option')
        .map((option) => option.textContent ?? '');
}

describe('SniPicker', () => {
    afterEach(() => {
        vi.unstubAllGlobals();
    });

    it('filters by part of the name and picks an option with a click', async () => {
        stubSniList(() => Response.json(sniCodes));
        const { user, input } = await renderPicker();

        await user.type(input, 'vård');
        expect(optionTexts()).toEqual(['96210 Hårvård', '96220 Skönhetsvård']);

        await user.click(
            screen.getByRole('option', { name: '96220 Skönhetsvård' }),
        );

        expect(formValue()).toBe('96220');
        expect(input.value).toBe('96220 Skönhetsvård');
        expect(screen.queryByRole('listbox')).toBeNull();
        expect(document.activeElement).toBe(input);
    });

    it('filters by a code with a dot and picks with ArrowDown and Enter', async () => {
        stubSniList(() => Response.json(sniCodes));
        const { user, input } = await renderPicker();

        await user.type(input, '96.21');
        expect(optionTexts()).toEqual(['96210 Hårvård']);

        await user.keyboard('{ArrowDown}{Enter}');

        expect(formValue()).toBe('96210');
        expect(input.value).toBe('96210 Hårvård');
    });

    it('shows the code and name for a code from the page URL', async () => {
        stubSniList(() => Response.json(sniCodes));
        const { input } = await renderPicker('96210');

        expect(input.value).toBe('96210 Hårvård');
        expect(screen.queryByText(searchText.sniUnknown)).toBeNull();
    });

    it('counts a typed code from the list as picked', async () => {
        stubSniList(() => Response.json(sniCodes));
        const { user, input } = await renderPicker();

        await user.type(input, '96210');
        expect(formValue()).toBe('96210');

        await user.tab();
        expect(input.value).toBe('96210 Hårvård');
    });

    it('shows Okänd SNI-kod for a typed code that is not in the list', async () => {
        stubSniList(() => Response.json(sniCodes));
        const { user, input } = await renderPicker();

        await user.type(input, '99999');
        await user.tab();

        expect(formValue()).toBe('99999');
        expect(screen.getByText(searchText.sniUnknown)).toBeTruthy();
        expect(input.getAttribute('aria-invalid')).toBe('true');
    });

    it('shows Okänd SNI-kod for an unknown code from the page URL', async () => {
        stubSniList(() => Response.json(sniCodes));
        const { input } = await renderPicker('99999');

        expect(input.value).toBe('99999');
        expect(screen.getByText(searchText.sniUnknown)).toBeTruthy();
    });

    it('shows the error and loads the list again on retry', async () => {
        stubSniList(
            () =>
                new Response(JSON.stringify({ code: 'scb_unavailable' }), {
                    status: 503,
                    headers: { 'content-type': 'application/problem+json' },
                }),
            () => Response.json(sniCodes),
        );
        const user = userEvent.setup();
        renderWithQuery(<Harness />);

        expect(await screen.findByRole('alert')).toBeTruthy();
        const input = screen.getByRole<HTMLInputElement>('combobox');
        expect(input.disabled).toBe(true);

        await user.click(
            screen.getByRole('button', { name: commonText.retry }),
        );

        await waitFor(() => {
            expect(input.disabled).toBe(false);
        });
        expect(screen.queryByRole('alert')).toBeNull();
    });

    it('works with the keyboard only', async () => {
        stubSniList(() => Response.json(sniCodes));
        const { user, input } = await renderPicker();

        await user.tab();
        expect(document.activeElement).toBe(input);

        await user.keyboard('{ArrowDown}');
        expect(screen.getByRole('listbox')).toBeTruthy();
        expect(input.getAttribute('aria-expanded')).toBe('true');

        await user.keyboard('{ArrowDown}{Enter}');
        expect(formValue()).toBe('96210');
        expect(screen.queryByRole('listbox')).toBeNull();

        await user.keyboard('{ArrowDown}');
        // The list opens on the picked code.
        const active = input.getAttribute('aria-activedescendant');
        expect(active).not.toBeNull();
        expect(document.getElementById(active ?? '')?.textContent).toContain(
            '96210 Hårvård',
        );

        await user.keyboard('{Escape}');
        expect(screen.queryByRole('listbox')).toBeNull();
        expect(input.value).toBe('96210 Hårvård');
        expect(formValue()).toBe('96210');

        await user.keyboard('{Escape}');
        expect(input.value).toBe('');
        expect(formValue()).toBe('');
    });

    it('keeps typed text on the first Escape and clears it on the second', async () => {
        stubSniList(() => Response.json(sniCodes));
        const { user, input } = await renderPicker();

        await user.type(input, 'hår');
        expect(screen.getByRole('listbox')).toBeTruthy();

        await user.keyboard('{Escape}');
        expect(screen.queryByRole('listbox')).toBeNull();
        expect(input.value).toBe('hår');

        await user.keyboard('{Escape}');
        expect(input.value).toBe('');
    });
});
