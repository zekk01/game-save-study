# Reproduction package note

Primary study data are in `results/main-run-v1/`; pilots are separate and excluded. Start with the repository README and `protocol.md`. The Release source is under `src/SaveStudy/`. Analysis uses `analysis/summarize.py` and `analysis/plots.py`.

The raw run records Windows 11 IoT Enterprise LTSC build 26100, .NET 9.0.6 / SDK 9.0.301, AMD Ryzen 9 7845HX, local D: NTFS, Intel SSDPEKNU512GZ NVMe, and a power-mode caveat. One main run was collected. `artifact-hashes.json` hashes the recorded raw data and build artifact. Do not combine pilot outputs with the main run.

Reproduction is currently local and the manuscript requires a public repository DOI before submission. The paper must not call this archive an open/public dataset until deposited.
