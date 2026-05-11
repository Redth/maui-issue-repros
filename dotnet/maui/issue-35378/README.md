# dotnet/maui#35378 — `CollectionView` first item draws under the navigation bar on iOS

- **Repo:** [dotnet/maui](https://github.com/dotnet/maui)
- **Issue:** [#35378](https://github.com/dotnet/maui/issues/35378)

## The bug

When a `CollectionView` is placed inside a `Layout` whose vertical `SafeAreaEdges` is `None` (i.e. the layout extends behind a `UINavigationBar`), the underlying `UICollectionView` is **not** given a top `contentInset` matching the nav bar height. The first item renders at `y = 0` of the scroll view's bounds — visually clipped under the translucent nav bar — instead of starting below the bar and scrolling under it on user gesture.

Setting `UIScrollView.ContentInsetAdjustmentBehavior` to either `.Automatic` or `.Always` via handler customization (`Handler.PlatformView`) has **no observable effect** — the property write is either ignored or undone by MAUI's mapper later in the pipeline. So neither the default UIKit auto-inset behaviour *nor* the manual override path works.

Scrolling under the nav bar itself works correctly; only the initial content offset is wrong.

## Expected

The first item renders **below** the nav bar at first paint, and scrolls **under** the bar on user gesture (classic UIKit auto-inset-under-nav-bar pattern).

## Actual

The first item renders **at `y = 0` of the scroll view** — the top half is clipped by the translucent nav bar at all times when scrolled to the top.

## Repro project

The repro is a minimal `dotnet new maui` (.NET 10) app with `MainPage` hosted under the default `AppShell` (which produces a `UINavigationBar` on iOS) and a `Grid` with `SafeAreaEdges="Container, None"` wrapping a `CollectionView` of 50 string items.

The relevant file is [`Issue35378/MainPage.xaml`](Issue35378/MainPage.xaml).

### Build & run

```bash
cd Issue35378
dotnet build -f net10.0-ios
dotnet build -f net10.0-ios -t:Run -p:_DeviceName=":v2:udid=<iPhone-sim-udid>"
```

…or open the project in Visual Studio / Rider and run the iOS target on a simulator. A notched iPhone (16/17 Pro, etc.) on iOS 16+ is recommended because the translucent / large-title bar makes the clipping unmistakable.

When the app launches, observe that **"Item 1" is partly hidden under the nav bar**. Expected: "Item 1" should be fully visible just below the nav bar; scrolling up should move it under the bar.

## Environment used to file the original bug

| Item | Version |
|---|---|
| .NET SDK | `10.0.203` |
| MAUI workload | `10.0.20/10.0.100` |
| `Microsoft.Maui.Controls` | `10.0.60` |
| iOS workload | `26.4.10259/10.0.100` |
| Target framework | `net10.0-ios` (also reproduces on `net10.0-maccatalyst` per Apple's nav bar pattern) |
| `SupportedOSPlatformVersion` | `15.0` |
| Simulator | iPhone 17 Pro, iOS 26.4 (`23E244`) |
| Host | macOS 25.4 |
