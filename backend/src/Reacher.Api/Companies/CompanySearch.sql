WITH company_values AS (
    SELECT
        c.orgnr,
        c.name,
        c.sni,
        c.employee_class,
        c.revenue_class,
        c.revenue_class_year,
        c.registered_at,
        latest.period_end AS latest_period_end,
        latest.revenue AS latest_revenue,
        latest.net_result AS latest_net_result
    FROM company AS c
    LEFT JOIN LATERAL (
        SELECT
            f.period_end,
            MAX(f.value) FILTER (WHERE f.key = 'revenue') AS revenue,
            MAX(f.value) FILTER (WHERE f.key = 'net_result') AS net_result
        FROM financial_fact AS f
        WHERE f.orgnr = c.orgnr
        GROUP BY f.period_end
        ORDER BY f.period_end DESC
        LIMIT 1
    ) AS latest ON TRUE
    WHERE EXISTS (
        SELECT 1
        FROM workplace AS matching_workplace
        WHERE matching_workplace.orgnr = c.orgnr
          AND matching_workplace.sni = @sni
          AND matching_workplace.municipality = ANY(@sni_municipalities)
    )
),
filtered AS (
    SELECT *
    FROM company_values
    WHERE (
            cardinality(@employee_classes) = 0
            OR employee_class = ANY(@employee_classes)
        )
      AND (
            cardinality(@revenue_classes) = 0
            OR revenue_class = ANY(@revenue_classes)
        )
      AND (@registered_from IS NULL OR registered_at >= @registered_from)
      AND (@registered_to IS NULL OR registered_at <= @registered_to)
      AND (
            (
                NOT @has_financials
                AND @revenue_min IS NULL
                AND @revenue_max IS NULL
                AND @net_result_min IS NULL
                AND @net_result_max IS NULL
            )
            OR latest_period_end IS NOT NULL
        )
      AND (@revenue_min IS NULL OR latest_revenue >= @revenue_min)
      AND (@revenue_max IS NULL OR latest_revenue <= @revenue_max)
      AND (@net_result_min IS NULL OR latest_net_result >= @net_result_min)
      AND (@net_result_max IS NULL OR latest_net_result <= @net_result_max)
),
ordered AS (
    SELECT
        filtered.*,
        COUNT(*) OVER () AS total_count,
        (
            CASE
                WHEN @sort = 'name' AND @order = 'asc' THEN name IS NULL
                WHEN @sort = 'revenue' AND @order = 'asc' THEN latest_revenue IS NULL
                WHEN @sort = 'netResult' AND @order = 'asc' THEN latest_net_result IS NULL
                WHEN @sort = 'registeredAt' AND @order = 'asc' THEN registered_at IS NULL
                WHEN @sort = 'employeeClass' AND @order = 'asc' THEN employee_class IS NULL
                WHEN @sort = 'name' AND @order = 'desc' THEN name IS NULL
                WHEN @sort = 'revenue' AND @order = 'desc' THEN latest_revenue IS NULL
                WHEN @sort = 'netResult' AND @order = 'desc' THEN latest_net_result IS NULL
                WHEN @sort = 'registeredAt' AND @order = 'desc' THEN registered_at IS NULL
                WHEN @sort = 'employeeClass' AND @order = 'desc' THEN employee_class IS NULL
            END
        ) AS missing_sort_value
    FROM filtered
),
paged AS (
    SELECT
        ordered.*,
        ROW_NUMBER() OVER (
            ORDER BY
                missing_sort_value,
                CASE WHEN @sort = 'name' AND @order = 'asc' THEN name END ASC,
                CASE WHEN @sort = 'name' AND @order = 'desc' THEN name END DESC,
                CASE WHEN @sort = 'revenue' AND @order = 'asc' THEN latest_revenue END ASC,
                CASE WHEN @sort = 'revenue' AND @order = 'desc' THEN latest_revenue END DESC,
                CASE WHEN @sort = 'netResult' AND @order = 'asc' THEN latest_net_result END ASC,
                CASE WHEN @sort = 'netResult' AND @order = 'desc' THEN latest_net_result END DESC,
                CASE WHEN @sort = 'registeredAt' AND @order = 'asc' THEN registered_at END ASC,
                CASE WHEN @sort = 'registeredAt' AND @order = 'desc' THEN registered_at END DESC,
                CASE WHEN @sort = 'employeeClass' AND @order = 'asc' THEN employee_class END ASC,
                CASE WHEN @sort = 'employeeClass' AND @order = 'desc' THEN employee_class END DESC,
                orgnr
        ) AS result_order
    FROM ordered
    ORDER BY
        missing_sort_value,
        CASE WHEN @sort = 'name' AND @order = 'asc' THEN name END ASC,
        CASE WHEN @sort = 'name' AND @order = 'desc' THEN name END DESC,
        CASE WHEN @sort = 'revenue' AND @order = 'asc' THEN latest_revenue END ASC,
        CASE WHEN @sort = 'revenue' AND @order = 'desc' THEN latest_revenue END DESC,
        CASE WHEN @sort = 'netResult' AND @order = 'asc' THEN latest_net_result END ASC,
        CASE WHEN @sort = 'netResult' AND @order = 'desc' THEN latest_net_result END DESC,
        CASE WHEN @sort = 'registeredAt' AND @order = 'asc' THEN registered_at END ASC,
        CASE WHEN @sort = 'registeredAt' AND @order = 'desc' THEN registered_at END DESC,
        CASE WHEN @sort = 'employeeClass' AND @order = 'asc' THEN employee_class END ASC,
        CASE WHEN @sort = 'employeeClass' AND @order = 'desc' THEN employee_class END DESC,
        orgnr
    LIMIT @page_size
    OFFSET @page_offset
),
response AS (
    SELECT jsonb_build_object(
        'items',
        COALESCE(
            (
                SELECT jsonb_agg(
                    jsonb_build_object(
                        'orgnr', p.orgnr,
                        'name', p.name,
                        'sni', p.sni,
                        'employeeClass', p.employee_class,
                        'revenueClass', p.revenue_class,
                        'revenueClassYear', p.revenue_class_year,
                        'registeredAt', p.registered_at,
                        'workplaces', (
                            SELECT COALESCE(jsonb_agg(jsonb_build_object(
                                'name', w.name,
                                'municipality', w.municipality,
                                'city', w.city
                            ) ORDER BY w.cfar), '[]'::jsonb)
                            FROM workplace AS w
                            WHERE w.orgnr = p.orgnr
                              AND w.sni = @sni
                              AND w.municipality = ANY(@sni_municipalities)
                        ),
                        'financials', (
                            SELECT COALESCE(jsonb_agg(jsonb_build_object(
                                'periodEnd', years.period_end,
                                'revenue', years.revenue,
                                'netResult', years.net_result
                            ) ORDER BY years.period_end DESC), '[]'::jsonb)
                            FROM (
                                SELECT
                                    f.period_end,
                                    MAX(f.value) FILTER (WHERE f.key = 'revenue') AS revenue,
                                    MAX(f.value) FILTER (WHERE f.key = 'net_result') AS net_result
                                FROM financial_fact AS f
                                WHERE f.orgnr = p.orgnr
                                GROUP BY f.period_end
                                ORDER BY f.period_end DESC
                                LIMIT 3
                            ) AS years
                        )
                    )
                    ORDER BY p.result_order
                )
                FROM paged AS p
            ),
            '[]'::jsonb
        ),
        'page', @page_offset / @page_size + 1,
        'pageSize', @page_size,
        'totalCount', COALESCE(
            (SELECT MAX(total_count) FROM paged),
            (SELECT COUNT(*) FROM filtered)
        ),
        'coverage', (
            SELECT COALESCE(jsonb_agg(jsonb_build_object(
                'municipality', requested.municipality,
                'fetchedAt', coverage.fetched_at,
                'stale', coverage.fetched_at IS NULL
                    OR coverage.fetched_at < @stale_cutoff
            ) ORDER BY requested.ordinality), '[]'::jsonb)
            FROM unnest(@sni_municipalities) WITH ORDINALITY AS requested(municipality, ordinality)
            LEFT JOIN coverage
                ON coverage.sni = @sni
               AND coverage.municipality = requested.municipality
        )
    ) AS payload
)
SELECT payload::text
FROM response;
