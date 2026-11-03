"""Audit and summarize SaveStudy CSV outputs using only the Python standard library."""

from __future__ import annotations

import csv
import json
import random
import statistics
import sys
from collections import Counter, defaultdict
from datetime import datetime, timezone
from pathlib import Path


def percentile(values: list[float], q: float) -> float:
    ordered = sorted(values)
    if len(ordered) == 1:
        return ordered[0]
    position = (len(ordered) - 1) * q
    low = int(position)
    high = min(low + 1, len(ordered) - 1)
    fraction = position - low
    return ordered[low] * (1 - fraction) + ordered[high] * fraction


def read_csv(path: Path) -> list[dict[str, str]]:
    with path.open(newline="", encoding="utf-8-sig") as stream:
        return list(csv.DictReader(stream))


def write_csv(path: Path, rows: list[dict[str, object]], fieldnames: list[str]) -> None:
    with path.open("w", newline="", encoding="utf-8") as stream:
        writer = csv.DictWriter(stream, fieldnames=fieldnames)
        writer.writeheader()
        writer.writerows(rows)


def main(result_dir: Path) -> None:
    fault_path = result_dir / "fault-trials.csv"
    timing_path = result_dir / "save-timings.csv"
    metadata_path = result_dir / "run-metadata.json"
    faults = read_csv(fault_path)
    timings = read_csv(timing_path)
    metadata = json.loads(metadata_path.read_text(encoding="utf-8"))

    expected_trials = int(metadata["faultTrialsPerStage"])
    expected_timing = int(metadata["timingIterationsPerStrategy"])
    issues: list[str] = []
    fault_keys = [(r["strategy"], r["item_count"], r["stage"], r["trial"]) for r in faults]
    if len(fault_keys) != len(set(fault_keys)):
        issues.append("Duplicate strategy/item_count/stage/trial fault rows")
    timing_keys = [(r["strategy"], r["item_count"], r["iteration"]) for r in timings]
    if len(timing_keys) != len(set(timing_keys)):
        issues.append("Duplicate strategy/item_count/iteration timing rows")

    fault_groups: dict[tuple[str, str, str], list[dict[str, str]]] = defaultdict(list)
    for row in faults:
        fault_groups[(row["strategy"], row["item_count"], row["stage"])].append(row)
    incomplete_fault_groups = {"/".join(key): len(rows) for key, rows in fault_groups.items() if len(rows) != expected_trials}
    if incomplete_fault_groups:
        issues.append(f"Fault groups with count other than {expected_trials}: {incomplete_fault_groups}")

    timing_groups: dict[tuple[str, str], list[dict[str, str]]] = defaultdict(list)
    for row in timings:
        timing_groups[(row["strategy"], row["item_count"])].append(row)
    incomplete_timing_groups = {"/".join(key): len(rows) for key, rows in timing_groups.items() if len(rows) != expected_timing}
    if incomplete_timing_groups:
        issues.append(f"Timing groups with count other than {expected_timing}: {incomplete_timing_groups}")

    allowed_outcomes = {"old-valid", "new-valid", "no-valid-save", "unexpected-state", "recovery-error"}
    unknown_outcomes = sorted({r["outcome"] for r in faults} - allowed_outcomes)
    if unknown_outcomes:
        issues.append(f"Unknown recovery outcomes: {unknown_outcomes}")

    hashes_by_size: dict[str, set[tuple[str, str]]] = defaultdict(set)
    payload_bytes_by_size: dict[str, set[int]] = defaultdict(set)
    for row in faults:
        hashes_by_size[row["item_count"]].add((row["old_sha256"], row["new_sha256"]))
        payload_bytes_by_size[row["item_count"]].add(int(row["payload_bytes"]))
    for size, hashes in hashes_by_size.items():
        if len(hashes) != 1 or any(old == new for old, new in hashes):
            issues.append(f"Old/new hash inconsistency for item_count={size}")
    if any(len(v) != 1 for v in payload_bytes_by_size.values()):
        issues.append("Payload byte size varies across strategies/stages for a workload size")

    outcome_counts = Counter(r["outcome"] for r in faults)
    mixed_stage_groups = ["/".join(key) for key, rows in fault_groups.items() if len({r["outcome"] for r in rows}) > 1]
    strategy_groups: dict[tuple[str, str], list[dict[str, str]]] = defaultdict(list)
    for row in faults:
        strategy_groups[(row["strategy"], row["item_count"])].append(row)
    strategy_summary: list[dict[str, object]] = []
    for (strategy, item_count), rows in sorted(strategy_groups.items(), key=lambda item: (int(item[0][1]), item[0][0])):
        counts = Counter(r["outcome"] for r in rows)
        stage_count = sum(1 for key in fault_groups if key[:2] == (strategy, item_count))
        strategy_summary.append({
            "strategy": strategy,
            "item_count": int(item_count),
            "checkpoint_groups": stage_count,
            "stage_trial_observations": len(rows),
            "old_valid": counts["old-valid"],
            "new_valid": counts["new-valid"],
            "no_valid_save": counts["no-valid-save"],
            "unexpected_state": counts["unexpected-state"],
            "recovery_error": counts["recovery-error"],
        })
    write_csv(result_dir / "fault-strategy-summary.csv", strategy_summary, [
        "strategy", "item_count", "checkpoint_groups", "stage_trial_observations", "old_valid", "new_valid",
        "no_valid_save", "unexpected_state", "recovery_error",
    ])
    fault_summary: list[dict[str, object]] = []
    for (strategy, item_count, stage), rows in sorted(fault_groups.items(), key=lambda item: (int(item[0][1]), item[0][0], item[0][2])):
        counts = Counter(r["outcome"] for r in rows)
        fault_summary.append({
            "strategy": strategy,
            "item_count": int(item_count),
            "stage": stage,
            "trials": len(rows),
            "old_valid": counts["old-valid"],
            "new_valid": counts["new-valid"],
            "no_valid_save": counts["no-valid-save"],
            "unexpected_state": counts["unexpected-state"],
            "recovery_error": counts["recovery-error"],
            "recovery_ms_median": round(statistics.median(float(r["recovery_ms"]) for r in rows), 4),
            "directory_bytes_median": round(statistics.median(int(r["save_directory_bytes"]) for r in rows), 1),
        })
    write_csv(result_dir / "fault-summary.csv", fault_summary, [
        "strategy", "item_count", "stage", "trials", "old_valid", "new_valid", "no_valid_save",
        "unexpected_state", "recovery_error", "recovery_ms_median", "directory_bytes_median",
    ])

    timing_summary: list[dict[str, object]] = []
    timing_values: dict[tuple[str, str], dict[int, float]] = defaultdict(dict)
    for (strategy, item_count), rows in sorted(timing_groups.items(), key=lambda item: (int(item[0][1]), item[0][0])):
        values = [float(r["save_ms"]) for r in rows]
        for row in rows:
            timing_values[(strategy, item_count)][int(row["iteration"])] = float(row["save_ms"])
        timing_summary.append({
            "strategy": strategy,
            "item_count": int(item_count),
            "samples": len(values),
            "save_ms_median": round(statistics.median(values), 4),
            "save_ms_p25": round(percentile(values, 0.25), 4),
            "save_ms_p75": round(percentile(values, 0.75), 4),
            "save_ms_p90": round(percentile(values, 0.90), 4),
            "directory_bytes_median": round(statistics.median(int(r["save_directory_bytes"]) for r in rows), 1),
            "payload_bytes": int(rows[0]["payload_bytes"]),
        })
    write_csv(result_dir / "timing-summary.csv", timing_summary, [
        "strategy", "item_count", "samples", "save_ms_median", "save_ms_p25", "save_ms_p75", "save_ms_p90",
        "directory_bytes_median", "payload_bytes",
    ])

    paired_rows: list[dict[str, object]] = []
    for item_count in sorted({key[1] for key in timing_values}, key=int):
        for left, right in [("replace", "direct"), ("dual", "direct"), ("replace", "dual")]:
            left_values = timing_values[(left, item_count)]
            right_values = timing_values[(right, item_count)]
            common = sorted(set(left_values) & set(right_values))
            differences = [left_values[i] - right_values[i] for i in common]
            ratios = [left_values[i] / right_values[i] for i in common]
            rng = random.Random(7331 + int(item_count) + len(left) * 17 + len(right))
            boot_medians: list[float] = []
            boot_ratios: list[float] = []
            for _ in range(10_000):
                sample_d = [differences[rng.randrange(len(differences))] for _ in differences]
                sample_r = [ratios[rng.randrange(len(ratios))] for _ in ratios]
                boot_medians.append(statistics.median(sample_d))
                boot_ratios.append(statistics.median(sample_r))
            paired_rows.append({
                "item_count": int(item_count),
                "comparison": f"{left} minus {right}",
                "paired_iterations": len(common),
                "median_difference_ms": round(statistics.median(differences), 4),
                "median_difference_ci95_low": round(percentile(boot_medians, 0.025), 4),
                "median_difference_ci95_high": round(percentile(boot_medians, 0.975), 4),
                "median_ratio": round(statistics.median(ratios), 4),
                "median_ratio_ci95_low": round(percentile(boot_ratios, 0.025), 4),
                "median_ratio_ci95_high": round(percentile(boot_ratios, 0.975), 4),
            })
    write_csv(result_dir / "timing-paired-effects.csv", paired_rows, [
        "item_count", "comparison", "paired_iterations", "median_difference_ms", "median_difference_ci95_low",
        "median_difference_ci95_high", "median_ratio", "median_ratio_ci95_low", "median_ratio_ci95_high",
    ])

    audit = {
        "auditedUtc": datetime.now(timezone.utc).isoformat(),
        "faultRows": len(faults),
        "faultStageGroups": len(fault_groups),
        "expectedTrialsPerFaultGroup": expected_trials,
        "timingRows": len(timings),
        "timingGroups": len(timing_groups),
        "expectedTimingSamplesPerGroup": expected_timing,
        "outcomes": dict(sorted(outcome_counts.items())),
        "mixedOutcomeCheckpointGroups": len(mixed_stage_groups),
        "mixedOutcomeCheckpointGroupKeys": mixed_stage_groups,
        "itemCounts": sorted(int(v) for v in hashes_by_size),
        "payloadBytesByItemCount": {size: sorted(values) for size, values in sorted(payload_bytes_by_size.items(), key=lambda pair: int(pair[0]))},
        "issues": issues,
        "interpretation": "0 issues means the files conform to configured row counts and value checks; it does not validate experimental design or generalize beyond this run.",
    }
    (result_dir / "audit.json").write_text(json.dumps(audit, indent=2), encoding="utf-8")
    print(json.dumps(audit, indent=2))
    if issues:
        raise SystemExit(1)


if __name__ == "__main__":
    if len(sys.argv) != 2:
        raise SystemExit("Usage: python analysis/summarize.py <result-directory>")
    main(Path(sys.argv[1]).resolve())
