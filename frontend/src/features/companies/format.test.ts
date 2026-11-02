import { describe, expect, it } from 'vitest';

import { formatAmount, formatCount, toAmount } from './format';

/** sv-SE uses a no-break space and a real minus sign. This makes tests readable. */
function plain(text: string): string {
    return text.replace(/\s/g, ' ').replace('−', '-');
}

describe('toAmount', () => {
    it('keeps null, because no figure is not the same as 0', () => {
        expect(toAmount(null)).toBeNull();
    });

    it('converts numbers and numeric text', () => {
        expect(toAmount(1500)).toBe(1500);
        expect(toAmount('-1500')).toBe(-1500);
        expect(toAmount(0)).toBe(0);
    });
});

describe('formatAmount', () => {
    it('groups thousands and adds kr', () => {
        expect(plain(formatAmount(1234000))).toBe('1 234 000 kr');
    });

    it('shows a negative amount with a minus sign', () => {
        expect(plain(formatAmount(-45000))).toBe('-45 000 kr');
    });

    it('shows whole kronor', () => {
        expect(plain(formatAmount(1000.6))).toBe('1 001 kr');
    });
});

describe('formatCount', () => {
    it('groups thousands', () => {
        expect(plain(formatCount(3625))).toBe('3 625');
    });
});
