# Initial results summary (descriptive; limited to this run)

## Data checks

The audit script found 3,500 fault rows across 175 strategy/size/checkpoint groups, with 20 trials per group, and 900 save-timing rows across nine strategy/size groups, with 100 measurements per group. No duplicate or incomplete groups, unknown outcomes, mixed outcomes within a checkpoint group, or inconsistent payload sizes/hashes were found. The audit does not validate external generalizability.

Base payload sizes were 3,025 bytes (25 items), 29,510 bytes (250), and 117,963 bytes (1,000). Across the 3,500 checkpoint-trial observations, outcomes were 2,440 old-valid, 300 new-valid, 760 no-valid-save, and zero unexpected-state or recovery-error.

## Fault-recovery behavior

- Direct overwrite lost the previous valid file immediately after truncation. During partial writes it had no valid save until the entire base payload was written. At each tested point, all 20 repeats had the same classification.
- Temporary-file replacement retained the prior save throughout creation and writing of the temporary file, then exposed the complete new save after `File.Replace`.
- The two-slot method retained the old generation while the new slot was incomplete; after a complete checksummed envelope had been written, recovery selected the new generation.
- No tested checkpoint produced a syntactically valid state that matched neither expected state.

These are observations for this exact protocol and Windows/NTFS environment. Checkpoint-trial totals are not independent user crash probabilities and should not be presented as such.

## Save API timing

Median latency and interquartile range (IQR), milliseconds:

| Base payload | Direct overwrite | Temp + replace | Two-slot + checksum |
|---:|---:|---:|---:|
| 3,025 B | 0.120 (0.112–0.132) | 1.639 (1.565–1.742) | 0.632 (0.161–1.362) |
| 29,510 B | 0.156 (0.146–0.232) | 5.391 (4.999–5.923) | 0.251 (0.238–0.279) |
| 117,963 B | 0.236 (0.227–0.353) | 5.621 (5.186–5.912) | 0.536 (0.521–0.582) |

Paired median timing differences were positive for temp+replace versus direct at all sizes: 1.507 ms (95% paired bootstrap interval 1.474–1.538), 5.030 ms (4.846–5.197), and 5.174 ms (4.912–5.363). The method and interval describe only the paired samples from this one run/device; they are not confidence intervals over hardware or players. Direct/dual timing distributions showed outliers, so medians and IQR are the primary summaries.

## Storage footprint

Normal-save directories contained approximately one base-payload equivalent for direct overwrite and temp+replace. The two-slot scheme used 3.50–3.57 times the base payload size because it retained two encoded slots plus generation/checksum envelope overhead. Exact values are in `timing-summary.csv`.

## Files

- Raw observations: `fault-trials.csv`, `save-timings.csv`.
- Machine/run context: `run-context.md`, `run-metadata.json`.
- Audited summaries: `audit.json`, `fault-summary.csv`, `fault-strategy-summary.csv`, `timing-summary.csv`, `timing-paired-effects.csv`.
- Figures: `figure-latency.png/.pdf`, `figure-storage.png/.pdf`.

## Interpretation boundary

The observed ordering is consistent with what the three procedures are designed to do, so the paper must not present it as a newly invented guarantee. The contribution should be framed as a reproducible game-save workload evaluation of the practical recovery windows and measured time/storage tradeoffs. Novelty remains a material review risk; the literature search is preliminary and should be strengthened before submission.
