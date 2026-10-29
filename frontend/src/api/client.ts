import { getErrorMessage } from '../texts/errors';

type ProblemDetails = {
    code?: string;
};

export class ApiError extends Error {
    readonly code: string | undefined;
    readonly status: number;

    constructor(status: number, code: string | undefined, message: string) {
        super(message);
        this.name = 'ApiError';
        this.status = status;
        this.code = code;
    }
}

async function readProblem(response: Response): Promise<ApiError> {
    const contentType = response.headers.get('content-type') ?? '';
    let code: string | undefined;

    if (contentType.toLowerCase().includes('application/problem+json')) {
        const problem = (await response.json()) as ProblemDetails;
        code = problem.code;
    }

    return new ApiError(response.status, code, getErrorMessage(code));
}

export async function apiFetch<T>(
    input: RequestInfo | URL,
    init?: RequestInit,
): Promise<T> {
    const response = await fetch(input, init);

    if (!response.ok) {
        throw await readProblem(response);
    }

    if (response.status === 204) {
        return undefined as T;
    }

    const contentType = response.headers.get('content-type') ?? '';
    if (contentType.toLowerCase().includes('json')) {
        return (await response.json()) as T;
    }

    return (await response.text()) as T;
}
