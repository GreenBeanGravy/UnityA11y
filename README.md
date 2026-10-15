# UnityA11y

Publishes a Unity game's menus to Windows UI Automation, so screen readers such as NVDA can read and handle them natively.

Used by [LethalAccess](https://github.com/GreenBeanGravy/LethalAccess-Dev) (BepInEx, Mono) and [Super Auto Pets Accessibility](https://github.com/GreenBeanGravy/SuperAutoAccessibility) (MelonLoader, IL2CPP).

## Supported setups

| Loader | Runtime | Status |
|---|---|---|
| BepInEx 5 | Mono | Supported |
| BepInEx 6 | IL2CPP | Supported |
| MelonLoader 0.6+ | IL2CPP | Supported |
| BepInEx 6 or MelonLoader | Mono | Untested |
| MelonLoader 0.5 and older | IL2CPP | Not supported |

Requires 64-bit Windows and uGUI menus (Mono games: .NET 4.x). Other UI systems need a custom `IUiSource`.

## Build

```
# Mono
dotnet build UnityA11y.csproj -c Release -p:GameDir="C:\Games\YourGame" -p:GameName="YourGame"

# IL2CPP, MelonLoader
dotnet build il2cpp\UnityA11y.Il2Cpp.csproj -c Release -p:GameDir="C:\Games\YourGame"

# IL2CPP, BepInEx 6
dotnet build il2cpp\UnityA11y.Il2Cpp.csproj -c Release -p:GameDir="C:\Games\YourGame" -p:Loader=BepInEx
```

For IL2CPP, run the game once with the loader first.

Or compile the sources into your mod:

```xml
<ItemGroup>
  <Compile Include="..\UnityA11y\src\UnityA11y.Core\**\*.cs" LinkBase="UnityA11y\Core" />
  <Compile Include="..\UnityA11y\src\UnityA11y.AccessKit\**\*.cs" LinkBase="UnityA11y\AccessKit" />
  <Compile Include="..\UnityA11y\src\UnityA11y.Unity\**\*.cs" LinkBase="UnityA11y\Unity" />
</ItemGroup>
```

Define `IL2CPP` for IL2CPP games, plus `BEPINEX` for BepInEx 6.

Ship `native/accesskit.dll` (accesskit-c 0.22.3, x64) with your mod, next to the game's exe or loaded before the runtime starts.

## Use

Provider mode keeps the game's own navigation and follows its focus. Pass `EventSystemFocusOwner` (uGUI selection) or `ExternalFocusOwner` (your own focus tracking). Takeover mode, `A11yRuntime.Create()`, handles the navigation keys itself.

On Mono the runtime ticks itself. Call `TryAttachBridge()` once a second until `BridgeRunning` is true.

On IL2CPP, drive it from your mod:

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

public override void OnLateUpdate() => _runtime?.FlushFocus();
public override void OnApplicationQuit() => _runtime?.Dispose();
```

Entry point templates for BepInEx 6 and MelonLoader are in `src/UnityA11y.Plugin`.

### Custom labels

Subclass `UGuiSource` and register it:

```csharp
public class MyGameSource : UGuiSource
{
    public override string Name => "My Game";
    public override int Priority => 100;

    protected override string ExtractLabel(Selectable sel)
    {
        var slot = sel.GetComponent<InventorySlot>();
        return slot != null ? slot.Item.Name : base.ExtractLabel(sel);
    }
}

A11yRuntime.Instance.Register(new MyGameSource());
```

For menus that aren't uGUI, implement `IUiSource`.

### Announcements

```csharp
A11yRuntime.Instance.Announce("Quest updated");
A11yRuntime.Instance.Announce("Health critical", assertive: true);
```

## Known gaps

- Grid cells have no row or column info.
- `Announce` (a UIA live region) is unverified.
- IL2CPP is tested only on Super Auto Pets.
