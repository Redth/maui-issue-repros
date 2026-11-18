# dotnet/macios — managed peer of an NSObject subclass is finalized while native code holds a promoted weak reference

- **Repo:** [dotnet/macios](https://github.com/dotnet/macios)
- **Issue:** _to be filed_
- **App-level crash this causes in .NET MAUI:** [`../maui-duo-fold-crash`](../maui-duo-fold-crash)

## The bug

A managed `NSObject` subclass (a "user type") is only kept alive while native code owns it if that ownership went
through `-retain`: `xamarin_retain_trampoline` switches the object's GCHandle to strong. A strong reference obtained by
**promoting a weak reference**, `objc_loadWeakRetained` (what ARC and Swift do for `weak` → strong), goes through
`-retainWeakReference` instead. `xamarin_retainWeakReference_trampoline` (`runtime/trampolines.m`) only refuses the
promotion when the wrapper is already in the finalizer queue. As its comment explains, it can't do more under the
Objective-C runtime's locks.

So that native +1 is invisible to the runtime: the managed peer can be collected and finalized while native code
still owns the object and can call into it. The next lookup creates a second wrapper (or fails with "Failed to
marshal … no constructor that takes one NativeHandle argument"), and the original managed state is gone.

This happens on **both Mono (.NET 10) and CoreCLR (.NET 11)**: user types aren't toggle refs on either runtime
(`NSObject.MarkDirty` skips custom types).

## Expected

While native code holds a strong reference, `Runtime.GetNSObject(handle)` returns the same managed instance, however
that reference was obtained.

## Actual

| Native strong reference taken with | Result (3 of 3 rounds, both runtimes) |
|---|---|
| `objc_retain` (control) | same instance, `State = 42` |
| `objc_initWeak` + `objc_loadWeakRetained` | **peer finalized**; `GetNSObject` returns a new `Peer` with `State = 0`, and the runtime logs `Tried to create a managed reference from an object that already has a managed reference (type: WeakPeerTest.Peer)` |

```
[WEAKTEST] runtime: .NET 11.0.0, CoreCLR
[WEAKTEST] round 1 control (objc_retain): native retainCount=2, original peer alive=True, GetNSObject -> Peer #1, State=42, same instance=True => OK
Tried to create a managed reference from an object that already has a managed reference (type: WeakPeerTest.Peer)
[WEAKTEST] round 1 test (objc_loadWeakRetained): native retainCount=1, original peer alive=False, GetNSObject -> Peer #3, State=0, same instance=False => PEER LOST
...
[WEAKTEST] runtime: .NET 10.0.15, Mono   (same results: OK=3 LOST=3 warnings=3)
```

## Why it matters

UIKit promotes weak references to user-type objects constantly. In a .NET MAUI app we hooked `-retainWeakReference` on
MAUI's `LayoutView`, `ContentView` and `PrimaryToolbarItem` and saw thousands of successful promotions:
- from `-[_UIButtonBarButtonVisualProviderIOS tintColor]`, `configureButton:fromBarButtonItem:`, `updateMenu`, … for
  bar button items (about 75 per second even while idle);
- from Swift Observation tracking inside UIKit for views.

After a lost peer, a second problem turns it into wrong-type dispatch:
1. `NativeObjectHasDied(ptr, A)` (`src/ObjCRuntime/Runtime.cs`) only removes the `object_map` entry if it points at the
   original wrapper A or at null. The entry now points at the duplicate B, which is still alive, so it outlives the
   native object.
2. When a new native object is allocated at the same address, `RegisterNSObject(onlyIfNeeded: true)` keeps that live
   stale entry ("Already registered; don't touch the existing entry").
3. Later lookups for the new object return B. In MAUI that crashes a UIKit layout pass with `Object type
   Microsoft.Maui.Platform.ContentView does not match target type Microsoft.Maui.Platform.LayoutView`. See
   [`../maui-duo-fold-crash`](../maui-duo-fold-crash).

(Code references are from tag `dotnet-11.0.1xx-xcode27.2.11039`. Related but different: dotnet/macios#15089 /
dotnet/macios#26994, which only covers code that calls `GC.ReRegisterForFinalize` itself.)

## Repro project

[`WeakPeerTest`](WeakPeerTest) is a plain `dotnet new ios` app (no MAUI) targeting `net11.0-ios` (CoreCLR) and
`net10.0-ios` (Mono). The test is [`WeakRetainTest.cs`](WeakPeerTest/WeakRetainTest.cs), started from
`AppDelegate.FinishedLaunching`. It logs `[WEAKTEST]` lines to the console.

## Build & run

Tested with Xcode 27.2 (27B52), an iOS 27.2 simulator, .NET SDK 11.0.100 (`Microsoft.iOS.Sdk.net11.0_27.2`
27.2.11039) and the .NET 10 iOS packs.

```sh
cd WeakPeerTest
dotnet build -f net11.0-ios -c Debug -r iossimulator-arm64                                    # CoreCLR
dotnet build -f net10.0-ios -c Debug -r iossimulator-arm64 -p:ValidateXcodeVersion=false     # Mono
xcrun simctl install <udid> bin/Debug/<tfm>/iossimulator-arm64/WeakPeerTest.app
xcrun simctl launch --console-pty <udid> com.companyname.WeakPeerTest
```
