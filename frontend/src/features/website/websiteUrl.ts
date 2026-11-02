/**
 * A pasted address that is worth offering as a link.
 * Only the shape is checked here. The backend rejects private and unsafe hosts.
 */
export function pastedWebsiteUrl(value: string): string | undefined {
    const trimmed = value.trim();
    if (trimmed === '') {
        return undefined;
    }

    let url: URL;
    try {
        url = new URL(trimmed);
    } catch {
        return undefined;
    }

    if (url.protocol !== 'http:' && url.protocol !== 'https:') {
        return undefined;
    }

    if (url.hostname === '') {
        return undefined;
    }

    return trimmed;
}

/** The host to show. Anything that is not a URL becomes empty, never the raw text. */
export function websiteDomain(url: string): string {
    try {
        return new URL(url).hostname;
    } catch {
        return '';
    }
}
