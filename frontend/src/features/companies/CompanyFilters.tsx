import { useId, useState } from 'react';
import type { FormEvent } from 'react';
import { useQuery } from '@tanstack/react-query';
import type { UseQueryResult } from '@tanstack/react-query';

import type { CodeItem } from '../../api/codes';
import { commonText } from '../../texts/common';
import { companyText } from '../../texts/companies';
import {
    employeeClassesQuery,
    revenueClassesQuery,
} from '../search/codeQueries';
import { errorText } from '../search/searchRules';
import { emptyFilters, validateFilters } from './companyView';
import type { CompanyFilters as Filters, FilterErrors } from './companyView';

type CompanyFiltersProps = {
    filters: Filters;
    onApply: (filters: Filters) => void;
};

/**
 * The form edits a draft and only applies it on submit. Typing 100000 into a
 * field that applied at once would send six searches.
 */
export function CompanyFilters({ filters, onApply }: CompanyFiltersProps) {
    const [draft, setDraft] = useState(filters);
    const [errors, setErrors] = useState<FilterErrors>({});
    const employeeClasses = useQuery(employeeClassesQuery());
    const revenueClasses = useQuery(revenueClassesQuery());

    function update(change: Partial<Filters>) {
        setDraft((current) => ({ ...current, ...change }));
    }

    function handleSubmit(event: FormEvent<HTMLFormElement>) {
        event.preventDefault();
        const found = validateFilters(draft);
        setErrors(found);
        if (Object.keys(found).length === 0) {
            onApply(draft);
        }
    }

    function handleClear() {
        setDraft(emptyFilters);
        setErrors({});
        onApply(emptyFilters);
    }

    return (
        <form
            className="company-filters"
            aria-label={companyText.filtersLabel}
            noValidate
            onSubmit={handleSubmit}
        >
            <div className="filter-classes">
                <ClassFilter
                    legend={companyText.filterEmployees}
                    query={employeeClasses}
                    selected={draft.employeeClasses}
                    onChange={(codes) => update({ employeeClasses: codes })}
                />
                <ClassFilter
                    legend={companyText.filterRevenueClass}
                    query={revenueClasses}
                    selected={draft.revenueClasses}
                    onChange={(codes) => update({ revenueClasses: codes })}
                />
            </div>

            <div className="filter-fields">
                <FilterField
                    label={companyText.filterRegisteredFrom}
                    type="date"
                    value={draft.registeredFrom}
                    onChange={(value) => update({ registeredFrom: value })}
                    error={errors.registered}
                />
                <FilterField
                    label={companyText.filterRegisteredTo}
                    type="date"
                    value={draft.registeredTo}
                    onChange={(value) => update({ registeredTo: value })}
                />
                <FilterField
                    label={companyText.filterRevenueMin}
                    type="number"
                    value={draft.revenueMin}
                    onChange={(value) => update({ revenueMin: value })}
                    error={errors.revenue}
                />
                <FilterField
                    label={companyText.filterRevenueMax}
                    type="number"
                    value={draft.revenueMax}
                    onChange={(value) => update({ revenueMax: value })}
                />
                <FilterField
                    label={companyText.filterNetResultMin}
                    type="number"
                    value={draft.netResultMin}
                    onChange={(value) => update({ netResultMin: value })}
                    error={errors.netResult}
                />
                <FilterField
                    label={companyText.filterNetResultMax}
                    type="number"
                    value={draft.netResultMax}
                    onChange={(value) => update({ netResultMax: value })}
                />
            </div>

            <div className="filter-actions">
                <label className="filter-check">
                    <input
                        type="checkbox"
                        checked={draft.hasFinancials}
                        onChange={(event) =>
                            update({ hasFinancials: event.target.checked })
                        }
                    />
                    {companyText.filterHasFinancials}
                </label>
                <button type="submit">{companyText.filterApply}</button>
                <button type="button" onClick={handleClear}>
                    {companyText.filterClear}
                </button>
            </div>
        </form>
    );
}

type ClassFilterProps = {
    legend: string;
    query: UseQueryResult<CodeItem[]>;
    selected: string[];
    onChange: (codes: string[]) => void;
};

function ClassFilter({ legend, query, selected, onChange }: ClassFilterProps) {
    return (
        <fieldset className="class-filter">
            <legend>{legend}</legend>
            {query.isPending && (
                <p className="field-status">
                    {companyText.filterClassesLoading}
                </p>
            )}
            {query.isError && (
                <div role="alert" className="field-error">
                    <p>{errorText(query.error)}</p>
                    <button type="button" onClick={() => void query.refetch()}>
                        {commonText.retry}
                    </button>
                </div>
            )}
            {query.data?.map((item) => (
                <label key={item.code} className="filter-check">
                    <input
                        type="checkbox"
                        checked={selected.includes(item.code)}
                        onChange={(event) =>
                            onChange(
                                event.target.checked
                                    ? [...selected, item.code]
                                    : selected.filter(
                                          (code) => code !== item.code,
                                      ),
                            )
                        }
                    />
                    {item.name}
                </label>
            ))}
        </fieldset>
    );
}

type FilterFieldProps = {
    label: string;
    type: 'date' | 'number';
    value: string;
    onChange: (value: string) => void;
    error?: string;
};

function FilterField({
    label,
    type,
    value,
    onChange,
    error,
}: FilterFieldProps) {
    const id = useId();
    const errorId = `${id}-error`;

    return (
        <div className="field">
            <label htmlFor={id}>{label}</label>
            <input
                id={id}
                type={type}
                // Results are in whole kronor, but any number is a valid limit.
                step={type === 'number' ? 'any' : undefined}
                aria-invalid={error !== undefined}
                aria-describedby={error === undefined ? undefined : errorId}
                value={value}
                onChange={(event) => onChange(event.target.value)}
            />
            {error !== undefined && (
                <p id={errorId} className="field-error">
                    {error}
                </p>
            )}
        </div>
    );
}
