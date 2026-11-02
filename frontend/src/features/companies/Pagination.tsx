import { companyText } from '../../texts/companies';

type PaginationProps = {
    page: number;
    lastPage: number;
    onChange: (page: number) => void;
};

export function Pagination({ page, lastPage, onChange }: PaginationProps) {
    return (
        <nav className="pagination" aria-label={companyText.paginationLabel}>
            <button
                type="button"
                disabled={page <= 1}
                // A bookmarked page can be past the end if the list has shrunk.
                onClick={() => onChange(Math.min(page - 1, lastPage))}
            >
                {companyText.previousPage}
            </button>
            <span>{companyText.pageStatus(page, lastPage)}</span>
            <button
                type="button"
                disabled={page >= lastPage}
                onClick={() => onChange(page + 1)}
            >
                {companyText.nextPage}
            </button>
        </nav>
    );
}
