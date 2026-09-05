# Architecture and refactoring checks

## Responsibilities

- `Program.cs`: startup, single instance routing and diagnostic command dispatch.
- `App.cs`: application lifecycle, settings application, tray behavior and main panel.
- `MainForm.Graph.cs`: collapsible diagnostic section and its redraw timer.
- `ThemeControls.cs`: shared appearance and controls.
- `WindowReport.cs`: the single parser for native readings and list presentation.
- `GraphHistory.cs`: timestamped ten-second history, context boundaries and Freeze; no UI or native calls.
- `LiveGraph.cs`: rendering and adaptation of the history model to a control.
- `PlayerBridge.cs`: pipe reception, validated browser lookup and native updates, with these operations separated into methods.
- `DimmingResponse.h`: existing window/video response policies shared by runtime and native regression checks.
- `WindowAnalysis.h`: visible-region discovery, frame-matched browser snapshots, sampling, state and reporting.
- `GraphTimeline.h`: bounded native timeline, monotonic sample times and separately timestamped context events.
- `PipelineTrace.h`: opt-in timing diagnostics without screen content.

The window-list report ABI remains compatible. The Firefox protocol additionally carries browser window identity and activation timing; `NfGraphRead` supplies bounded batches of engine-timed graph readings and event markers. See [pipeline timing](PIPELINE.md). Native report fields are dimming/title, brightness (`?` when unavailable), source identity, and an optional `active` marker. Only the parser handles this representation; list and graph share the resulting readings. Source identity includes the browser generation so tab changes retain history and create boundaries.

## Invariants

- Unknown brightness is a gap, never an artificial zero.
- Context changes retain history; Freeze retains samples and context.
- Clicking Softlight preserves the selected external application.
- Video response equations and existing 120 ms / 150 ms transition timing are preserved.
- The current-user pipe ACL, 4096-byte message limit and browser bounds checks are retained.
- Existing settings, hotkeys and generated binary names stay compatible.

## Validation

`--self-test` includes deterministic `DiagnosticTests` for report parsing (including Polish culture), malformed data, unavailable values, active selection, context history, retention and Freeze, alongside the existing native response, shader/HDR and settings tests.

`--interface-check` exercises the live panel with an isolated settings file. `--smoke-test` checks actual desktop capture. During this refactor, the expanded panel rendered from the preceding commit and the refactored build produced identical PNG hashes.
