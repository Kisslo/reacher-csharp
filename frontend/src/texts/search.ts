export const searchText = {
    heading: 'Sök företag',
    sniLabel: 'SNI-kod',
    sniHint: 'Fem siffror, till exempel 96210.',
    sniRequired: 'Ange en SNI-kod.',
    sniFormat: 'SNI-koden måste bestå av fem siffror.',
    sniUnknown: 'Okänd SNI-kod',
    sniChecking: 'Kontrollerar SNI-koden...',
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
} as const;
