# Architecture and refactoring checks

The native refactor was restored on September 6 after the user's comparison found no behavioral difference from the preceding engine. The reported brightness-transition jump has not been attributed to the refactor or confirmed resolved by restoring it.

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
- `DesktopCapture.h`: owns the original frame texture, duplication resource and capture timestamp. Acquisition and release stay paired, including failures.
- `LuminanceSampler.h`: owns the small luminance texture and cached GPU readback; it has no window or response-policy state.
- `GpuPipeline.h`: GPU resources, visible-fragment clipping and separate `DrawOverlay` / `DrawProtected` paths.
- `MonitorRenderer.h`: per-monitor DirectComposition lifecycle and the ordered acquire → analyze → present → publish sequence.
- `BrowserContext.h`: bridge updates, bounded context history and locked frame-time snapshots. Analysis consumes a value snapshot instead of accessing bridge globals.
- `EngineDiagnostics.h` / `DiagnosticState.h`: explicit pixel probes and numerical GPU/response tests, kept outside ordinary frame processing.
- `Engine.cpp`: worker lifecycle, monitor discovery, configuration and exported API adapters.

The window-list report ABI remains compatible. The Firefox protocol additionally carries browser window identity and activation timing; `NfGraphRead` supplies bounded batches of engine-timed graph readings and event markers. See [pipeline timing](PIPELINE.md). Native report fields are dimming/title, brightness (`?` when unavailable), source identity, and an optional `active` marker. Only the parser handles this representation; list and graph share the resulting readings. Source identity includes the browser generation so tab changes retain history and create boundaries.

## Invariants

- Unknown brightness is a gap, never an artificial zero.
- Context changes retain history; Freeze retains samples and context.
- Clicking Softlight preserves the selected external application.
- Video response equations and existing 120 ms / 150 ms transition timing are preserved.
- The current-user pipe ACL, 4096-byte message limit and browser bounds checks are retained.
- Existing settings, hotkeys and generated binary names stay compatible.
- Both presentation paths retain the same shader constant layout, gain equations, frame limits and render ordering. Diagnostics exercise the production paths.
- Closing the panel with × or a standard close request hides it even when pinned. The filter and saved pin preference remain active; the tray/shortcut can reopen it. Tray Exit still terminates the application.
- The two footer checkboxes inherit native CheckBox behavior and accessibility, with a custom high-contrast glyph only.

## Validation

`--self-test` includes deterministic `DiagnosticTests` for report parsing (including Polish culture), malformed data, unavailable values, active selection, context history, retention and Freeze, alongside the existing native response, shader/HDR and settings tests.

`--interface-check` exercises the live panel with an isolated settings file, including hiding a pinned panel while the filter is active and reopening it. `--smoke-test` checks actual desktop capture; `--flash-check` checks actual composition and both rendering modes. Interactive checks must launch with a visible window (`Start-Process -WindowStyle Normal`), otherwise a hidden launch can cause measurements to sample another application.

The native refactor preserves rendering operations and response equations. The intentional visual changes are the close control and checkbox glyphs; `--render-ui output.png --checked` previews both checked footer controls without changing Windows startup registration.

September 6, 2026 validation: numerical GPU/HDR/response/settings checks, active-filter panel closing, desktop smoke, protected-frame composition, and live Firefox integration passed. The Firefox fixture reported Player dimming 69% and dark Page 0%. A short visible-motion comparison on the same SDR desktop averaged 82.0 submitted frames/s before and 80.4 after, with individual runs spanning 74–93; this does not establish a speed improvement. The close-button and checkbox preview was inspected at 250% scaling.
