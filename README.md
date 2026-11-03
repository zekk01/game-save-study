# Game-save update strategy study

Reproducible case study comparing direct overwrite, temporary-file replacement, and a two-slot generation/checksum strategy for synthetic game-state saves under controlled writer-process termination.

## Research question

For identical game-like JSON payloads, how do the three update procedures compare in recovered state at predeclared interruption checkpoints, normal save latency, recovery routine latency, and storage footprint?

## Completed

- C#/.NET fault-injection and save-timing harness implemented in `src/SaveStudy/`.
- Protocol v1 and its workload, seeds, checkpoint strategy, and analysis rules documented in `protocol.md`.
- Engineering pilots completed; the Unity smoke pilot collided with four initial main-run trial directories due to a controller path bug. Those original rows are preserved as `controller-error`, the corresponding trials were rerun as trial 21, and all 147 checkpoint groups now contain 20 valid main observations.
- Main run completed on one Windows 11/NTFS workstation: 3,500 interruption trials and 900 normal-save timing observations.
- Raw observations, hardware/runtime notes, audit, summary tables, figures, and artifact hashes are in `results/main-run-v1/`.
- Separate Unity 6000.3.11f1 runtime replication completed: 2,940 valid fault trials across 147 complete checkpoint groups, four preserved pilot-collision rows, and 900 save timings. Results and analysis are in `results/unity-run-v1/`; Unity project/protocol/controller are under `unity/`.
- The Unity replication uses C: (WD PC SN560) while the .NET main run used D: (Intel SSDPEKNU512GZ); analyze timings within each run and do not interpret them as a controlled runtime comparison.
- Python standard-library audit/summarization in `analysis/summarize.py`; plotting script uses NumPy/Matplotlib.
- The JEPCS LaTeX manuscript source and seven-page draft PDF are in `manuscript/`; remaining submission checks are in `manuscript/STATUS.md` and `manuscript/JEPCS-checklist.md`. The author reports reviewing the current draft; it has not been submitted.
- A completion checklist is in `WORKPLAN.md`.
- The literature log includes targeted citation-chain and OpenAlex-index searches (2026-11-03) identifying peer-reviewed MMO checkpoint recovery and transaction-persistence studies (Vaz Salles et al., 2009; Cao et al., 2011; Zhang & Kemme, 2011), and the related 2010 McGill thesis. The manuscript now recognizes this substantial game-state recovery prior art and is positioned only as a local-file process-termination case study; JEPCS originality/fit remains high risk. The search is not systematic; the 45- and 36-record OpenAlex screening snapshots are preserved under `literature/`.
- This public source and data repository is https://github.com/zekk01/game-save-study. No DOI has been assigned.


## Reproduction commands

Use Python 3.10 or newer. The audit/summary scripts use the standard library; plotting also requires the pinned packages in `requirements.txt`.

### Compile the manuscript

Install Tectonic 0.17.0. The JEPCS class and logo are included in `manuscript/`; from that directory run:

```powershell
tectonic --bundle https://data1b.fullyjustified.net/tlextras-2022.0r0.tar main.tex --keep-logs
```

This v33 bundle is the TeX Live 2022 bundle mirrored by the official Tectonic relay service. The template selects Times New Roman when available, so page breaks may differ on systems with different font installations.

### Recreate analysis from the included observations

From the project root:

```powershell
python analysis/summarize.py results/main-run-v1
python analysis/plots.py results/main-run-v1
python analysis/summarize_unity.py --run-dir results/unity-run-v1
Copy-Item results/main-run-v1/figure-latency.png manuscript/figure-latency.png -Force
Copy-Item results/main-run-v1/figure-storage.png manuscript/figure-storage.png -Force
```

The Unity summary command accepts any run directory produced by the Unity controller. The plot script produces the two manuscript figures from the .NET primary run only; Unity timings are reported separately and are not pooled or compared directly.

### Repeat the .NET experiment

Install the .NET 9 SDK, then run the frozen matrix into a new output folder (the runner overwrites files in its output folder):

```powershell
dotnet run --project src/SaveStudy/SaveStudy.csproj --configuration Release -- run --fault-trials 20 --timing-iterations 100 --item-sizes 25,250,1000 --chunk-size 4096 --output-dir results/reproduction-main-run-v1
python analysis/summarize.py results/reproduction-main-run-v1
python analysis/plots.py results/reproduction-main-run-v1
```

### Rebuild and repeat the Unity Player experiment

Install Unity Editor 6000.3.11f1 with Windows Standalone Build Support. From the project root, replace the executable path with the installed editor path:

```powershell
& 'C:/Path/To/Unity.exe' -batchmode -quit -projectPath 'unity/GameSaveStudyUnity' -executeMethod SaveStudyUnity.Editor.SaveStudyBuild.BuildWindowsPlayer
python unity/run_unity_study.py --trials 20 --run-label unity-reproduction-v1
python analysis/summarize_unity.py --run-dir results/unity-reproduction-v1
```

The controller uses a unique label to keep repeated data separate and refuses to overwrite an existing run folder. The Unity project version is pinned in `ProjectSettings/ProjectVersion.txt`. Unity Player runs should be performed only on a controlled workstation using the published protocol; do not treat the interruption counts as real-world crash probabilities.

## Scope

This is a synthetic workload, one run per runtime, one workstation, local NTFS volumes, and an application-process-termination fault model. It is not an OS crash, power-loss, durable-media, player study, or cross-platform evaluation. Results do not establish real-world failure probabilities or generalize across hardware. The two-slot pattern is established engineering practice; the paper claims only a bounded empirical implementation comparison.

## Before submission

The literature search remains targeted and needs scholarly-database expansion and citation chaining. The author reports reviewing the current manuscript and supplying declarations; the author remains responsible for all content. The public repository is available at the URL above, but no DOI has been assigned. Editorial metadata (article ID, DOI, and publication dates) are marked unassigned pending journal processing. The PDF was rebuilt with current author details and declarations. JEPCS has not been contacted and the manuscript has not been submitted.
