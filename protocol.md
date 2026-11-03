# Experimental protocol (version 1, pilot-informed; lock before main run)

The first two small runs were engineering pilots only. They exposed worker-control and workload-sizing issues; neither is study data. This version freezes the primary workload and measurements before the main run.

## Research question

Under abrupt process termination during a save, how do three game-save update procedures differ in the state recovered and in their normal-operation time and storage cost?

## Strategies

All methods begin with the same compact UTF-8 JSON game-state payload, using the same .NET serializer settings.

1. **Direct overwrite:** truncate the active file and write the new payload in fixed-size chunks.
2. **Temporary-file replacement:** write the complete payload to a temporary file in the same directory, close it, then replace the active file with `File.Replace`.
3. **Two-slot generation scheme:** preserve the prior slot and write the next generation to the other slot. Each slot contains a generation number, payload, and SHA-256 checksum. Recovery validates both slots and selects the highest generation that passes checksum and payload validation.

The implementation records exact operations and API calls. Replacement behavior is evaluated only on the tested Windows filesystem; no cross-platform atomicity guarantee will be inferred.

## Workload

Generate deterministic game-like saves containing player progress, inventory, quest state, and discovered locations. Test item/location counts of **25, 250, and 1,000**; quest count is `max(1, floor(item_count / 5))`. The fixed chunk size is **4,096 bytes**. These sizes are intended to span small, intermediate, and larger multi-chunk save payloads; raw serialized byte size is recorded for each run. Old/new payloads use seeds 7 and 19 and are held identical across strategies within each workload size. Their SHA-256 hashes serve as the recovery oracle.

## Failure injection

Run the writer in a child process. It pauses at each selected checkpoint; the controller terminates the child there, so the writer cannot continue past the selected point. Test checkpoints before file truncation/temp/slot creation, after truncation, after every write chunk, after close, and immediately before/after replacement where applicable. Repeat every strategy/workload-size/checkpoint combination **20 times** using the fixed payload seeds.

Each chunk write is followed by `FileStream.Flush()` before the checkpoint. This moves managed stream data through the OS file API; it does not request a durable-media flush. The experiment tests application-process termination only. It does not test kernel crashes, storage-controller behavior, or power loss. Do not describe these results as durability guarantees.

## Recovery outcome categories

After restarting the recovery routine, classify each result as:

- **Old valid:** complete prior generation matches its expected hash.
- **New valid:** complete intended generation matches its expected hash.
- **No valid save:** neither expected generation can be loaded and validated.
- **Unexpected state:** the parsed data matches neither expected generation or required invariants fail.
- **Recovery error:** the recovery routine itself fails unexpectedly.

Primary outcome: proportion of trials recovering either the complete old or complete new state. Also report each category separately; never count a silent unexpected-state load as success.

## Measurements

- **Save API latency:** pre-serialized base payload, 10 warm-up saves then 100 measured saves per strategy and size. Strategy order is shuffled with a fixed per-size seed. Timing includes file creation/write/stream flush/replacement and the two-slot envelope/checksum creation, but excludes generation and JSON serialization of the base game state.
- **Recovery latency:** time spent in the recovery routine after each injected interruption; process startup time is excluded.
- **Storage overhead:** bytes occupied by files in that strategy's save directory after interruption/recovery or normal save, including leftover temp files or both save slots.
- **Recovery outcomes:** raw counts by strategy, workload size, and interruption stage.

Fault-injection files and result files must share the same local volume. Record per-trial CSV and machine/run metadata (OS, filesystem, .NET runtime, processor count/identifier, chunk size, workload sizes, repetitions, seeds, and timer resolution). Report the machine's power mode and storage device in the run notes.

## Validity and limitations

- The initial study is a Windows/NTFS case study on one workstation; findings do not establish cross-platform behavior.
- The workload is synthetic and must be documented and published.
- The three strategies differ in recovery metadata and write sequence by design; this compares complete practical procedures, not isolated serialization formats.
- `Flush()` and process termination do not establish the state of data on physical media.
- Any exception, failed run, or deviation is retained and reported.
- Do not select results, workload sizes, or interruption points after inspecting outcomes without identifying that work as exploratory.

## Before the main run

- Confirm the harness matches this protocol and build it in Release mode.
- Record exact Windows build, NTFS volume, physical drive, AC/battery state, and .NET version.
- Run the full matrix once. If runtime is too high, make and document a protocol amendment before collecting final data.
- Preserve pilot outputs separately; do not combine them with main-study results.
