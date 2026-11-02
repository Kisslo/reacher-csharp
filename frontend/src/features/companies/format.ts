const amountFormat = new Intl.NumberFormat('sv-SE', {
    style: 'currency',
    currency: 'SEK',
    maximumFractionDigits: 0,
});

const countFormat = new Intl.NumberFormat('sv-SE');

/**
 * .NET's OpenAPI types numbers as number | string, so convert before use.
 * null means "unknown". Number(null) would turn it into 0, which is a real amount.
 */
export function toAmount(value: number | string | null): number | null {
    return value === null ? null : Number(value);
}

export function formatAmount(amount: number): string {
    return amountFormat.format(amount);
}

export function formatCount(count: number): string {
    return countFormat.format(count);
}
