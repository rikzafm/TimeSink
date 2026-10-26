# Shared read-only opener replaces the nine copies

Status: ready-for-agent
Spec: ../spec.md

Add one read-only database opener (ReadOnly, Pooling=false, `busy_timeout=2000`). Every `Run(string dbPath, ...)` overload in Today (x2), Week, Month, Year, Wrapped, Matrix, All and AppDetail delegates to it. Public signatures unchanged.

Done when: no query builds its own `SqliteConnectionStringBuilder`; the full test suite is green.
