"""Run the frozen Unity Player process-interruption replication."""

from __future__ import annotations

import argparse
import csv
import json
import re
import subprocess
import sys
import time
from collections import Counter
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "unity" / "GameSaveStudyUnity"
PLAYER = PROJECT / "build" / "GameSaveStudyUnity.exe"
RESULTS = ROOT / "results"
CHUNK_SIZE = 4096
ITEM_SIZES = (25, 250, 1000)
STRATEGIES = ("direct", "replace", "dual")
TRIALS = 20


def run_player(args: list[str], timeout: float = 90.0) -> int:
    completed = subprocess.run(
        [str(PLAYER), "-batchmode", "-nographics", *args],
        cwd=PROJECT,
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
        timeout=timeout,
        check=False,
    )
    return completed.returncode


def player_args(mode: str, **values: object) -> list[str]:
    result = ["--save-study-mode", mode]
    for key, value in values.items():
        result.extend(["--" + key.replace("_", "-"), str(value)])
    return result


def read_json(path: Path) -> dict:
    if not path.is_file():
        raise RuntimeError(f"Player did not write expected output: {path}")
    value = json.loads(path.read_text(encoding="utf-8"))
    if "error" in value:
        raise RuntimeError(value["error"])
    return value


def collect_catalogs(run_dir: Path, sizes: tuple[int, ...], strategies: tuple[str, ...]) -> tuple[dict, Path]:
    catalogs = {}
    persistent_root = None
    for size in sizes:
        for strategy in strategies:
            output = run_dir / f"catalog-{size}-{strategy}.json"
            code = run_player(
                player_args(
                    "catalog",
                    strategy=strategy,
                    item_count=size,
                    chunk_size=CHUNK_SIZE,
                    result_path=output,
                )
            )
            if code != 0:
                raise RuntimeError(f"Catalog Player failed for {size}/{strategy}: exit {code}")
            catalog = read_json(output)
            catalogs[(size, strategy)] = catalog
            # Keep smoke/pilot writes physically separate from the main run.
            current_root = Path(catalog["persistentDataPath"]) / "SaveStudyUnity" / run_dir.name
            if persistent_root is None:
                persistent_root = current_root
            elif current_root != persistent_root:
                raise RuntimeError("Unity persistentDataPath changed between catalog runs")
    if persistent_root is None:
        raise RuntimeError("No catalog generated")
    return catalogs, persistent_root


def run_faults(run_dir: Path, catalogs: dict, persistent_root: Path, repetitions: int) -> dict:
    csv_path = run_dir / "fault-trials.csv"
    fields = [
        "item_count", "strategy", "checkpoint_index", "checkpoint", "trial",
        "outcome", "recovery_ms", "save_directory_bytes", "payload_bytes",
        "chunk_size", "writer_exit_code", "writer_killed", "error",
    ]
    group_counts = Counter()
    totals = Counter()
    with csv_path.open("w", newline="", encoding="utf-8") as out_file:
        writer = csv.DictWriter(out_file, fieldnames=fields)
        writer.writeheader()
        for (size, strategy), catalog in catalogs.items():
            for checkpoint_index, checkpoint_name in enumerate(catalog["stages"]):
                for trial in range(1, repetitions + 1):
                    case_dir = persistent_root / f"items-{size}" / strategy / f"checkpoint-{checkpoint_index:03d}" / f"trial-{trial:02d}"
                    marker = case_dir / "checkpoint.ready"
                    recover_path = run_dir / "recover-result.json"
                    row = {
                        "item_count": size,
                        "strategy": strategy,
                        "checkpoint_index": checkpoint_index,
                        "checkpoint": checkpoint_name,
                        "trial": trial,
                        "outcome": "controller-error",
                        "recovery_ms": "",
                        "save_directory_bytes": "",
                        "payload_bytes": catalog["payloadBytes"],
                        "chunk_size": CHUNK_SIZE,
                        "writer_exit_code": "",
                        "writer_killed": 0,
                        "error": "",
                    }
                    if case_dir.exists():
                        row["error"] = "case directory already exists; preserved and skipped"
                        writer.writerow(row)
                        out_file.flush()
                        totals["case-directory-exists"] += 1
                        continue

                    process = subprocess.Popen(
                        [
                            str(PLAYER), "-batchmode", "-nographics",
                            *player_args(
                                "fault",
                                strategy=strategy,
                                case_dir=case_dir,
                                item_count=size,
                                chunk_size=CHUNK_SIZE,
                                checkpoint_index=checkpoint_index,
                            ),
                        ],
                        cwd=PROJECT,
                        stdout=subprocess.DEVNULL,
                        stderr=subprocess.DEVNULL,
                    )
                    deadline = time.monotonic() + 90.0
                    while time.monotonic() < deadline and process.poll() is None and not marker.is_file():
                        time.sleep(0.02)

                    if marker.is_file():
                        process.kill()
                        process.wait(timeout=20)
                        row["writer_killed"] = 1
                        row["writer_exit_code"] = process.returncode
                        try:
                            if recover_path.exists():
                                recover_path.unlink()
                            recover_code = run_player(
                                player_args(
                                    "recover",
                                    strategy=strategy,
                                    case_dir=case_dir,
                                    result_path=recover_path,
                                )
                            )
                            if recover_code != 0:
                                raise RuntimeError(f"recovery Player exit {recover_code}")
                            recovered = read_json(recover_path)
                            row["outcome"] = recovered["outcome"]
                            row["recovery_ms"] = recovered["recoveryMilliseconds"]
                            row["save_directory_bytes"] = recovered["directoryBytes"]
                        except Exception as exc:  # preserve every failed recovery as a raw row
                            row["outcome"] = "recovery-error"
                            row["error"] = str(exc)
                    else:
                        if process.poll() is None:
                            process.kill()
                            process.wait(timeout=20)
                            row["error"] = "checkpoint acknowledgement timeout"
                        else:
                            row["error"] = "writer exited before checkpoint acknowledgement"
                        row["writer_exit_code"] = process.returncode

                    writer.writerow(row)
                    out_file.flush()
                    group_counts[(size, strategy, checkpoint_index)] += 1
                    totals[row["outcome"]] += 1
                    print(f"fault {size:5d} {strategy:7s} {checkpoint_index:3d} {checkpoint_name:24s} {trial:2d}/{repetitions}: {row['outcome']}")

    expected_groups = sum(len(catalog["stages"]) for catalog in catalogs.values())
    expected_rows = expected_groups * repetitions
    actual_groups = len(group_counts)
    actual_rows = sum(group_counts.values())
    audit = {
        "repetitions_per_group": repetitions,
        "expected_groups": expected_groups,
        "actual_groups": actual_groups,
        "expected_rows": expected_rows,
        "actual_rows": actual_rows,
        "all_groups_complete": actual_groups == expected_groups and all(v == repetitions for v in group_counts.values()),
        "outcomes": dict(totals),
    }
    (run_dir / "fault-audit.json").write_text(json.dumps(audit, indent=2) + "\n", encoding="utf-8")
    return audit


