# Tab switching and pipeline timing

Softlight applies one uniform gain per detected region. Brightness comes from the original captured desktop; Dim is the gain submitted for that measurement. The default **Flash protection** mode presents captured pixels with that gain already applied. The optional transparent-mask mode applies the same gain over the live desktop.

## Synchronization

- Firefox sends a tab activation timestamp, stable browser window ID, generation and known player rectangle together. Previously visited tabs reuse their geometry immediately and request a fresh content-script scan. Unknown geometry is marked pending instead of clearing the player prematurely.
- Late messages from a previously active tab are ignored. Validated browser window IDs remain bound to their native windows, avoiding a title-change race on every switch.
- Context and geometry enter the native engine under one lock. A bounded snapshot history selects metadata appropriate to the captured frame timestamp. Reporting cannot pick up a newer context halfway through processing an older image.
- Brightness reporting uses the measured mean, independently of the response policy's slower reference value.
- The graph reads batches of engine-timed samples retained for ten seconds. Its 33 ms UI timer only redraws the history. Activation markers use the original event time, including when their message arrives after a sample. They do not move to the next UI poll. Missing measurements remain gaps.

## Rendering

With Flash protection enabled, the existing click-through composition surface displays opaque, processed copies of eligible windows. A new capture is measured before its pixels are submitted. While capture is pending, the previously processed image stays visible: a newer live white frame cannot shine through a previously transparent dark-window mask in an already covered region. HDR uses the original linear scRGB values, preserving extended-range colors and uniform gain. Windows explicitly excluded from capture are not replayed.

This adds capture/presentation latency and makes visible motion depend on the processing frame rate. Save power limits it to 30 fps. It is not a guarantee against every flash: initial coverage, new/uncovered areas, geometry races, device resets and unsupported/protected content remain limitations. DRM content may appear black; disable Flash protection to return to the transparent-mask mode. This is a desktop capture pipeline, not an interception of Firefox's renderer before its first frame.

Rendering subtracts foreground rectangles before drawing each background region, avoiding repeated full-resolution copies of covered windows. Transparent masks use D3D11 ClearView; processed fragments use a scissored GPU pass. The fallback shader preserves the same first-visible-region ordering. A pure mask is not presented again when unchanged; a protected image is refreshed on every new captured frame.

The analysis transfer is a 160×100 single-channel floating-point luminance texture (64 KB instead of 256 KB). Regular response ticks reuse it until a new frame or SDR-white normalization arrives. New frames are always measured, including at low user-selected analysis frequencies. Bright attacks bypass the re-exposure hold; video response constants and user frequency/speed settings are preserved. The device queue is limited to one frame, and single-monitor acquisition waits at most 4 ms.

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

`--flash-check path.txt` deliberately holds a dark captured frame while a real test window turns white, then reads the DWM-composed result. On the development HDR display the mask mode showed 255/255, while frame protection retained 24/255. After capture resumed, the white source was measured as white and the processed output was 63/255 at 95% strength, matching DWM. The check also covers window movement, click-through and returning to masks. This is a controlled stalled-capture test, not a high-speed measurement of all browser transitions.

`--motion-test path.txt protected` exercises animated desktop content. The local full-resolution HDR run submitted approximately 73–86 frames/s after occlusion clipping, versus 68–72 frames/s in the first protected-frame implementation. These submission rates are not measurements of physical display refresh or a guarantee of 120 fps. The corresponding median GPU readback wait fell from 9.1 to 6.7 ms; the mask-only baseline above remains less expensive.
