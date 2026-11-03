"""Summarize the separate Unity Player replication with standard-library tools."""

from __future__ import annotations

import csv
import json
import statistics
import argparse
from collections import Counter, defaultdict
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]


def read_csv(path: Path) -> list[dict[str, str]]:
    with path.open(newline="", encoding="utf-8") as stream:
        return list(csv.DictReader(stream))


def write_csv(path: Path, rows: list[dict[str, object]]) -> None:
    if not rows:
        return
    with path.open("w", newline="", encoding="utf-8") as stream:
        writer = csv.DictWriter(stream, fieldnames=list(rows[0]))
        writer.writeheader()
        writer.writerows(rows)


def quantile(values: list[float], q: float) -> float:
    ordered = sorted(values)
    if len(ordered) == 1:
        return ordered[0]
    position = (len(ordered) - 1) * q
    lower = int(position)
    upper = min(lower + 1, len(ordered) - 1)
    fraction = position - lower
    return ordered[lower] * (1 - fraction) + ordered[upper] * fraction


def main(run_dir: Path) -> None:
    faults = read_csv(run_dir / "fault-trials.csv")
    groups: dict[tuple[str, str, str], list[dict[str, str]]] = defaultdict(list)
    combinations: dict[tuple[str, str], dict] = {}
    for row in faults:
        groups[(row["item_count"], row["strategy"], row["checkpoint_index"])].append(row)
    metadata = json.loads((run_dir / "run-metadata.json").read_text(encoding="utf-8"))
    for key, catalog in metadata["catalogs"].items():
        size, strategy = key.split("-", 1)
        combinations[(size, strategy)] = catalog

    outcome_rows = []
    for (size, strategy), catalog in combinations.items():
        rows = [r for r in faults if r["item_count"] == size and r["strategy"] == strategy]
        counts = Counter(r["outcome"] for r in rows)
        expected_group_count = len(catalog["stages"])
        complete_groups = sum(
            len(groups.get((size, strategy, str(i)), [])) >= metadata["trialsPerCheckpoint"]
            and sum(r["outcome"] != "controller-error" for r in groups.get((size, strategy, str(i)), [])) >= metadata["trialsPerCheckpoint"]
            for i in range(expected_group_count)
        )
        outcome_rows.append({
            "item_count": size,
            "strategy": strategy,
            "expected_checkpoint_groups": expected_group_count,
            "groups_with_20_non_controller_errors": complete_groups,
            "raw_rows": len(rows),
            "old_valid": counts["old-valid"],
            "new_valid": counts["new-valid"],
            "no_valid_save": counts["no-valid-save"],
            "unexpected_state": counts["unexpected-state"],
            "recovery_error": counts["recovery-error"],
            "controller_error": counts["controller-error"],
        })
    write_csv(run_dir / "unity-fault-summary.csv", outcome_rows)

    timing_rows = read_csv(run_dir / "save-timings.csv")
    timing_groups: dict[tuple[str, str], list[float]] = defaultdict(list)
    byte_groups: dict[tuple[str, str], list[int]] = defaultdict(list)
    for row in timing_rows:
        key = (row["itemCount"], row["strategy"])
        timing_groups[key].append(float(row["saveMilliseconds"]))
        byte_groups[key].append(int(row["directoryBytes"]))
    summary = []
    for key, values in sorted(timing_groups.items(), key=lambda pair: (int(pair[0][0]), pair[0][1])):
        summary.append({
            "item_count": key[0],
            "strategy": key[1],
            "n": len(values),
            "median_ms": statistics.median(values),
            "p25_ms": quantile(values, 0.25),
            "p75_ms": quantile(values, 0.75),
            "p95_ms": quantile(values, 0.95),
            "mean_save_directory_bytes": statistics.mean(byte_groups[key]),
        })
    write_csv(run_dir / "unity-timing-summary.csv", summary)

    report = {
        "fault_rows": len(faults),
        "fault_groups": len(groups),
        "timing_rows": len(timing_rows),
        "fault_summary": outcome_rows,
        "timing_summary": summary,
        "limitations": [
            "Separate same-workstation Unity runtime replication; do not pool with the .NET main study.",
            "Forced Player-process termination is not an OS crash or power-loss test.",
            "Four pilot-collision rows are preserved as controller errors and excluded from main outcomes; the four groups have fresh trial-21 backfills, leaving 20 valid main trials in every group.",
        ],
    }
    (run_dir / "unity-summary.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"fault_rows": len(faults), "timing_rows": len(timing_rows)}, indent=2))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Summarize Unity process-interruption study CSVs")
    parser.add_argument("--run-dir", type=Path, default=ROOT / "results" / "unity-run-v1")
    args = parser.parse_args()
    main(args.run_dir.resolve())
