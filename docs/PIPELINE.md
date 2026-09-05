# Tab switching and pipeline timing

Softlight uses a uniform alpha mask per detected region. Brightness comes from the original captured desktop; Dim is the gain submitted for that measurement. This remains a capture/composition pipeline, so the first visible browser frame can precede the corresponding mask update.

## Synchronization

- Firefox sends a tab activation timestamp, stable browser window ID, generation and known player rectangle together. Previously visited tabs reuse their geometry immediately and request a fresh content-script scan. Unknown geometry is marked pending instead of clearing the player prematurely.
- Late messages from a previously active tab are ignored. Validated browser window IDs remain bound to their native windows, avoiding a title-change race on every switch.
- Context and geometry enter the native engine under one lock. A bounded snapshot history selects metadata appropriate to the captured frame timestamp. Reporting cannot pick up a newer context halfway through processing an older image.
- Brightness reporting uses the measured mean, independently of the response policy's slower reference value.
- The graph reads batches of engine-timed samples retained for ten seconds. Its 33 ms UI timer only redraws the history. Activation markers use the original event time, including when their message arrives after a sample. They do not move to the next UI poll. Missing measurements remain gaps.

## Rendering

Uniform masks are filled as rectangles in reverse Z order using D3D11 ClearView, with the previous shader as a fallback. An unchanged mask is not presented again just because the browser produced another frame. Capture still checks new frames for sudden brightness changes; video response constants and user frequency/speed settings are preserved.

A diagnostic pixel request repaints the next back buffer with the last submitted mask before reading it. Flip-model back buffers can otherwise contain an older image when no redraw was necessary. The desktop smoke test also verifies the actual DWM-composed output, not only this diagnostic buffer.

## Measurements

Measured on September 5, 2026, on one HDR display, with repeated switches between a local dark page and a YouTube gradient video. These are local timings, not general performance guarantees.

| Stage | Before | After initial optimization |
| --- | ---: | ---: |
| GPU readback wait, median | 7.8 ms | 1.7 ms |
| Analysis on frames requiring a mask update, median | 8.8 ms | 3.9 ms |

The transport normally took a few milliseconds. The important correctness issue was that tab context, player geometry, captured image and graph polling used separate update times. The fix addresses that ordering as well as the render cost.

## Optional diagnostics

Set `SOFTLIGHT_TRACE` to a writable CSV path before launching Softlight. On graceful exit, the engine writes at most 32,768 events. Without that environment variable it records nothing. The trace contains times and numeric measurements, without titles, URLs or pixels.

Stages: 1 capture (changed, acquisition wait, source age in ms); 2 selected reading (generation, brightness, dim, player flag); 3 mask submission (analysis and submission time in ms); 4 bridge receive (sender timestamp and visibility); 5 unmatched browser; 6 matched browser; 8 GPU readback wait; 9 activation delivery delay; 10 graph batch read (old/new sequence and payload size). Times in the first CSV column are Unix milliseconds; graph history itself uses the monotonic performance-counter clock.

Regression checks include native/GPU response tests, extension activation/message-order tests, geometry/cadence tests, late graph boundaries, Freeze, desktop occlusion and memory, actual DWM composition, and the live panel.
