# Changelog

## 0.3.0

UnityA11y now lives in its own repository. LethalAccess and Super Auto Pets
Accessibility take it as a git submodule.

### New

- **BepInEx 6 on IL2CPP games.** `il2cpp/UnityA11y.Il2Cpp.csproj` takes
  `-p:Loader=BepInEx` (default `MelonLoader`), which builds against
  `BepInEx\interop` and `BepInEx\core` and defines `BEPINEX`. With `BEPINEX`
  defined, `UGuiSource` uses BepInEx's unprefixed `TMPro` namespace instead of
  MelonLoader's `Il2CppTMPro`. `BepInEx6Entry.cs.template` is a ready entry
  point: a `BasePlugin` that resolves `accesskit.dll` beside the plugin and
  drives the runtime from an injected MonoBehaviour. Tried on Super Auto Pets
  with BepInEx 6.0.0-be.788 (Unity 6000.3).

- **Provider mode.** `A11yRuntime.Create(IFocusOwner, suppressor, registerUGuiSource)`
  publishes the tree and leaves navigation to the host: nothing is suppressed
  and no keys are polled. `ExternalFocusOwner` reports the host's focus and
  passes the screen reader's focus requests back.
- **`EventSystemFocusOwner`.** Provider-mode focus for hosts whose navigation
  moves the uGUI selection. Node ids follow `UGuiSource.StableId`.
- **IL2CPP flavor.** `il2cpp/UnityA11y.Il2Cpp.csproj` builds the same sources
  with `IL2CPP` defined against MelonLoader's interop assemblies; a mod can also
  compile them into its own assembly. Tested on Super Auto Pets with MelonLoader
  0.7.3 and Unity 6000.3.
  - `Compat`: `As<T>` / `Is<T>` use `TryCast` under IL2CPP, where `as` checks
    only the wrapper type and turned every control into a plain button.
    `GetWorldCorners` goes through `Il2CppStructArray`, and `AllSelectables()`
    through `AllSelectablesNoAlloc` into a reused buffer (the array property
    cast to `Selectable[]` gave null, so no controls at all).
  - `A11yRuntime` is a plain class under IL2CPP. The host calls `Tick()` every
    frame and `Dispose()` on quit. The Mono flavor stays a MonoBehaviour.
  - `A11yLabels` holds label and description overrides keyed by GameObject.
    The `A11yLabel` component is Mono only.
  - The change signature and the focused-node refresh run at 4 Hz and 10 Hz
    under IL2CPP, since every interop call allocates.
- **Text fields as a UIA TextPattern.** The input node plus one TextRun child
  holding the characters, with the caret or selection on the input node.
- **`A11yLog.Sink`** for library log lines, replacing `UnityEngine.Debug`, which
  crashes IL2CPP when called from the UIA thread.
- **`native/accesskit.dll`** (accesskit-c 0.22.3) ships in the repository.
- **Diagnostics.** Bridge counters for window messages, `WM_GETOBJECT`,
  activations and updates, and a log line for every published focus change.

### Fixed

| Bug | Fix |
|---|---|
| `PositionInSet` was one too high. AccessKit's position is 0-based and its Windows adapter adds one, so NVDA dropped "n of m" on the last control of every section. | Converted at the FFI boundary. |
| No "of m" on screens without groups. The Windows adapter reads SizeOfSet from the container, never the item. | The screen node, or the flattened group's root in `UGuiSource`, carries the set size. |
| Sliders published no value. | NumericValue with min, max and step from the Slider. |
| NVDA needed an alt-tab before it saw the tree: it asked the window before the adapter attached and kept the "no provider" answer. | After attaching, the bridge raises `EVENT_OBJECT_FOCUS` while the game window is in the foreground. |
| `WM_DESTROY` cleared the saved window procedure and then forwarded to it, as `CallWindowProc(0, ...)`. | Forward from a local copy, or to `DefWindowProc`. |
| The bridge took the active or first visible window of the process, which can be a splash or console window. | Prefers the window with class `UnityWndClass`. |
| TextMesh Pro's zero-width characters (U+200B, U+200C, U+200D, U+2060, U+FEFF) reached labels and values. | Stripped. |
| TextMesh Pro sprite tags were stripped with the rest, so "Give +2<sprite name=Attack>" read "Give +2". | A sprite reads as its name, split at capitals, unless the next word already says it (`UGuiSource.SpritesAsWords`). Hosts map names that are not words through `UGuiSource.SpriteText` ("ArrowRight" to ":"). |
| A `<br>` tag joined the words around it ("perk.<br>Before" read "perk.Before"). | Read as a space. |
| A scroll bar with no text took its label from its sibling, the viewport it scrolls, so it was named after the list's first row; with nothing there, after its object name ("Scrollbar Vertical"), repeating the role. | Scroll bars use their object name without the role words, or their orientation ("Vertical"). |

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
