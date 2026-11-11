import { analysisText } from './analysisText';

/** The company table sorts by one of these at a time, in memory only. */
export const scoreColumns = [
    'rework',
    'performance',
    'accessibility',
    'bestPractices',
    'seo',
] as const;

export type ScoreColumn = (typeof scoreColumns)[number];

export type LighthouseSessionScores = {
    performance: number;
    accessibility: number;
    bestPractices: number;
    seo: number;
};

/**
 * Kept for the browser session. Never includes the website URL (D9).
 * `scored` is a finished analysis. `noWebsite` is the user saying the company
 * has no website, so it counts as analysed but has no scores.
 */
export type SessionScore =
    | {
          kind: 'scored';
          reworkScore: number;
          analysedAt: string;
          lighthouse: LighthouseSessionScores | null;
      }
    | {
          kind: 'noWebsite';
          analysedAt: string;
      };

export type ScoreSort = {
    column: ScoreColumn;
    order: 'asc' | 'desc';
};

export const scoreColumnLabel: Record<ScoreColumn, string> = {
    rework: analysisText.sortColumn,
    performance: analysisText.performance,
    accessibility: analysisText.accessibility,
    bestPractices: analysisText.bestPractices,
    seo: analysisText.seo,
};

/** OpenAPI types the integers as number | string, so normalise before storing. */
export function readLighthouse(
    lighthouse: {
        performance: number | string;
        accessibility: number | string;
        bestPractices: number | string;
        seo: number | string;
    } | null,
): LighthouseSessionScores | null {
    if (lighthouse === null) {
        return null;
    }

    return {
        performance: Number(lighthouse.performance),
        accessibility: Number(lighthouse.accessibility),
        bestPractices: Number(lighthouse.bestPractices),
        seo: Number(lighthouse.seo),
    };
}

export function scoreValue(
    score: SessionScore | undefined,
    column: ScoreColumn,
): number | undefined {
    if (score === undefined || score.kind === 'noWebsite') {
        return undefined;
    }
    if (column === 'rework') {
        return score.reworkScore;
    }
    if (score.lighthouse === null) {
        return undefined;
    }

    return score.lighthouse[column];
}

/**
 * The same column flips direction. A different column, or the first click,
 * starts ascending.
 */
export function nextScoreSort(
    current: ScoreSort | null,
    column: ScoreColumn,
): ScoreSort {
    if (current !== null && current.column === column) {
        return {
            column,
            order: current.order === 'asc' ? 'desc' : 'asc',
        };
    }

    return { column, order: 'asc' };
}

/** A company with no value for the column stays last in both directions. */
export function compareScoreValues(
    left: number | undefined,
    right: number | undefined,
    order: 'asc' | 'desc',
): number {
    if (left === undefined && right === undefined) {
        return 0;
    }
    if (left === undefined) {
        return 1;
    }
    if (right === undefined) {
        return -1;
    }

    return order === 'asc' ? left - right : right - left;
}
