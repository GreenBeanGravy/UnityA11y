# Changelog

## 0.2.0 — bug-fix and correctness pass

### Behaviour changes

- **Incremental tree updates.** New `TreeDiffer` sends only changed nodes for
  content-only changes, and the whole tree whenever the structure changes.
  Frames with no change do not cross FFI at all. Rationale and the panic-safety
  invariants are in the README.
- **Per-frame refresh of the focused node.** `IUiSource.TryRefreshNode` pulls
  the focused control's current values every frame without rebuilding the tree.
  Previously a slider only updated on the 0.5 s poll, so dragging it announced
  stale values.
- **Live-region announcements.** `A11yRuntime.Announce(message, assertive)` for
  text not attached to the focused control.
- **Container grouping.** The uGUI scanner now emits a `Group` node per Canvas
  (skipped when there is only one), so NVDA announces context on entry and
  "n of m" is scoped to the group rather than the whole screen.

### Bugs fixed

| Severity | Bug |
|---|---|
| **Crash** | The announcer node could be added before any root existed, orphaning it. An orphaned node in a `TreeUpdate` panics `accesskit_consumer`, which aborts the process. Now guarded. |
| **Crash** | Focus could point at a node absent from the tree, which also panics. `TreeDiffer.ResolveFocus` now falls back to the root. |
| **Crash** | The `EnumWindows` callback delegate was created inline and could be collected mid-enumeration. Now held in a local with `GC.KeepAlive`. |
| **Compat** | `Selectable.allSelectablesArray` only exists from Unity 2019.3. Older games throw `MissingMethodException`. Resolved once by reflection with a fallback to `allSelectables`. |
| **Compat** | `ConcurrentQueue` is absent from the .NET 3.5 Mono profile older Unity games use. Replaced with `Queue` behind a lock. |
| **Correctness** | Group node ids started at 2, colliding with the reserved announcer id. Groups now start at 16. |
| **Correctness** | `RectTransformUtility.WorldToScreenPoint` was always passed a null camera, which is only right for Screen Space - Overlay canvases. Now uses the canvas camera for other render modes. |
| **Correctness** | A `CanvasGroup` with `interactable = false` hid its controls entirely. A greyed-out panel should be announced as unavailable, not vanish. Now maps to `Disabled`, and `ignoreParentGroups` is honoured. |
| **Correctness** | `SizeOfSet` was set only on items. AccessKit documents it as a container property while NVDA reads it from the item, so it is now set on both. |
| **Correctness** | Reading order sort had no tie-break, so `List.Sort` being unstable made the order flicker between frames. |
| **Correctness** | `SetWindowLongPtr` returns 0 both on failure and when the previous value was legitimately 0. `SetLastError(0)` is now called first. |
| **Correctness** | Two identical consecutive `Announce` calls produced the same content hash and were silently dropped. Now alternates a trailing space, which is never spoken. |
| **Robustness** | One malformed control could throw and take down the whole tree build. Node construction is now per-control try/catch. |
| **Robustness** | Action-request payload data (`SetValue` numeric and string) was never parsed. Offsets were measured by compiling `accesskit.h` and printing `offsetof` rather than hand-computing alignment. |
| **Robustness** | `Activate` only tried `ISubmitHandler`. Falls back to `pointerClickHandler` for games that only wire up clicks. |
| **Perf** | `NavigationController.PollKey` allocated a `KeyCode[]` every frame. Now cached. |
| **Perf** | `Process.GetCurrentProcess()` was called per window during enumeration. PID is cached at startup. |
| **Perf** | Node bounds are quantised to whole pixels in the content hash, so sub-pixel jitter from animated layouts no longer resends every node every frame. |
| **API** | `LastUpdateNodeCount` / `LastUpdateWasFull` were declared but never assigned. Now wired to the differ. |
| **Cleanup** | String marshalling used out-parameter argument-evaluation ordering that was correct but far too subtle. Replaced with explicit `SetLabel` / `SetValue` helpers. |
| **Cleanup** | TMP dropdown increment/decrement was unreachable through the reflection shim. Added `TmpSupport.TryNudge`. |
| **Cleanup** | TMP password fields leaked their contents into `Value`. Now suppressed, matching the legacy `InputField` path. |

## 0.1.0

Initial base: AccessKit P/Invoke, WndProc subclassing adapter, uGUI scanner,
EventSystem navigation takeover, BepInEx entry point.
