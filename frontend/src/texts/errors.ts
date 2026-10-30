const errorMessages: Record<string, string> = {
    analysis_timeout: 'Analysen tog för lång tid. Försök igen.',
    brave_quota_exceeded: 'Månadens gräns för webbplatssökningar är nådd.',
    invalid_search:
        'Sökningen är inte giltig. Kontrollera SNI-kod och kommuner.',
    invalid_url: 'Webbadressen är inte giltig.',
    job_not_found: 'Sökjobbet hittades inte.',
    scb_unavailable: 'SCB kan inte nås just nu. Försök igen senare.',
    sni_not_found: 'SNI-koden kunde inte hittas.',
};

export function getErrorMessage(code: string | undefined): string {
    if (code === undefined) {
        return 'Ett oväntat fel inträffade. Försök igen.';
    }

    return errorMessages[code] ?? 'Ett oväntat fel inträffade. Försök igen.';
}
