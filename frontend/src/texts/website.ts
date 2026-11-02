export const websiteText = {
    column: 'Webbplats',
    find: 'Hitta webbplats',
    dialogTitle: 'Webbplatskandidater',
    close: 'Stäng',
    finding: 'Söker efter webbplats…',
    quotaNote: 'Varje sökning använder en del av månadens sökgräns.',
    quotaRemaining: (count: number) =>
        `Sökningar kvar den här månaden: ${count}`,
    empty: 'Ingen trolig webbplats hittades',
    unknownDomain: 'Okänd adress',
    candidatesLabel: 'Förslag',
    bestMatch: 'Bästa träff',
    confidence: {
        high: 'Trolig',
        medium: 'Möjlig',
        low: 'Osäker',
    },
    open: 'Öppna',
    analyze: 'Analysera',
    pasteLabel: 'Klistra in adress',
    pasteActions: 'Åtgärder för inklistrad adress',
} as const;
