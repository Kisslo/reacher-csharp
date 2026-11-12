export const searchText = {
    heading: 'Sök företag',
    sniLabel: 'SNI-kod',
    sniHint:
        'Skriv en kod, till exempel 96210, eller en del av branschnamnet och välj i listan.',
    sniRequired: 'Ange en SNI-kod.',
    sniFormat: 'SNI-koden måste bestå av fem siffror.',
    sniUnknown: 'Okänd SNI-kod',
    sniLoading: 'Hämtar SNI-koder...',
    sniEmpty: 'Inga SNI-koder matchar sökningen.',
    sniOption: (code: string, name: string) => `${code} ${name}`,
    municipalitiesLabel: 'Kommuner',
    municipalitiesHint:
        'Skriv för att söka och välj med Enter eller ett klick. Högst 20 kommuner.',
    municipalitiesLoading: 'Hämtar kommuner...',
    municipalitiesEmpty: 'Inga kommuner matchar sökningen.',
    municipalitiesRequired: 'Välj minst en kommun.',
    municipalitiesMax: 'Du kan välja högst 20 kommuner.',
    selectedMunicipalities: 'Valda kommuner',
    selectedCount: (count: number, max: number) => `${count} av ${max} valda`,
    removeMunicipality: (name: string) => `Ta bort ${name}`,
    submit: 'Sök företag',
    submitting: 'Startar sökningen...',
    refresh: 'Uppdatera data',
    lastFetched: (date: string) => `Uppgifterna hämtades senast ${date}.`,
    lastFetchedUnknown: 'Det är okänt när uppgifterna hämtades senast.',
    progressLabel: 'Hämtningens förlopp',
    progressQueued: 'Sökningen väntar på att starta…',
    progressWorkplaces: 'Hämtar arbetsställen…',
    progressCompanies: (done: number, total: number | null) =>
        total === null
            ? 'Hämtar företagsuppgifter…'
            : `Hämtar företagsuppgifter (${done} av ${total})…`,
    progressFinancials: (done: number, total: number | null) =>
        total === null
            ? 'Hämtar årsredovisningar…'
            : `Hämtar årsredovisningar (${done} av ${total})…`,
    progressFailed: 'Hämtningen misslyckades',
    progressCancelled: 'Sökningen avbröts',
    cancel: 'Avbryt sökning',
    resultsIncomplete:
        'Sökningen pågår, så listan är inte komplett. Nya företag visas medan de hämtas, och uppgifterna räknas som aktuella först när sökningen är klar.',
    resultsPartial:
        'Sökningen blev inte klar, så listan kan sakna företag och uppgifterna är inte aktuella.',
} as const;
