# Swift usage queries with device scope

Status: resolved
Spec: ../spec.md · Plan: ../plan.md (phase 4)
Blocked by: 06, 18
Needs a Mac.

Port the Today, Week, Month, Year, All, Matrix, Wrapped and App detail queries, `AppGroups`, `MergePlan` and the settings keys. Include `DeviceScope`, union uptime across devices and the per-device figures from ticket 06. Sessions are clipped to DST-aware local days; merged apps are one group under the Main app.

Done when: all query fixtures pass in Swift, including overlap counted once, one device, another time zone and a DST day, and a C#-written database gives identical numbers through both implementations.
