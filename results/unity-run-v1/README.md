# Unity runtime replication v1

This is a separate same-workstation replication of the .NET main study, using Unity 6000.3.11f1, `JsonUtility`, and `Application.persistentDataPath`. Do not pool its timings or fault observations with `results/main-run-v1/`.

## Final audit

- 147 size/strategy/checkpoint groups, each with 20 valid main trials (2,940 total).
- 900 save timings: 100 per strategy and item count, after 10 warm-ups per cell.
- Outcomes among the 2,940 valid main trials: 1,880 `old-valid`, 760 `no-valid-save`, 300 `new-valid`; zero unexpected states or recovery errors.
- `fault-trials.csv` contains 2,944 rows because four pilot-collision attempts are preserved as `controller-error`. The corresponding groups were backfilled with fresh trial 21 entries; those four controller-error rows are not part of the 2,940 valid main trials.
- Final group audit is `fault-audit.json`; summaries are `unity-fault-summary.csv`, `unity-timing-summary.csv`, and `unity-summary.json`.

## Files

- `fault-trials.csv`: raw fault observations. Checkpoint labels come from the Unity Player catalog files. Recovery latency excludes Player startup.
- `save-timings.csv`: raw timing observations for the three strategies and payload sizes.
- `catalog-*.json`: exact payload hashes, payload sizes, and checkpoint lists per size/strategy.
- `run-metadata.json`: engine/runtime, fault model, workload matrix, and catalog metadata.
- `artifact-hashes.json`: SHA-256 checksums for the Unity run outputs.
- `backfill-recover-*.json`: individual recovery outputs for the four replacement trials.
- `timing-*.json`: per-size timing outputs before consolidation into `save-timings.csv`.

The smoke pilot and the first four collision directories are retained separately from valid main trial rows. The incident and correction are described in the repository-root `protocol-unity-v1.md` and the manuscript methods. Future smoke runs use a distinct persistent-data root.

Unity saves were on C: (WD PC SN560 NVMe, NTFS); the .NET main study saved to D: (Intel SSDPEKNU512GZ NVMe, NTFS). This confounds any between-run absolute timing comparison. Both fault models are process termination only, with no OS crash, restart, power cut, or durability flush.
