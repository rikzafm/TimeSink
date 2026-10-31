# Merge across devices in MergePlan and AppLibrary

Status: resolved
Spec: ../spec.md · Plan: ../plan.md (phase 1)
Blocked by: 03

`MergePlan`, `AppLibrary` and `AppCatalogue` learn about `device_id`: the candidates for merging into a Main app include apps of the other device, grouped by device; `canonical_id` may point at an app of another device. Suggest same-name matches across devices (the "Suggested" banner data). Unmerging, retiring and changing the Main app (decision on "Change main…") keep working across devices. Sessions keep their own device.

Done when: tests cover merging a Windows app with a Mac app, unmerging, a Main app on either device, retiring the Main app, and the totals and per-device split after each.
