# Header device scope control and device tokens

Status: ready-for-agent
Spec: ../spec.md · Plan: ../plan.md (phase 3)
Blocked by: 05
Design: design/mac/Main.dc.html, design/mac/TodayAll.dc.html (header; see design/mac/README.md)

Add the "This PC | All devices" control to `FlyoutWindow`'s header between the wordmark and the games button (canvas: any tab board). It writes `ui.deviceScope` and applies to every tab (global). Hidden when no other device exists and sync is off. Add device tokens to `pulse-dark` and `pulse-light` (`Brush.DeviceA`, `Brush.DeviceB`) and a shape rule (circle for this device's platform A, square for B) so colour is never the only cue. New string-table keys for every label (Principle 7).

Done when: the control shows and persists the scope, tabs refresh when it changes, the control is hidden with one device, and a screenshot matches the canvas header.