def run_timings(run_dir: Path, sizes: tuple[int, ...]) -> int:
    total = 0
    for size in sizes:
        output = run_dir / f"timing-{size}.json"
        code = run_player(
            player_args(
                "timing",
                item_count=size,
                chunk_size=CHUNK_SIZE,
                iterations=100,
                case_root=run_dir / "timing-files",
                result_path=output,
            ),
            timeout=300.0,
        )
        if code != 0:
            raise RuntimeError(f"Timing Player failed for {size}: exit {code}")
        timing = read_json(output)
        rows = timing.get("rows", [])
        if len(rows) != 300:
            raise RuntimeError(f"Expected 300 timing rows for {size}, received {len(rows)}")
        total += len(rows)
        with (run_dir / "save-timings.csv").open("a", newline="", encoding="utf-8") as f:
            writer = csv.DictWriter(f, fieldnames=list(rows[0].keys()))
            if f.tell() == 0:
                writer.writeheader()
            writer.writerows(rows)
    return total


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--smoke", action="store_true", help="Run one pilot case; output is not study data")
    parser.add_argument("--trials", type=int, default=TRIALS)
    parser.add_argument("--run-label", help="Fresh output folder name under results/ (default: unity-run-v1 or unity-smoke-v1)")
    opts = parser.parse_args()
    if not PLAYER.is_file():
        raise FileNotFoundError(f"Build the Unity Player first: {PLAYER}")
    if opts.trials < 1:
        raise ValueError("--trials must be positive")
    label = opts.run_label or ("unity-smoke-v1" if opts.smoke else "unity-run-v1")
    if not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9._-]*", label) or label in {".", ".."}:
        raise ValueError("--run-label must be a simple folder name using letters, numbers, dots, underscores, or hyphens")
    run_dir = RESULTS / label
    if run_dir.exists():
        raise FileExistsError(f"Refusing to overwrite existing run directory: {run_dir}")
    run_dir.mkdir(parents=True)
    sizes = (25,) if opts.smoke else ITEM_SIZES
    strategies = ("direct",) if opts.smoke else STRATEGIES
    repetitions = 1 if opts.smoke else opts.trials
    catalogs, persistent_root = collect_catalogs(run_dir, sizes, strategies)
    (run_dir / "run-metadata.json").write_text(
        json.dumps(
            {
                "engine": "Unity 6000.3.11f1",
                "player": str(PLAYER),
                "faultModel": "controller terminates standalone Player process at acknowledged application checkpoint",
                "persistentDataPath": str(persistent_root),
                "chunkSize": CHUNK_SIZE,
                "itemSizes": sizes,
                "strategies": strategies,
                "trialsPerCheckpoint": repetitions,
                "catalogs": {f"{size}-{strategy}": value for (size, strategy), value in catalogs.items()},
                "pilotExcluded": bool(opts.smoke),
            },
            indent=2,
        ) + "\n",
        encoding="utf-8",
    )
    audit = run_faults(run_dir, catalogs, persistent_root, repetitions)
    if not opts.smoke:
        timings = run_timings(run_dir, sizes)
        audit["timing_rows"] = timings
        (run_dir / "fault-audit.json").write_text(json.dumps(audit, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(audit, indent=2))
    return 0 if audit["all_groups_complete"] else 2


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        sys.exit(1)
