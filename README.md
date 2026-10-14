# UnityA11y — a reusable screen reader base for Unity game mods

Exposes a real UI Automation tree from an already-running Unity game, so NVDA
(and JAWS, and Narrator) read it with their own logic, their own verbosity
settings, their own braille output and their own review cursor.

This is the **provider** approach, not the speech-pipe approach. You do not
build utterances. You publish a tree; the screen reader does the rest.

Verified against NVDA **2026.1.1** and `accesskit-c` 0.22.3.

Used by [LethalAccess](https://github.com/GreenBeanGravy/LethalAccess) (BepInEx,
Mono) and [Super Auto Pets Accessibility](https://github.com/GreenBeanGravy/SuperAutoAccessibility)
(MelonLoader, IL2CPP). Both take this repository as a git submodule.

---

## What NVDA does for you, and what it does not

This is the part that trips people up, so it is worth being exact.

**NVDA does not move keyboard focus.** Tab, arrow keys and Enter go straight to
the application. A screen reader observes the focus system; it does not drive
it. If your game has no focus ring, there is nothing for NVDA to announce no
matter how good your tree is. So something has to own one: either this
library's `NavigationController`, or the game or mod that already moves a
selection around (provider mode, see "Takeover or provider" below).

**What you get for free once the tree is correct:**

| Capability | Who implements it |
|---|---|
| Deciding what to say and in what order (name, role, value, states, position) | NVDA |
| Suppressing redundant roles, filtering states, negative states | NVDA |
| Announcing only what changed on a property change | NVDA |
| Braille output | NVDA |
| Object navigation and the review cursor | NVDA |
| All user verbosity settings and speech rate | NVDA |
| Say-all, repeat-last, speech modes | NVDA |
| **Moving focus between controls** | **You** |
| **Activating, incrementing, decrementing controls** | **You** |
| **Deciding what counts as a control at all** | **You** |

**How NVDA's own navigation reaches the game.** Object navigation and the review
cursor explore the tree *without* moving focus. When the user asks NVDA to act
on what they found — move focus to the navigator object, or activate it — NVDA
issues a UIA request, which arrives in `AccessKitBridge.OnAction` as an
`AccessAction`. That is the entire translation layer:

| NVDA command | Arrives as | Handled by |
|---|---|---|
| Move focus to navigator object | `Focus` | `NavigationController.SetFocus` |
| Activate navigator object | `Click` | `IUiSource.PerformAction` |
| Slider increase / decrease | `Increment` / `Decrement` | `IUiSource.PerformAction` |
| Set a value directly | `SetValue` | `IUiSource.PerformAction` |

So yes, easy to translate — but only because you already built the focus ring
those actions target.

---

## Why incremental updates, not whole-tree pushes

v1 pushed the entire tree on every change. That was correct but wrong-headed,
and the AccessKit docs say why:

> an update should only include nodes that are new or changed. AccessKit
> platform adapters will avoid raising extraneous events for nodes that have not
> changed since the previous update, but there is still a cost in processing
> these nodes and replacing the previous instances.

So the *user* hears the right thing either way — the adapter diffs, and no
spurious events are raised. But every full push allocates and replaces every
node. On a 200-control screen that is real work for nothing, every frame a
slider moves.

The reason not to go all the way to naive incremental updates is much sharper.
Sending a node whose parent is neither already in the tree nor in the same
update makes `accesskit_consumer` panic:

> TreeUpdate includes 11 nodes which are neither in the current tree nor a child
> of another node from the update

A Rust panic across FFI **aborts the process**. Godot shipped exactly this
crash. So `TreeDiffer` uses a rule that makes orphans structurally impossible:

```
structural change (ids added/removed, any child list changed, root changed)
    -> send the whole tree; orphans cannot occur

content-only change (a label, a value, a toggle state)
    -> send just the changed nodes; their parents are unchanged, so every
       node involved is already in the tree

anything ambiguous
    -> full send
```

Two further invariants the differ enforces, both otherwise process-abort bugs:

- Focus always resolves to a node that exists in the resulting tree, falling
  back to the root.
- The update factory never returns null, even before any tree is built; the
  degenerate case is a valid one-node tree.

And a cheap win on top: if nothing changed since the previous frame, `Push`
returns without crossing FFI at all.

---

## Architecture

```
                 +------------------------------------------+
                 |             Game process                  |
                 |                                           |
  NVDA --WM_GETOBJECT--> AccessKitBridge --> TreeDiffer       |
                 |        owns adapter + WndProc subclass    |
                 |                  ^                        |
                 |            AccessSnapshot <-- IUiSource    |
                 |                  ^          |- UGuiSource (0)
                 |             A11yRuntime     +- YourGameSource (100)
                 |                  |                        |
  keyboard ----->|      NavigationController --> INavigationSuppressor
                 +------------------------------------------+
```

| Layer | Responsibility | Game-specific? |
|---|---|---|
| `UnityA11y.Core` | Node/snapshot model, content hashing, `IUiSource` | No |
| `UnityA11y.AccessKit` | P/Invoke, adapter lifecycle, WndProc subclass, diffing | No |
| `UnityA11y.Unity` | uGUI scanner, focus owners, runtime driver, IL2CPP shims (`Compat`) | No |
| `UnityA11y.Plugin` | Standalone BepInEx entry point; not compiled by default (see the csproj) | No |
| Your adapter | One `IUiSource` implementation | **Yes** |

---

## Takeover or provider

`A11yRuntime.Create` starts the library one of two ways.

**Takeover**, `A11yRuntime.Create()`: the built-in `NavigationController` polls
the keys, keeps its own focus ring and suppresses the game's navigation. Use it
for a game with no keyboard navigation worth keeping.

**Provider**, `A11yRuntime.Create(focusOwner, null, registerUGuiSource)`: the
library publishes the tree and reports focus, and the host keeps every key it
already handles. Nothing is suppressed and no keys are polled. Two focus owners
come with it:

- `ExternalFocusOwner(readCurrentId, requestFocus)` for a host that tracks focus
  its own way. LethalAccess uses this with its screen registry.
- `EventSystemFocusOwner(resolve)` for a host whose navigation moves the uGUI
  selection. The focused node is `EventSystem.current.currentSelectedGameObject`,
  and the screen reader's "move focus here" selects the object. Super Auto Pets
  Accessibility uses this.

Pass `registerUGuiSource: false` when the host registers its own source with an
authored reading order, so the generic scanner's geometric guess does not compete
with it.

A mod that had its own speech before (Tolk and the like) should hand menu speech
to the screen reader only when three things hold: the bridge is attached, a
screen reader is running, and the tree publishes the control in question. If any
of them fails, keep speaking it the old way. A missing `accesskit.dll` then costs
braille and the screen reader's wording, never silence.

---

## The one non-obvious implementation detail

`accesskit_windows_subclassing_adapter_new` **cannot be used from a mod.** The
header states it panics if the window is already visible, and it must be created
before the window is first shown. A BepInEx plugin's `Awake` runs long after
Unity has created and shown its window.

So the bridge uses `accesskit_windows_adapter_new` and subclasses the WndProc
itself with `SetWindowLongPtr`, forwarding `WM_GETOBJECT` to
`accesskit_windows_adapter_handle_wm_getobject`. Everything else follows.

---

## Setup

### 1. AccessKit

`native/accesskit.dll` is an x64 `accesskit-c` 0.22.3 release build. Ship it
with your mod. It has to be loadable before the first AccessKit call, which
leaves two choices:

- Put it next to the game's exe. Simple, but an updater that only replaces your
  mod's DLL will never install it.
- Keep it in your mod's folder and load it from there before starting the
  runtime. On Mono, `LoadLibrary` with the full path is enough, since the P/Invoke
  then binds to the module already loaded. On CoreCLR (MelonLoader's IL2CPP
  support), use `NativeLibrary.SetDllImportResolver` on the assembly that holds
  the `[DllImport]`s and return the handle from `NativeLibrary.Load`. Super Auto
  Pets Accessibility embeds the DLL as a resource and extracts it with a hash
  check, so its release stays a single file.

### 2. Build

Mono games (BepInEx 5 and the like), `net472`:

```
dotnet build UnityA11y.csproj -c Release -p:GameDir="C:\Games\YourGame" -p:GameName="YourGame"
```

`GameName` is the folder prefix Unity uses, i.e. `YourGame_Data\Managed`.
Referencing the game's own Unity assemblies rather than a stock Unity install
avoids `MissingMethodException` from version drift.

IL2CPP games with MelonLoader, `net6.0`, `IL2CPP` defined:

```
dotnet build il2cpp\UnityA11y.Il2Cpp.csproj -c Release -p:GameDir="C:\Games\YourGame"
```

Run the game once with MelonLoader first, so the interop assemblies exist under
`MelonLoader\Il2CppAssemblies`.

Instead of shipping `UnityA11y.dll`, a mod can compile the sources into its own
assembly. That keeps the mod one DLL, which matters when an updater or installer
only knows about that one file:

```xml
<PropertyGroup>
  <DefineConstants>$(DefineConstants);IL2CPP</DefineConstants>
</PropertyGroup>
<ItemGroup>
  <Compile Include="..\UnityA11y\src\UnityA11y.Core\**\*.cs" LinkBase="UnityA11y\Core" />
  <Compile Include="..\UnityA11y\src\UnityA11y.AccessKit\**\*.cs" LinkBase="UnityA11y\AccessKit" />
  <Compile Include="..\UnityA11y\src\UnityA11y.Unity\**\*.cs" LinkBase="UnityA11y\Unity" />
</ItemGroup>
```

Leave out the `IL2CPP` define for a Mono game.

### 3. Start it

BepInEx (Mono): create the runtime once an EventSystem exists. `Create` tries to
attach to the game window once. A plugin's `Awake` runs before the window is
shown, so call `TryAttachBridge()` every second or so until `BridgeRunning` is
true. On Mono the runtime is a MonoBehaviour and ticks itself.

IL2CPP: the runtime is a plain class, since a MonoBehaviour defined in a mod
would need registering with the IL2CPP class injector. Drive it from the mod:

```csharp
public override void OnUpdate()
{
    if (_runtime == null && EventSystem.current != null)
        _runtime = A11yRuntime.Create(new EventSystemFocusOwner(), null, registerUGuiSource: true);
    if (_runtime == null) return;
    if (!_runtime.BridgeRunning && Time.unscaledTime >= _nextAttach)
    {
        _nextAttach = Time.unscaledTime + 1f;
        _runtime.TryAttachBridge();
    }
    _runtime.Tick();
}

public override void OnApplicationQuit() => _runtime?.Dispose();
```

Set `A11yLog.Sink` if you want the library's log lines in your own log. Do not
point it at `UnityEngine.Debug` under IL2CPP: some lines come from the UIA thread,
and a Unity log call from a thread IL2CPP does not know crashes the process.

Start the screen reader, start the game. You should hear the focused control.

---

## Writing a game adapter

Subclass `UGuiSource` when the game uses uGUI and just needs refinement:

```csharp
public class MyGameSource : UGuiSource
{
    public override string Name => "My Game";
    public override int Priority => 100;

    // Claim only the screens you actually handle; everything else falls
    // through to the generic scanner.
    public override bool IsActive => GameState.Current == Screen.Inventory;

    protected override string ExtractLabel(Selectable sel)
    {
        var slot = sel.GetComponent<InventorySlot>();
        if (slot != null)
            return slot.IsEmpty ? "empty" : $"{slot.Item.Name}, {slot.Count}";
        return base.ExtractLabel(sel);
    }

    protected override AccessRole RoleFor(Selectable sel)
    {
        if (sel.GetComponent<InventorySlot>() != null) return AccessRole.GridCell;
        return base.RoleFor(sel);
    }

    // Group by panel instead of by Canvas.
    protected override object GroupKeyFor(Selectable sel)
        => sel.GetComponentInParent<UiPanel>();
}
```

Register it:

```csharp
A11yRuntime.Instance.Register(new MyGameSource());
```

Implement `IUiSource` from scratch when the game does not use uGUI at all.

### Announcements

```csharp
A11yRuntime.Instance.Announce("Quest updated: find the blacksmith");
A11yRuntime.Instance.Announce("Health critical", assertive: true);
```

Implemented as a UIA live region, which NVDA maps to aria-live politeness.
Rate-limit hard: speech is a strictly serial channel, and two events per second
makes a game unusable.

---

## Suppressing the game's own navigation

Three tiers, in order of effort:

1. **uGUI games** — `EventSystemSuppressor` sets
   `EventSystem.current.sendNavigationEvents = false` and re-asserts it every
   frame. Nothing else needed.

2. **Custom menu controller** — Harmony-patch its input method:

   ```csharp
   [HarmonyPatch(typeof(MenuController), "HandleInput")]
   static bool Prefix() => !HarmonySuppressor.Active;
   ```

3. **Games reading `Input` directly everywhere** — patch `Input.GetKeyDown` /
   `GetAxis` selectively, or implement `INavigationSuppressor` against whatever
   the game responds to. Check tiers 1 and 2 first.

---

## Known gaps

Honest list, roughly in the order I would close them:

- **Grid semantics.** `GridCell` exists but row/column indices are not
  populated, so spatial inventories announce as flat lists.
- **Live regions are implemented but unverified.** `set_live` maps to UIA
  `LiveSetting`, and NVDA reads that
  (`UIAHandler.UIALiveSettingtoNVDAAriaLivePoliteness`), but whether the
  AccessKit Windows adapter raises `LiveRegionChanged` on a node change needs
  confirming with AccEvent on first run. If `Announce` is silent, start there.
- **IL2CPP is tested on one game.** Super Auto Pets (Unity 6000.3, MelonLoader
  0.7.3, CoreCLR). BepInEx 6's IL2CPP support has not been tried.
- **Text fields have no character positions or word starts.** The TextPattern
  works for reading and caret movement; NVDA's word movement and character
  bounding boxes fall back to less precise results.
- **Report hotkeys** for HUD state are not implemented.
- **No config file.** Keybinds are fields on `NavigationController`.
- **32-bit games.** The action-request field offsets were measured on a 64-bit
  build. They should hold on 32-bit Windows too, since the struct's alignment is
  driven by a `u64` and a `double`, but it is unverified.

---

## Testing

- **NVDA Speech Viewer** (NVDA menu → Tools) shows exactly what is spoken. Far
  faster to iterate against than listening.
- **NVDA Python Console** (`NVDA+Ctrl+Z`): inspect `focus`, `focus.parent`,
  `focus.role`, `focus.states`, `focus.positionInfo` to see the tree as NVDA
  sees it, in the same vocabulary as the speech spec.
- **Accessibility Insights for Windows** shows the raw UIA tree, which tells you
  whether a problem is in your tree or in NVDA's reading of it.
- **AccEvent** (Windows SDK) shows which UIA events actually fire — the tool to
  reach for if live regions stay silent.
- **Without a screen reader.** A UIA client in a second process (Python with
  `comtypes` and `UIAutomationCore`, or the Accessibility Insights CLI) can walk
  the game window's tree and read name, role, value, "n of m" and focus, which is
  most of what NVDA would say. In process, reading the runtime's published
  snapshot gives the same answer without UIA in the way.
- `A11yRuntime.Instance.BridgeRunning` tells you whether the adapter attached.
  The bridge's counters (window messages, `WM_GETOBJECT`, activations, updates)
  separate "no messages", "nobody asked", "our answer was rejected" and "nothing
  published", which all sound the same: silence.
  `LastUpdateWasFull` being true every frame means some source is reporting a
  structural change it should not be.
- Run the game **borderless windowed**. Exclusive fullscreen interferes with
  screen readers.
