# retained benchmark evidence

these json files are the alternating-run samples, summary, and golden output
checksums recorded on 2026-09-30. see [method and limits](../performance.md).

`core` is the informational version embedded by msbuild. current results were
recorded from the optimized working tree before its feature commit, so this
field still contains the parent `fc918a0` revision. it does not identify the
uncommitted file contents. the baseline was a separate source export of the
released tag; it was not rebuilt from the working tree. `dsp-source-sha256.txt`
records the optimized files used for these measurements.

golden-run allocations include deliberate control-thread configuration edits;
steady render allocations are in the numbered reports. baseline fixtures and
optimized fixtures have matching primary/monitor checksums in all seven cases.
