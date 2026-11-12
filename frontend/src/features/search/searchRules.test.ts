import { describe, expect, it } from 'vitest';

import type { CodeItem } from '../../api/codes';
import { searchText } from '../../texts/search';
import {
    filterMunicipalities,
    filterSni,
    isSniFormat,
    maxMunicipalities,
    sniCodeFromText,
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

describe('filterSni', () => {
    const sniCodes = [
        { code: '62100', name: 'Dataprogrammering' },
        { code: '96210', name: 'Hårvård' },
        { code: '96220', name: 'Skönhetsvård' },
    ] satisfies CodeItem[];

    function codesFor(filter: string): string[] {
        return filterSni(sniCodes, filter).map((item) => item.code);
    }

    it('returns every code when the filter is blank', () => {
        expect(filterSni(sniCodes, '  ')).toEqual(sniCodes);
    });

    it.each(['9621', '96.21', '96.210', ' 96210 '])(
        'matches %j as a code prefix',
        (filter) => {
            expect(codesFor(filter)).toEqual(['96210']);
        },
    );

    it('matches only the start of the code', () => {
        expect(codesFor('621')).toEqual(['62100']);
    });

    it('matches part of the name with Swedish letters, ignoring case', () => {
        expect(codesFor('hår')).toEqual(['96210']);
        expect(codesFor('HÅR')).toEqual(['96210']);
        expect(codesFor('vård')).toEqual(['96210', '96220']);
    });
});

describe('sniCodeFromText', () => {
    it.each([
        ['96210', '96210'],
        ['96.210', '96210'],
        [' 96210 ', '96210'],
        ['9621', null],
        ['962100', null],
        ['hårvård', null],
        ['96210 Hårvård', null],
    ])('reads %j as %j', (text, expected) => {
        expect(sniCodeFromText(text)).toBe(expected);
    });
});
