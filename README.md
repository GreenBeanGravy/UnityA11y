# UnityA11y — a reusable screen reader base for Unity game mods

Exposes a real UI Automation tree from an already-running Unity game, so NVDA
(and JAWS, and Narrator) read it with their own logic, their own verbosity
settings, their own braille output and their own review cursor.

This is the **provider** approach, not the speech-pipe approach. You do not
build utterances. You publish a tree; the screen reader does the rest.

Verified against NVDA **2026.1.1** and the current `accesskit-c` API.

---

## What NVDA does for you, and what it does not

This is the part that trips people up, so it is worth being exact.

**NVDA does not move keyboard focus.** Tab, arrow keys and Enter go straight to
the application. A screen reader observes the focus system; it does not drive
it. If your game has no focus ring, there is nothing for NVDA to announce no
matter how good your tree is. That is why `NavigationController` exists and is
not optional.

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
| `UnityA11y.Unity` | uGUI scanner, navigation takeover, runtime driver | No |
| `UnityA11y.Plugin` | BepInEx / MelonLoader entry point | No |
| Your adapter | One `IUiSource` implementation | **Yes** |

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

### 1. Get AccessKit

Download an `accesskit-c` release and copy `accesskit.dll` next to the game
executable. Match the game's architecture — in practice always x64.

### 2. Build

```
dotnet build -c Release -p:GameDir="C:\Games\YourGame" -p:GameName="YourGame"
```

`GameName` is the folder prefix Unity uses, i.e. `YourGame_Data\Managed`.
Referencing the game's own Unity assemblies rather than a stock Unity install
avoids `MissingMethodException` from version drift.

### 3. Install

```
YourGame/
  accesskit.dll                  <- from accesskit-c
  BepInEx/plugins/UnityA11y.dll  <- your build
```

Start NVDA, start the game. You should hear the main menu.

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

- **Text nodes with a caret.** Text inputs expose a value but not a navigable
  text range, so NVDA's review cursor cannot walk inside them.
- **Grid semantics.** `GridCell` exists but row/column indices are not
  populated, so spatial inventories announce as flat lists.
- **Live regions are implemented but unverified.** `set_live` maps to UIA
  `LiveSetting`, and NVDA reads that
  (`UIAHandler.UIALiveSettingtoNVDAAriaLivePoliteness`), but whether the
  AccessKit Windows adapter raises `LiveRegionChanged` on a node change needs
  confirming with AccEvent on first run. If `Announce` is silent, start there.
- **IL2CPP untested.** P/Invoke is fine; the reverse-P/Invoke callbacks are
  already static, which is the main requirement, but they need
  `[MonoPInvokeCallback]` and a real test.
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
- `A11yRuntime.Instance.BridgeRunning` tells you whether the adapter attached.
  `LastUpdateWasFull` being true every frame means some source is reporting a
  structural change it should not be.
- Run the game **borderless windowed**. Exclusive fullscreen interferes with
  screen readers.
