import { afterEach, describe, expect, it } from 'vitest';

import { readSearchFromUrl, writeSearchToUrl } from './searchUrl';

afterEach(() => {
    window.history.replaceState(null, '', '/');
});

function setSearch(search: string): void {
    window.history.replaceState(null, '', `/${search}`);
}

describe('readSearchFromUrl', () => {
    it('reads sni and municipalities, dropping blanks and duplicates', () => {
        setSearch(
            '?sni= 96210 &municipality=0180&municipality=0180&municipality=',
        );

        expect(readSearchFromUrl()).toEqual({
            sni: '96210',
            municipalities: ['0180'],
        });
    });
});

describe('writeSearchToUrl', () => {
    it('writes sni and municipalities and keeps other parameters', () => {
        setSearch('?page=2&municipality=9999');

        writeSearchToUrl({
            sni: '96210',
            municipalities: ['0180', '1480'],
        });

        const params = new URLSearchParams(window.location.search);
        expect(params.get('sni')).toBe('96210');
        expect(params.getAll('municipality')).toEqual(['0180', '1480']);
        expect(params.get('page')).toBe('2');
    });
});
