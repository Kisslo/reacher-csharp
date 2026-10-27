-- Fictional dev data, so T2-01 and the frontend can be built before ingest exists (T1-01).
--
-- Every name, street, orgnr and amount is made up. Each orgnr has the shape of an
-- aktiebolag's but a deliberately wrong check digit, so it cannot belong to a real company.
-- SNI codes, municipality codes and postal codes are real: they point at no one.
--
-- Replaces every company and all coverage. Never run it against a database that holds
-- a real ingest. The command is in the README.

BEGIN;

-- Also empties workplace and financial_fact, which reference company.
TRUNCATE company, coverage CASCADE;

-- 25 hair salons (96210) and 5 beauty salons (96220). Names with Å, Ä and Ö test sorting.
INSERT INTO company (orgnr, name, sni, legal_form, company_status, employee_class,
                     revenue_class, revenue_class_year, registered_at, ad_block_type,
                     updated_at)
SELECT orgnr, name, sni, '49', '1', employee_class, revenue_class, 2025,
       registered_at::date, '1', now()
FROM (VALUES
    ('5599000015', 'Saxen & Kammen AB',        '96210', '2', '2', '2014-03-12'),
    ('5599000023', 'Hårfixarna AB',            '96210', '3', '3', '2009-09-01'),
    ('5599000031', 'Lockigt Läge AB',          '96210', '2', '2', '2018-05-23'),
    ('5599000049', 'Barberarnas Bod AB',       '96210', '1', '1', '2021-11-02'),
    ('5599000056', 'Klippoteket AB',           '96210', '4', '4', '2003-02-17'),
    ('5599000064', 'Toppform Frisör AB',       '96210', '2', '2', '2016-08-30'),
    ('5599000072', 'Salong Silverlock AB',     '96210', '2', '3', '2011-04-04'),
    ('5599000080', 'Kamhuset AB',              '96210', '1', '1', NULL),
    ('5599000098', 'Frisyrfabriken AB',        '96210', '3', '3', '2007-10-15'),
    ('5599000106', 'Ängens Hårstudio AB',      '96210', '5', '4', '1998-06-01'),
    ('5599000114', 'Rakt Snitt AB',            '96210', '2', '2', '2022-01-10'),
    ('5599000122', 'Hårpalatset Norr AB',      '96210', '2', '2', '2023-03-03'),
    ('5599000130', 'Lugg & Lock AB',           '96210', '1', '1', '2023-09-19'),
    ('5599000148', 'Öresunds Frisör AB',       '96210', '2', '2', '2022-06-14'),
    ('5599000155', 'Studio Slingan AB',        '96210', '3', '3', '2021-12-01'),
    ('5599000163', 'Femte Saxen AB',           '96210', '1', '1', '2019-02-28'),
    ('5599000171', 'Klipp & Kaffe AB',         '96210', '2', '2', '2017-07-07'),
    ('5599000189', 'Barber Nova AB',           '96210', '1', '1', '2020-10-20'),
    ('5599000197', 'Hårboden i Hörnet AB',     '96210', '2', '1', '2015-05-05'),
    ('5599000205', 'Zebra Frisörer AB',        '96210', '2', '2', '2012-12-12'),
    ('5599000213', 'Ålänningens Salong AB',    '96210', '1', '1', '2025-04-01'),
    ('5599000221', 'Vågen Hårdesign AB',       '96210', '2', '2', '2024-08-15'),
    ('5599000239', 'Blankt & Fint AB',         '96210', '1', '1', NULL),
    ('5599000247', 'Kort & Gott Frisör AB',    '96210', '2', '2', '2024-11-11'),
    ('5599000254', 'Mittbena AB',              '96210', '1', '1', '2025-09-09'),
    ('5599000262', 'Hudvårdshörnan AB',        '96220', '2', '2', '2016-03-21'),
    ('5599000270', 'Spegel & Glans AB',        '96220', '1', '1', '2020-02-02'),
    ('5599000288', 'Ögonblick Skönhet AB',     '96220', '2', '2', '2013-06-18'),
    ('5599000296', 'Nagelateljén AB',          '96220', '1', '1', '2019-11-30'),
    ('5599000304', 'Lyster Skönhetsstudio AB', '96220', '3', '3', '2010-01-25')
) AS c (orgnr, name, sni, employee_class, revenue_class, registered_at);

