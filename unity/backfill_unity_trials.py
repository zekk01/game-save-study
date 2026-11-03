"""Replace the four pilot-collided trial-1 cases with fresh main trials."""

from __future__ import annotations

import csv
import json
import os
import subprocess
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "unity" / "GameSaveStudyUnity"
PLAYER = PROJECT / "build" / "GameSaveStudyUnity.exe"
RUN = ROOT / "results" / "unity-run-v1"
INPUT = RUN / "fault-trials.csv"
CHUNK = 4096


def args_for(mode: str, **values: object) -> list[str]:
    result = ["--save-study-mode", mode]
    for key, value in values.items():
        result += ["--" + key.replace("_", "-"), str(value)]
    return result


def run(args: list[str], timeout: float = 90) -> int:
    return subprocess.run(
        [str(PLAYER), "-batchmode", "-nographics", *args],
        cwd=PROJECT,
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
        timeout=timeout,
        check=False,
    ).returncode


def read_json(path: Path) -> dict:
    value = json.loads(path.read_text(encoding="utf-8"))
    if "error" in value:
        raise RuntimeError(value["error"])
    return value


def main() -> None:
    metadata = json.loads((RUN / "run-metadata.json").read_text(encoding="utf-8"))
    persistent = Path(metadata["persistentDataPath"].replace("%LOCALAPPDATA%", os.environ["LOCALAPPDATA"]))
    records = list(csv.DictReader(INPUT.open(newline="", encoding="utf-8")))
    contaminated = [
        row for row in records
        if row["item_count"] == "25" and row["strategy"] == "direct"
        and row["trial"] == "1" and row["outcome"] == "controller-error"
    ]
    if {(int(r["checkpoint_index"]), r["checkpoint"]) for r in contaminated} != {
        (0, "before-truncate"), (1, "after-truncate"),
        (2, "after-chunk-0"), (3, "after-close"),
    }:
        raise RuntimeError(f"Expected exactly the four documented pilot collisions; got {len(contaminated)}")

    additions = []
    root = persistent / "items-25" / "direct"
    for original in contaminated:
        checkpoint = int(original["checkpoint_index"])
        case_dir = root / f"checkpoint-{checkpoint:03d}" / "trial-21"
        if case_dir.exists():
            raise FileExistsError(f"Refusing to overwrite backfill directory: {case_dir}")
        marker = case_dir / "checkpoint.ready"
        result = {**original, "trial": 21, "outcome": "controller-error", "recovery_ms": "",
                  "save_directory_bytes": "", "writer_exit_code": "", "writer_killed": 0,
                  "error": ""}
        process = subprocess.Popen(
            [str(PLAYER), "-batchmode", "-nographics", *args_for(
                "fault", strategy="direct", case_dir=case_dir, item_count=25,
                chunk_size=CHUNK, checkpoint_index=checkpoint,
            )], cwd=PROJECT, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
        )
        deadline = time.monotonic() + 90
        while time.monotonic() < deadline and process.poll() is None and not marker.is_file():
            time.sleep(0.02)
        if marker.is_file():
            process.kill()
            process.wait(timeout=20)
            result["writer_killed"] = 1
            result["writer_exit_code"] = process.returncode
            recovery_path = RUN / f"backfill-recover-{checkpoint}.json"
            code = run(args_for("recover", strategy="direct", case_dir=case_dir, result_path=recovery_path))
            if code == 0:
                recovered = read_json(recovery_path)
                result["outcome"] = recovered["outcome"]
                result["recovery_ms"] = recovered["recoveryMilliseconds"]
                result["save_directory_bytes"] = recovered["directoryBytes"]
            else:
                result["outcome"] = "recovery-error"
                result["error"] = f"Recovery Player exit {code}"
        else:
            if process.poll() is None:
                process.kill()
                process.wait(timeout=20)
                result["error"] = "checkpoint acknowledgement timeout"
            else:
                result["error"] = "writer exited before checkpoint acknowledgement"
            result["writer_exit_code"] = process.returncode
        additions.append(result)
        print(f"backfill checkpoint {checkpoint}: {result['outcome']}", flush=True)

    with INPUT.open("a", newline="", encoding="utf-8") as stream:
        writer = csv.DictWriter(stream, fieldnames=list(records[0]))
        writer.writerows(additions)

    with INPUT.open(newline="", encoding="utf-8") as stream:
        all_rows = list(csv.DictReader(stream))
    counts = {}
    expected_groups = 0
    complete_groups = 0
    for key, catalog in metadata["catalogs"].items():
        size, strategy = key.split("-", 1)
        for checkpoint_index in range(len(catalog["stages"])):
            expected_groups += 1
            group = [r for r in all_rows if r["item_count"] == size and r["strategy"] == strategy
                     and int(r["checkpoint_index"]) == checkpoint_index
                     and r["outcome"] != "controller-error"]
            if len(group) == metadata["trialsPerCheckpoint"]:
                complete_groups += 1
            elif len(group) != metadata["trialsPerCheckpoint"]:
                counts[f"{size}/{strategy}/{checkpoint_index}"] = len(group)
    outcomes = {}
    for row in all_rows:
        outcomes[row["outcome"]] = outcomes.get(row["outcome"], 0) + 1
    timing_rows = sum(1 for _ in csv.DictReader((RUN / "save-timings.csv").open(newline="", encoding="utf-8")))
    audit = {
        "planned_fault_trials": 2940,
        "raw_fault_rows_including_four_preserved_collision_records": len(all_rows),
        "valid_main_fault_trials": sum(v for k, v in outcomes.items() if k != "controller-error"),
        "expected_groups": expected_groups,
        "complete_groups_with_20_valid_main_trials": complete_groups,
        "all_groups_complete": complete_groups == expected_groups and not counts,
        "preserved_controller_error_rows": outcomes.get("controller-error", 0),
        "outcomes_including_preserved_controller_errors": outcomes,
        "timing_rows": timing_rows,
        "group_count_exceptions": counts,
    }
    (RUN / "fault-audit.json").write_text(json.dumps(audit, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(audit, indent=2))
    if not audit["all_groups_complete"] or timing_rows != 900:
        raise SystemExit(2)


if __name__ == "__main__":
    main()
