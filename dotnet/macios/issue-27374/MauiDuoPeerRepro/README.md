# dotnet/macios#27374 — .NET MAUI on the iPhone Duo crashes on fold: lost managed peers (LayoutView type mismatch, PrimaryToolbarItem "Failed to marshal")

- **Repo:** [dotnet/macios](https://github.com/dotnet/macios) (root cause); a companion [dotnet/maui](https://github.com/dotnet/maui) issue covers what MAUI does to make it worse
- **Issues:** [dotnet/macios#27374](https://github.com/dotnet/macios/issues/27374) (root cause), [dotnet/maui#40390](https://github.com/dotnet/maui/issues/40390) (MAUI side)
- **Runtime root cause, minimal repro (no MAUI):** [`../WeakPeerTest`](../WeakPeerTest), described in [`../README.md`](../README.md)

## The bug

On iOS (.NET 11 / CoreCLR, MAUI 11.0.10), folding and unfolding an iPhone Duo running this app crashes it every few
fold/unfold cycles (2–3 crashes per 12–15 cycles) in one of two ways:

1. `ObjCRuntime.RuntimeException: An exception occurred while trying to invoke the function System.Void
   Microsoft.Maui.Platform.ContentView.LayoutSubviews (): Object type Microsoft.Maui.Platform.ContentView does not
   match target type Microsoft.Maui.Platform.LayoutView.` This happens during UIKit's layout pass
   (`_UITabBarControllerAdaptiveVisualStyle _verticalBarAvailabilityDidChange` → `layoutBelowIfNeeded`), after
   `Tried to create a managed reference from an object that already has a managed reference (type:
   Microsoft.Maui.Platform.LayoutView)` warnings.
2. `ObjCRuntime.RuntimeException: Failed to marshal the Objective-C object 0x… (type:
   Microsoft_Maui_Controls_Compatibility_Platform_iOS_ToolbarItemExtensions_PrimaryToolbarItem). Could not find an
   existing managed instance for this object, nor was it possible to create a new managed instance`, from
   `UINavigationItem.get_RightBarButtonItems()` in `NavigationHandlerParentingViewController.UpdateToolbarItems()`.

In both, a native object UIKit is still using no longer resolves to its own managed peer.

## Root cause

UIKit owns these objects through promoted weak references (`objc_loadWeakRetained`), which the runtime doesn't see,
so their managed peers get finalized while still in use. See [`../README.md`](../README.md)
for the deterministic runtime repro and the code involved. With `REPRO_WEAKSPY=1` this app logs which UIKit code promotes
weak references to MAUI's views and bar button items:
- `_UIButtonBarButtonVisualProviderIOS` for bar button items;
- Swift Observation tracking for views.

Two MAUI behaviours make it worse:
- `(NativeHandle)` constructors on `LayoutView`/`ContentView` (dotnet/maui#39422) turn a lost peer into a duplicate
  wrapper. The duplicate's stale `object_map` entry later answers for a different view at the same address, which is
  the type mismatch.
- `UpdateToolbarItems()` disposes the navigation item's bar button items while UIKit still holds them.

## Expected

Folding and unfolding never crashes.

## Actual

Crashes, as above. Logs go to `logs/run-N.log`.

## Repro project

This folder is a small code-only MAUI app (MAUI 11.0.10 + Redth.Maui.VirtualListView 0.5.6).
[`HomePage.cs`](HomePage.cs) is a `TabbedPage` inside a `NavigationPage` with two tabs:
- **Overview:** a `ScrollView` of `Border` cards.
- **Timeline:** a `VirtualListView` whose rows **rebuild their child views on every bind**: `FlexLayout.Children.Clear()`,
  new `Grid`/`Border`/`Label` chips, and a new `ContentView.Content`.

Two more pieces make up the trigger:
- [`SideInsetProbe`](Platforms/iOS/SideInsetProbe.cs) sets the list's flow-layout `SectionInset` from
  the side safe areas and invalidates the layout. Folding moves the safe areas, so the visible rows are re-bound.
- When the window is wider than tall (unfolded), the tab controller's two children move into a
  `UIArrangementViewController` (iOS 27.1) side by side, and Timeline's `ToolbarItem` moves onto Overview. Folding moves
  them back.

### Which ingredients are needed

Fold/unfold cycles with `stress.sh` (12 cycles each unless noted):

| Variant | Crashes | "Tried to create a managed reference" warnings |
|---|---|---|
| default (15 cycles) | 3 (2 × PrimaryToolbarItem, 1 × LayoutView) | 78 |
| default | 2 (PrimaryToolbarItem) | 75 |
| `REPRO_NOTOOLBAR=1` (toolbar item not moved) | 2 (LayoutView) | 34 |
| `REPRO_NOSPLIT=1` (view controllers never re-parented) | 0 | 0 |
| `REPRO_NOPROBE=1` (no section-inset change on fold) | 0 | 0 |
| `REPRO_NOREBUILD=1` (rows keep their child views on rebind) | 0 | 0 |
| `REPRO_GC_MS=1000` (`GC.Collect()` every second on a background thread) | 0 | 88 |

An earlier version of the sample used a plain `UIViewController` container (one swap per unfold) instead of
`UIArrangementViewController`; it gave 0 warnings and 0 crashes. Frequent collections don't stop the peers going stale,
but they do stop the crashes: the stale duplicate is collected before a native address is reused.

Switches are environment variables. Pass them as `SIMCTL_CHILD_<NAME>=1` in the environment of `stress.sh` or
`xcrun simctl launch`. `REPRO_WEAKTEST=1` also runs the MAUI-free test from [`../WeakPeerTest`](../WeakPeerTest) at launch.

## Build & run

Needs Xcode 27.2+ with the iOS 27.2 simulator runtime (it includes the iPhone Duo device type), .NET SDK 11.0.100 with
the `maui` workload, and the MAUI CLI for folding the simulator
(`dotnet tool install --global Microsoft.Maui.Cli --prerelease`).

```sh
xcrun simctl create "Duo Repro" com.apple.CoreSimulator.SimDeviceType.iPhone-Duo com.apple.CoreSimulator.SimRuntime.iOS-27-2
xcrun simctl boot <udid>
dotnet build -c Debug -r iossimulator-arm64
xcrun simctl install <udid> bin/Debug/net11.0-ios/iossimulator-arm64/MauiDuoPeerRepro.app
./stress.sh <udid> 15      # folds with `maui devflow device fold set folded|unfolded --experimental`
```

Folding the simulator by hand works too.
