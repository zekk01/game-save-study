"""Create paper figures from audited SaveStudy summaries."""

from __future__ import annotations

import csv
import sys
from pathlib import Path

import matplotlib.pyplot as plt
import numpy as np


COLORS = {"direct": "#3366CC", "replace": "#D97706", "dual": "#198754"}
LABELS = {"direct": "Direct overwrite", "replace": "Temp + replace", "dual": "Two-slot + checksum"}


def rows(path: Path) -> list[dict[str, str]]:
    with path.open(newline="", encoding="utf-8-sig") as stream:
        return list(csv.DictReader(stream))


def plot_latency(result_dir: Path) -> None:
    data = rows(result_dir / "timing-summary.csv")
    sizes = sorted({int(row["payload_bytes"]) for row in data})
    fig, ax = plt.subplots(figsize=(7.2, 4.5), layout="constrained")
    for strategy in ("direct", "replace", "dual"):
        series = sorted((row for row in data if row["strategy"] == strategy), key=lambda row: int(row["payload_bytes"]))
        x = np.array([int(row["payload_bytes"]) for row in series], dtype=float)
        med = np.array([float(row["save_ms_median"]) for row in series])
        low = np.array([float(row["save_ms_p25"]) for row in series])
        high = np.array([float(row["save_ms_p75"]) for row in series])
        ax.errorbar(x, med, yerr=np.vstack((med - low, high - med)), marker="o", linewidth=2,
                    markersize=6, capsize=4, color=COLORS[strategy], label=LABELS[strategy])
    ax.set_xscale("log")
    ax.set_yscale("log")
    ax.set_xticks(sizes, [f"{size:,}" for size in sizes])
    ax.set_xlabel("Base save payload size (bytes)")
    ax.set_ylabel("Save API latency (ms; median and IQR)")
    ax.grid(True, which="major", alpha=0.25)
    ax.legend(frameon=False, loc="best")
    ax.set_title("Save latency by workload size")
    for extension in ("png", "pdf"):
        fig.savefig(result_dir / f"figure-latency.{extension}", dpi=300 if extension == "png" else None)
    plt.close(fig)


def plot_storage(result_dir: Path) -> None:
    data = rows(result_dir / "timing-summary.csv")
    item_sizes = sorted({int(row["item_count"]) for row in data})
    x = np.arange(len(item_sizes), dtype=float)
    width = 0.23
    fig, ax = plt.subplots(figsize=(7.2, 4.5), layout="constrained")
    for index, strategy in enumerate(("direct", "replace", "dual")):
        series = {int(row["item_count"]): row for row in data if row["strategy"] == strategy}
        ratios = [float(series[size]["directory_bytes_median"]) / float(series[size]["payload_bytes"]) for size in item_sizes]
        bars = ax.bar(x + (index - 1) * width, ratios, width, color=COLORS[strategy], label=LABELS[strategy])
        ax.bar_label(bars, fmt="%.2fx", padding=3, fontsize=8)
    ax.axhline(1.0, color="#555555", linewidth=1, linestyle="--")
    ax.set_xticks(x, [f"{size:,} items" for size in item_sizes])
    ax.set_ylabel("Total save-directory bytes / base payload bytes")
    ax.set_ylim(0, max(4.6, ax.get_ylim()[1]))
    ax.grid(True, axis="y", alpha=0.25)
    ax.set_axisbelow(True)
    ax.legend(frameon=False, loc="upper left")
    ax.set_title("Normal-save storage footprint")
    for extension in ("png", "pdf"):
        fig.savefig(result_dir / f"figure-storage.{extension}", dpi=300 if extension == "png" else None)
    plt.close(fig)


def main(result_dir: Path) -> None:
    plot_latency(result_dir)
    plot_storage(result_dir)
    print(f"Created latency and storage figures in {result_dir}")


if __name__ == "__main__":
    if len(sys.argv) != 2:
        raise SystemExit("Usage: python analysis/plots.py <result-directory>")
    main(Path(sys.argv[1]).resolve())
