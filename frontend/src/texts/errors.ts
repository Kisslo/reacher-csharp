const errorMessages: Record<string, string> = {
    analysis_timeout: 'Analysen tog för lång tid. Försök igen.',
    brave_quota_exceeded: 'Sökgränsen för månaden är nådd.',
    brave_unavailable:
        'Webbplatssökningen kan inte nås just nu. Försök igen senare.',
    company_not_found: 'Företaget kunde inte hittas.',
    invalid_search:
        'Sökningen är inte giltig. Kontrollera SNI-kod och kommuner.',
    invalid_search_query:
        'Filtren är inte giltiga. Kontrollera värdena och försök igen.',
    invalid_sni: 'SNI-koden måste bestå av fem siffror.',
    invalid_url: 'Webbadressen är inte giltig.',
    job_not_cancellable: 'Sökningen är redan avslutad och kan inte avbrytas.',
    job_not_found: 'Sökjobbet hittades inte.',
    note_rejected:
        'Anteckningen sparades inte. Ta bort webbadresser och personnummer, och håll den under 500 tecken.',
    scb_unavailable: 'SCB kan inte nås just nu. Försök igen senare.',
    sni_not_found: 'SNI-koden kunde inte hittas.',
};

export function getErrorMessage(code: string | undefined): string {
    if (code === undefined) {
        return 'Ett oväntat fel inträffade. Försök igen.';
    }

    return errorMessages[code] ?? 'Ett oväntat fel inträffade. Försök igen.';
}
