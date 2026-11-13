import { describe, expect, it } from 'vitest';

import { getErrorMessage } from './errors';

describe('error messages', () => {
    it('translates the codes that used to fall through', () => {
        expect(getErrorMessage('analysis_fetch_failed')).toBe(
            'Sidan kunde inte hämtas. Försök igen.',
        );
        expect(getErrorMessage('unauthenticated')).toBe(
            'Lösenord krävs för att fortsätta.',
        );
        expect(getErrorMessage('internal_error')).toBe(
            'Ett oväntat fel inträffade. Försök igen.',
        );
    });
});
