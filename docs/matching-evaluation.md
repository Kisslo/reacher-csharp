# T2-07 Matching evaluation

## Method

The evaluation used 40 real companies from one ingest:

- 15 companies with SNI `43221`
- 10 companies with SNI `47111`
- 15 companies with SNI `96210`

The sample contained distinctive and generic company names. Each company was
queried once with the real Brave client before tuning and once after tuning.
The total was 80 Brave requests, below the ticket limit of 150.

For each run, the evaluator recorded only:

- `orgnr`
- whether the company had an official website
- the candidate position of the official website (`1`, `2`, `3`, or `none`)
- the confidence assigned to that candidate
- whether that candidate was preselected

The detailed orgnr-only log is [evaluation-log.csv](./evaluation-log.csv).
No URLs, titles, snippets, screenshots, or raw Brave responses were stored.

The measured change was to append `website` to the Brave query after the
workplace city. The confidence thresholds and blocked-domain list were not
changed.

## Metrics

- **Correct preselection:** the official website was present and preselected.
- **Wrong preselection:** another candidate was preselected instead of the
  official website.
- **None found when a site existed:** the official website did not appear in
  any of the three candidates.

The denominator for all percentages below is the 18 sampled companies known
to have an official website. The remaining 22 companies did not have a
website.

## Results before tuning

| Metric | Count | Percentage |
|---|---:|---:|
| Correct website found in candidates | 9 / 18 | 50.0% |
| Correct preselection | 8 / 18 | 44.4% |
| Wrong preselection | 0 / 18 | 0.0% |
| None found when a site existed | 9 / 18 | 50.0% |

## Results after tuning

| Metric | Count | Percentage |
|---|---:|---:|
| Correct website found in candidates | 12 / 18 | 66.7% |
| Correct preselection | 10 / 18 | 55.6% |
| Wrong preselection | 0 / 18 | 0.0% |
| None found when a site existed | 6 / 18 | 33.3% |

Appending `website` improved candidate recovery by 16.7 percentage points,
improved correct preselection by 11.2 percentage points, and reduced the
none-found rate by 16.7 percentage points. No incorrect candidate was observed
as high-confidence or preselected in either run.

## Chosen configuration

- Query: `"{company name}" {city} website`
- High-confidence threshold: `0.8`
- Medium-confidence threshold: `0.5`
- Title bonus: unchanged
- Blocked-domain list: unchanged
- Total Brave requests: 80 of 150

The thresholds were retained because the evaluation found no wrong
high-confidence or preselected candidates. Lowering them would risk reducing
precision without addressing cases where the official website was absent from
Brave's top three results.

## Limitations

- The sample contains 40 companies and may not represent all Swedish
  companies.
- Brave rankings can change between runs.
- Website existence and official-site labels were checked manually.
- The evaluation measured one query change; it did not test alternative query
  terms or orgnr-based queries.