-- One workplace per company, spread over Stockholm (0180), Solna (0184) and
-- Göteborg (1480). Every fifth company has a second workplace in another municipality,
-- so one company can match several (D15). Most have no name, as in SCB.
WITH numbered AS (
    SELECT orgnr, sni, CAST(row_number() OVER (ORDER BY orgnr) AS int) AS n
    FROM company
)
INSERT INTO workplace (cfar, orgnr, name, sni, municipality, street, postal_code, city,
                       workplace_status, ad_block_type, updated_at)
SELECT (90000000 + n)::text, orgnr,
       CASE WHEN n % 7 = 0 THEN 'Salongen nr ' || n END,
       sni,
       (ARRAY['0180', '0184', '1480'])[n % 3 + 1],
       'Påhittade gatan ' || n,
       (ARRAY['111 20', '171 45', '411 05'])[n % 3 + 1],
       (ARRAY['Stockholm', 'Solna', 'Göteborg'])[n % 3 + 1],
       '1', '1', now()
FROM numbered
UNION ALL
SELECT (90001000 + n)::text, orgnr, NULL, sni,
       (ARRAY['0184', '1480', '0180'])[n % 3 + 1],
       'Andra gatan ' || n,
       (ARRAY['171 45', '411 05', '111 20'])[n % 3 + 1],
       (ARRAY['Solna', 'Göteborg', 'Stockholm'])[n % 3 + 1],
       '1', '1', now()
FROM numbered
WHERE n % 5 = 0;

-- Annual reports for 20 of the 30 companies. The last 10 have none, so
-- hasFinancials=false has something to find. Whole kronor.
--   n 1-10:  fiscal years 2023-2025
--   n 11-15: fiscal years 2024-2025
--   n 16-20: 2025 only, net_result without revenue (a K2 report that leaves the line out)
-- Every fourth company made a loss.
WITH numbered AS (
    SELECT orgnr, CAST(row_number() OVER (ORDER BY orgnr) AS int) AS n
    FROM company
),
figures AS (
    SELECT n, orgnr, year, make_date(year, 12, 31) AS period_end,
           (400000 + (n * 37 % 30) * 150000 + (year - 2023) * 50000)::numeric AS revenue
    FROM numbered, generate_series(2023, 2025) AS year
    WHERE n <= 10
       OR (n <= 15 AND year >= 2024)
       OR (n <= 20 AND year = 2025)
)
INSERT INTO financial_fact (orgnr, period_end, key, value, document_id, fetched_at)
SELECT orgnr, period_end, 'revenue', revenue, 'seed-' || orgnr || '-' || year, now()
FROM figures
WHERE n <= 15
UNION ALL
SELECT orgnr, period_end, 'net_result',
       CASE WHEN n % 4 = 0 THEN -round(revenue / 20) ELSE round(revenue / 12) END,
       'seed-' || orgnr || '-' || year, now()
FROM figures;

-- 96210 is fresh in Stockholm and Solna and stale in Göteborg (40 days old, D6), so the
-- API's stale flag has something to show. 96220 is fresh everywhere.
INSERT INTO coverage (sni, municipality, fetched_at) VALUES
    ('96210', '0180', now() - interval '2 days'),
    ('96210', '0184', now() - interval '10 days'),
    ('96210', '1480', now() - interval '40 days'),
    ('96220', '0180', now() - interval '5 days'),
    ('96220', '0184', now() - interval '5 days'),
    ('96220', '1480', now() - interval '5 days');

COMMIT;
