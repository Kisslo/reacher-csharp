import { describe, expect, it } from 'vitest';

import type { CodeItem } from '../../api/codes';
import { searchText } from '../../texts/search';
import {
    filterMunicipalities,
    isSniFormat,
    maxMunicipalities,
    validateSearch,
} from './searchRules';

const municipalities = [
    { code: '0180', name: 'Stockholm' },
    { code: '1280', name: 'Malmö' },
    { code: '1480', name: 'Göteborg' },
] satisfies CodeItem[];

function codes(count: number): string[] {
    return Array.from({ length: count }, (_, index) => String(index + 1));
}

describe('SNI format', () => {
    it('accepts five digits', () => {
        expect(isSniFormat('96210')).toBe(true);
        expect(validateSearch('96210', ['0180']).sni).toBeUndefined();
    });

    it('requires a code', () => {
        expect(isSniFormat('')).toBe(false);
        expect(validateSearch('', ['0180']).sni).toBe(searchText.sniRequired);
    });

    it.each(['9621', '962100', '9621a'])('rejects %j', (sni) => {
        expect(isSniFormat(sni)).toBe(false);
        expect(validateSearch(sni, ['0180']).sni).toBe(searchText.sniFormat);
    });
});

describe('municipality count', () => {
    it('requires one municipality', () => {
        expect(validateSearch('96210', []).municipalities).toBe(
            searchText.municipalitiesRequired,
        );
    });

    it('accepts one and twenty municipalities', () => {
        expect(
            validateSearch('96210', codes(1)).municipalities,
        ).toBeUndefined();
        expect(
            validateSearch('96210', codes(maxMunicipalities)).municipalities,
        ).toBeUndefined();
    });

    it('rejects twenty-one municipalities', () => {
        expect(
            validateSearch('96210', codes(maxMunicipalities + 1))
                .municipalities,
        ).toBe(searchText.municipalitiesMax);
    });
});

describe('filterMunicipalities', () => {
    it('returns every municipality when the filter is blank', () => {
        expect(filterMunicipalities(municipalities, '   ')).toEqual(
            municipalities,
        );
    });

    it('matches a code prefix', () => {
        expect(
            filterMunicipalities(municipalities, '018').map(
                (item) => item.code,
            ),
        ).toEqual(['0180']);
    });

    it('matches Swedish letters without caring about case', () => {
        expect(
            filterMunicipalities(municipalities, 'göteborg').map(
                (item) => item.code,
            ),
        ).toEqual(['1480']);
        expect(
            filterMunicipalities(municipalities, 'GÖTEBORG').map(
                (item) => item.code,
            ),
        ).toEqual(['1480']);
        expect(
            filterMunicipalities(municipalities, 'malmö').map(
                (item) => item.code,
            ),
        ).toEqual(['1280']);
    });
});
