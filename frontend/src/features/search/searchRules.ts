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
    const needle = filter.trim().toLocaleLowerCase('sv-SE');
    if (needle === '') {
        return all;
    }

    return all.filter(
        (municipality) =>
            municipality.code.startsWith(needle) ||
            municipality.name.toLocaleLowerCase('sv-SE').includes(needle),
    );
}

/** ApiError already carries Swedish text. Anything else (network down) gets the generic text. */
export function errorText(error: Error): string {
    return error instanceof ApiError
        ? error.message
        : getErrorMessage(undefined);
}
