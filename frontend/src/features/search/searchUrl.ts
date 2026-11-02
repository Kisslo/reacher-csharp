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

/** Keeps other parameters, so T1-10 can add its filters to the same URL. */
export function writeSearchToUrl(search: SearchSelection): void {
    const params = new URLSearchParams(window.location.search);
    params.set('sni', search.sni);
    params.delete('municipality');
    for (const municipality of search.municipalities) {
        params.append('municipality', municipality);
    }

    window.history.replaceState(
        null,
        '',
        `${window.location.pathname}?${params.toString()}`,
    );
}
