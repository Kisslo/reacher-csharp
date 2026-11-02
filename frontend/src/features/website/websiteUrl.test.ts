import { describe, expect, it } from 'vitest';

import { pastedWebsiteUrl, websiteDomain } from './websiteUrl';

describe('pastedWebsiteUrl', () => {
    it('accepts an http or https address and trims it', () => {
        expect(pastedWebsiteUrl('  https://example.test/salon  ')).toBe(
            'https://example.test/salon',
        );
        expect(pastedWebsiteUrl('http://example.test')).toBe(
            'http://example.test',
        );
    });

    it('rejects an empty value, another scheme, and a hostless address', () => {
        expect(pastedWebsiteUrl('   ')).toBeUndefined();
        expect(pastedWebsiteUrl('example.test')).toBeUndefined();
        expect(pastedWebsiteUrl('ftp://files.test/a')).toBeUndefined();
        expect(pastedWebsiteUrl('javascript:alert(1)')).toBeUndefined();
    });
});

describe('websiteDomain', () => {
    it('returns the host', () => {
        expect(websiteDomain('https://example.test/company')).toBe(
            'example.test',
        );
    });

    it('returns nothing for a value that is not a URL', () => {
        expect(websiteDomain('not a url')).toBe('');
    });
});
