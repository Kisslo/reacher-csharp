import { ApiError } from '../../api/client';
import type { CodeItem } from '../../api/codes';
import { getErrorMessage } from '../../texts/errors';
import { searchText } from '../../texts/search';

/** Same limit as SearchRequestValidator.MaxMunicipalities in the backend. */
export const maxMunicipalities = 20;

const sniPattern = /^[0-9]{5}$/;

export type SearchErrors = {
    sni?: string;
    municipalities?: string;
};

export function isSniFormat(value: string): boolean {
    return sniPattern.test(value);
}

export function validateSearch(
    sni: string,
    municipalities: string[],
): SearchErrors {
    const errors: SearchErrors = {};

    if (sni === '') {
        errors.sni = searchText.sniRequired;
    } else if (!isSniFormat(sni)) {
        errors.sni = searchText.sniFormat;
    }

    if (municipalities.length === 0) {
        errors.municipalities = searchText.municipalitiesRequired;
    } else if (municipalities.length > maxMunicipalities) {
        errors.municipalities = searchText.municipalitiesMax;
    }

    return errors;
}

/** Matches the start of the code or any part of the name, ignoring case. */
export function filterMunicipalities(
    all: CodeItem[],
    filter: string,
): CodeItem[] {
    const needle = toNeedle(filter);
    if (needle === '') {
        return all;
    }

    return all.filter(
        (municipality) =>
            municipality.code.startsWith(needle) ||
            nameContains(municipality, needle),
    );
}

/**
 * Matches the start of the code or any part of the name, ignoring case.
 * Dots and spaces in a typed code are ignored, so `96.21` matches `96210`.
 */
export function filterSni(all: CodeItem[], filter: string): CodeItem[] {
    const needle = toNeedle(filter);
    if (needle === '') {
        return all;
    }

    const digits = stripCodeSeparators(needle);
    const isCodePrefix = /^[0-9]+$/.test(digits);

    return all.filter(
        (sni) =>
            (isCodePrefix && sni.code.startsWith(digits)) ||
            nameContains(sni, needle),
    );
}

/** The 5-digit code in typed text such as `96210` or `96.210`, or null. */
export function sniCodeFromText(text: string): string | null {
    const digits = stripCodeSeparators(text.trim());
    return isSniFormat(digits) ? digits : null;
}

function toNeedle(filter: string): string {
    return filter.trim().toLocaleLowerCase('sv-SE');
}

function stripCodeSeparators(text: string): string {
    return text.replace(/[.\s]/g, '');
}

function nameContains(item: CodeItem, needle: string): boolean {
    return item.name.toLocaleLowerCase('sv-SE').includes(needle);
}

/** ApiError already carries Swedish text. Anything else (network down) gets the generic text. */
export function errorText(error: Error): string {
    return error instanceof ApiError
        ? error.message
        : getErrorMessage(undefined);
}
