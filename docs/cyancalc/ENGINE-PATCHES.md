# Local engine corrections

The engine baseline remains IronCalc v0.9.0
(`297553dc3a37ec6b0f4821055220e3d26c4ac6a1`).
The following local correction is applied to that baseline.
[The manifest](../../tools/cyancalc/engine-patches.json) records the original and
patched Git blob for each modified upstream file. The source verifier checks those
exact contents and continues to reject undeclared changes to other upstream files.

## Deletion formula history

Deleting rows or columns can replace references with `#REF!` or shrink ranges.
Reinserting the deleted lines cannot reconstruct the original references.
`UserModel` therefore captures surviving formula anchors before deletion and keeps
the anchors whose formula index or array shape changed. Undo restores them after
reinserting the deleted cells, including formulas on other sheets. Formula indices
remain valid in the append-only shared formula table. Fixed-size array kind and
dimensions are retained; dynamic spills are reset before restoring their anchors.

This is a local implementation for the pinned engine, informed by
[IronCalc issue #1654](https://github.com/ironcalc/IronCalc/issues/1654) and external
contributor [PR #1655](https://github.com/ironcalc/IronCalc/pull/1655)
at `2993d200cd8f1508d91d57598bc7444dbfffcbf9`.
It is not a cherry-pick or an upstream-approved release fix. The proposed PR uses
newer formula displacement internals that are absent from the baseline.
Public method signatures, the generated binding and workbook file schemas are unchanged.

The correction is in engine history, so all hosts and command paths share it.
The regression suite covers row and column deletion, multiple lines, absolute and
relative references, whole and partial ranges, existing errors, other sheets,
arrays, repeated Undo/Redo, failed deletes and external diff replay.

## Binary collaboration compatibility

Deletion history now carries formula snapshots. This changes the unversioned
binary diff schema used by `FlushSendQueue` / `ApplyExternalDiffs`.
Do not exchange these diffs with unpatched v0.9.0, the PR build, or any other engine
build merely because their public methods match. A collaboration host must require
matching engine baseline and patch/diff format before accepting a connection.
`model.Query("info")` reports `enginePatches: ["delete-formula-history-v1"]` and
`diffFormat: "cyancalc-delete-formula-history-v1"` for that purpose.
This identifier does not implement a network handshake or authenticate a sender.
The desktop app has no collaboration transport and discards its outgoing queue.

The `.ic` workbook does not store Undo history or the outgoing queue, so this patch
does not alter its schema. XLSX serialization is also unchanged. Loading a workbook
starts a new history as before.

## Upstream reconciliation

As of 2026-10-31, upstream issue #1654 remains open and external PR #1655 is
unmerged. The correction above remains a CyanCalc-local patch; the PR's existence
or its author's reported test results do not establish upstream acceptance.

Revisit this patch when updating the pinned engine, when the upstream issue or PR
changes, or when an alternative upstream correction is released:

1. Check maintainer review and the actual merged commit, then identify the release
   or proposed engine revision containing the correction. Review changes since the
   PR head recorded above instead of assuming the final implementation matches it.
2. Run the deletion-history regressions against that candidate, including row and
   column deletion, cross-sheet references, ranges, arrays, repeated Undo/Redo and
   external diff replay. Verify saved workbooks through the bindings and desktop
   command paths as well as through the engine.
3. Reassess the binary diff schema and peer compatibility separately from `.ic`
   and XLSX compatibility. Do not reuse the current diff format identifier for an
   incompatible schema or assume matching public methods imply compatible diffs.
4. Once the replacement passes, remove only the superseded local implementation
   and retain relevant regression coverage. Update the pinned revision, patch
   manifest, source verifier expectations, binding metadata and compatibility
   documentation together.

If the proposal is rejected or does not cover these cases, retain the local
correction and record the remaining gap. An open tracking item does not prevent
shipping the independently verified correction.
