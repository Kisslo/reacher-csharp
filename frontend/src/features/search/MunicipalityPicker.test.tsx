import { useState } from 'react';
import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import type { CodeItem } from '../../api/codes';
import { searchText } from '../../texts/search';
import { renderWithQuery } from '../../test/render';
import { MunicipalityPicker } from './MunicipalityPicker';
import { maxMunicipalities } from './searchRules';

const municipalities = [
    { code: '0180', name: 'Stockholm' },
    { code: '1280', name: 'Malmö' },
    { code: '1480', name: 'Göteborg' },
] satisfies CodeItem[];

function stubMunicipalities(items: CodeItem[]): void {
    vi.stubGlobal(
        'fetch',
        vi.fn(async (input: RequestInfo | URL) => {
            const url = String(input);
            if (url === '/api/codes/municipalities') {
                return Response.json(items);
            }

            throw new Error(`Unexpected fetch: ${url}`);
        }),
    );
}

function Harness({ initial = [] }: { initial?: string[] }) {
    const [selected, setSelected] = useState(initial);
    return (
        <MunicipalityPicker
            selected={selected}
            onChange={setSelected}
            error={undefined}
        />
    );
}

async function renderPicker(initial: string[] = []) {
    const user = userEvent.setup();
    renderWithQuery(<Harness initial={initial} />);
    const input = await screen.findByRole('combobox');
    await waitFor(() => {
        expect((input as HTMLInputElement).disabled).toBe(false);
    });
    // Focus without clicking, so ArrowDown is what opens the list.
    input.focus();
    return { user, input };
}

describe('MunicipalityPicker', () => {
    beforeEach(() => {
        stubMunicipalities(municipalities);
    });

    afterEach(() => {
        vi.unstubAllGlobals();
    });

    it('selects the first option with ArrowDown and Enter', async () => {
        const { user } = await renderPicker();

        await user.keyboard('{ArrowDown}{Enter}');

        const selected = screen.getByRole('list', {
            name: searchText.selectedMunicipalities,
        });
        expect(selected.textContent).toContain('Stockholm');
    });

    it('selects the second option after a second ArrowDown', async () => {
        const { user } = await renderPicker();

        await user.keyboard('{ArrowDown}{ArrowDown}{Enter}');

        const selected = screen.getByRole('list', {
            name: searchText.selectedMunicipalities,
        });
        expect(selected.textContent).toContain('Malmö');
        expect(selected.textContent).not.toContain('Stockholm');
    });

    it('closes the list with Escape', async () => {
        const { user } = await renderPicker();

        await user.keyboard('{ArrowDown}');
        expect(screen.getByRole('listbox')).toBeTruthy();

        await user.keyboard('{Escape}');
        expect(screen.queryByRole('listbox')).toBeNull();
    });

    it('clears the text and keeps the list open after a pick by click', async () => {
        const { user, input } = await renderPicker();

        await user.type(input, 'Malm');
        await user.click(screen.getByRole('option', { name: 'Malmö' }));

        expect((input as HTMLInputElement).value).toBe('');
        expect(document.activeElement).toBe(input);
        expect(screen.getAllByRole('option')).toHaveLength(
            municipalities.length,
        );
        const selected = screen.getByRole('list', {
            name: searchText.selectedMunicipalities,
        });
        expect(selected.textContent).toContain('Malmö');
    });

    it('clears the text and keeps the list open after a pick with Enter', async () => {
        const { user, input } = await renderPicker();

        await user.type(input, 'Göte');
        await user.keyboard('{Enter}');

        expect((input as HTMLInputElement).value).toBe('');
        expect(screen.getByRole('listbox')).toBeTruthy();
        const selected = screen.getByRole('list', {
            name: searchText.selectedMunicipalities,
        });
        expect(selected.textContent).toContain('Göteborg');
    });

    it('clears the text after removing a municipality from the list', async () => {
        const { user, input } = await renderPicker(['0180', '1280']);

        await user.type(input, 'Stock');
        await user.keyboard('{Enter}');

        expect((input as HTMLInputElement).value).toBe('');
        const selected = screen.getByRole('list', {
            name: searchText.selectedMunicipalities,
        });
        expect(selected.textContent).not.toContain('Stockholm');
        expect(selected.textContent).toContain('Malmö');
    });

    it('closes the list on the first Escape and clears the text on the second', async () => {
        const { user, input } = await renderPicker(['0180']);

        await user.type(input, 'Malm');
        expect(screen.getByRole('listbox')).toBeTruthy();

        await user.keyboard('{Escape}');
        expect(screen.queryByRole('listbox')).toBeNull();
        expect((input as HTMLInputElement).value).toBe('Malm');

        await user.keyboard('{Escape}');
        expect((input as HTMLInputElement).value).toBe('');
        expect(screen.queryByRole('listbox')).toBeNull();

        // A third Escape on an empty box does nothing.
        await user.keyboard('{Escape}');
        const selected = screen.getByRole('list', {
            name: searchText.selectedMunicipalities,
        });
        expect(selected.textContent).toContain('Stockholm');
    });

    it('removes the last chip with Backspace when the filter is empty', async () => {
        const { user } = await renderPicker(['0180', '1280']);

        await user.keyboard('{Backspace}');

        const selected = screen.getByRole('list', {
            name: searchText.selectedMunicipalities,
        });
        expect(selected.textContent).toContain('Stockholm');
        expect(selected.textContent).not.toContain('Malmö');
    });

    it('does not add a twenty-first municipality', async () => {
        const many = Array.from(
            { length: maxMunicipalities + 1 },
            (_, index) => ({
                code: String(index + 1).padStart(4, '0'),
                name: `Kommun ${index + 1}`,
            }),
        ) satisfies CodeItem[];
        vi.unstubAllGlobals();
        stubMunicipalities(many);

        const { user, input } = await renderPicker(
            many.slice(0, maxMunicipalities).map((item) => item.code),
        );

        await user.type(input, 'Kommun 21');
        await user.keyboard('{ArrowDown}{Enter}');

        // Nothing was added, so the typed text stays.
        expect((input as HTMLInputElement).value).toBe('Kommun 21');
        const selected = screen.getByRole('list', {
            name: searchText.selectedMunicipalities,
        });
        expect(selected.textContent).not.toContain('Kommun 21');
        expect(
            screen.getByText(searchText.selectedCount(20, maxMunicipalities), {
                exact: false,
            }).textContent,
        ).toContain(searchText.municipalitiesMax);
    });
});
