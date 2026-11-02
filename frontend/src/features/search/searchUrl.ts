export type SearchSelection = {
    sni: string;
    municipalities: string[];
};

export function readSearchFromUrl(): SearchSelection {
    const params = new URLSearchParams(window.location.search);
    const municipalities = params
        .getAll('municipality')
        .map((code) => code.trim())
        .filter((code) => code !== '');

    return {
        sni: (params.get('sni') ?? '').trim(),
        municipalities: [...new Set(municipalities)],
    };
}

/**
 * Keeps other parameters, so the company filters stay in the same URL.
 * A different search drops `page`, because the old page number means nothing in the new list.
 * The same search keeps it, because a reloaded page writes its own search back.
 */
export function writeSearchToUrl(search: SearchSelection): void {
    const isSameSearch = searchKey(readSearchFromUrl()) === searchKey(search);

    const params = new URLSearchParams(window.location.search);
    params.set('sni', search.sni);
    params.delete('municipality');
    for (const municipality of search.municipalities) {
        params.append('municipality', municipality);
    }
    if (!isSameSearch) {
        params.delete('page');
    }

    window.history.replaceState(
        null,
        '',
        `${window.location.pathname}?${params.toString()}`,
    );
}

/** Identifies a search, for example as a React key. */
export function searchKey(search: SearchSelection): string {
    return `${search.sni}|${search.municipalities.join(',')}`;
}
