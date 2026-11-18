using System.Runtime.InteropServices;
using Foundation;
using ObjCRuntime;

namespace MauiDuoPeerRepro;

/// <summary>
/// REPRO_WEAKTEST=1: no MAUI involved. A managed NSObject subclass is created from managed code, then native
/// code takes a strong reference to it in two ways:
///   control: objc_retain (goes through -retain, which the runtime intercepts)
///   test:    objc_initWeak + objc_loadWeakRetained (a weak reference promoted to strong; does not call -retain)
/// The managed side then drops every reference and collects. While native code still owns the object, the
/// runtime is supposed to keep its managed peer alive: GetNSObject should return the same instance.
/// </summary>
static class WeakRetainTest
{
	[DllImport("/usr/lib/libobjc.dylib")] static extern IntPtr objc_retain(IntPtr o);
	[DllImport("/usr/lib/libobjc.dylib")] static extern void objc_release(IntPtr o);
	[DllImport("/usr/lib/libobjc.dylib")] static extern IntPtr objc_initWeak(IntPtr location, IntPtr o);
	[DllImport("/usr/lib/libobjc.dylib")] static extern IntPtr objc_loadWeakRetained(IntPtr location);
	[DllImport("/usr/lib/libobjc.dylib")] static extern void objc_destroyWeak(IntPtr location);
	[DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")] static extern nuint SendNUInt(IntPtr r, IntPtr sel);

	static readonly IntPtr SelRetainCount = Selector.GetHandle("retainCount");

	public static async Task Run()
	{
		await Task.Delay(1000);
		for (var round = 1; round <= 3; round++)
		{
			Check($"round {round} control (objc_retain)", useWeak: false);
			Check($"round {round} test (objc_loadWeakRetained)", useWeak: true);
		}
		Log("done");
	}

	static void Check(string name, bool useWeak)
	{
		var (handle, weakSlot, weakRef) = Create(useWeak);

		for (var i = 0; i < 3; i++)
		{
			GC.Collect();
			GC.WaitForPendingFinalizers();
		}
		// NSObject_Disposer releases finalized peers on the main thread; let it run.
		NSRunLoop.Current.RunUntil(NSDate.FromTimeIntervalSinceNow(0.3));
		GC.Collect();
		GC.WaitForPendingFinalizers();

		var retainCount = SendNUInt(handle, SelRetainCount);
		var original = weakRef.TryGetTarget(out var o) ? o : null;
		var looked = Runtime.GetNSObject(handle) as Peer;
		var same = original is not null && ReferenceEquals(original, looked);
		Log($"{name}: native retainCount={retainCount}, original peer alive={original is not null}, " +
			$"GetNSObject -> {(looked is null ? "null" : $"Peer #{looked.Id}, State={looked.State}")}, same instance={same} " +
			$"=> {(same && looked!.State == 42 ? "OK" : "PEER LOST")}");

		// Native gives up its reference.
		objc_release(handle);
		if (weakSlot != IntPtr.Zero)
		{
			objc_destroyWeak(weakSlot);
			Marshal.FreeHGlobal(weakSlot);
		}
	}

	// Separate method so no local in the caller keeps the peer reachable.
	[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
	static (IntPtr Handle, IntPtr WeakSlot, WeakReference<Peer> WeakRef) Create(bool useWeak)
	{
		var peer = new Peer { State = 42 };
		var handle = peer.Handle;
		var weakSlot = IntPtr.Zero;
		if (useWeak)
		{
			weakSlot = Marshal.AllocHGlobal(IntPtr.Size);
			Marshal.WriteIntPtr(weakSlot, IntPtr.Zero);
			objc_initWeak(weakSlot, handle);
			var strong = objc_loadWeakRetained(weakSlot);
			if (strong != handle)
				Log("unexpected: loadWeakRetained returned a different object");
		}
		else
		{
			objc_retain(handle);
		}
		return (handle, weakSlot, new WeakReference<Peer>(peer));
	}

	static void Log(string message) => Console.WriteLine($"[WEAKTEST] {message}");
}

class Peer : NSObject
{
	static int _next;
	public readonly int Id = Interlocked.Increment(ref _next);
	public int State;

	public Peer() { }

	// Lets the runtime make a new wrapper for an existing native object (what MAUI's views have since #39422).
	public Peer(NativeHandle handle) : base(handle) { }
}
