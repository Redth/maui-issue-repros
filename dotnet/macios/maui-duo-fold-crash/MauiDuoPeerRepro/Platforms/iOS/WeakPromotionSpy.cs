using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MauiDuoPeerRepro;

/// <summary>
/// REPRO_WEAKSPY=1: hooks -retainWeakReference on MAUI's LayoutView / ContentView / PrimaryToolbarItem
/// classes. objc_loadWeakRetained (a weak reference promoted to a strong one, which does not go through
/// -retain) calls it for classes with custom retain/release, which managed NSObject subclasses are. Each
/// successful promotion is a native strong reference the runtime does not know about. The hook only counts
/// and records raw return addresses (it runs under the Objective-C runtime's weak table lock); a background
/// thread symbolizes and prints the most common callers.
/// </summary>
static unsafe class WeakPromotionSpy
{
	[DllImport("/usr/lib/libobjc.dylib")] static extern IntPtr objc_getClass(string name);
	[DllImport("/usr/lib/libobjc.dylib")] static extern IntPtr sel_registerName(string name);
	[DllImport("/usr/lib/libobjc.dylib")] static extern IntPtr class_getMethodImplementation(IntPtr cls, IntPtr sel);
	[DllImport("/usr/lib/libobjc.dylib")] static extern IntPtr class_replaceMethod(IntPtr cls, IntPtr sel, IntPtr imp, string types);
	[DllImport("/usr/lib/libobjc.dylib")] static extern IntPtr object_getClass(IntPtr obj);
	[DllImport("/usr/lib/libobjc.dylib")] static extern IntPtr class_getSuperclass(IntPtr cls);
	[DllImport("/usr/lib/libobjc.dylib")] static extern IntPtr class_getName(IntPtr cls);
	[DllImport("/usr/lib/libSystem.dylib")] static extern int backtrace(IntPtr* buffer, int size);
	[DllImport("/usr/lib/libSystem.dylib")] static extern int dladdr(IntPtr addr, out DlInfo info);

	[StructLayout(LayoutKind.Sequential)]
	struct DlInfo { public IntPtr dli_fname, dli_fbase, dli_sname, dli_saddr; }

	static readonly string[] ClassNames =
	{
		"Microsoft_Maui_Platform_LayoutView",
		"Microsoft_Maui_Platform_ContentView",
		"Microsoft_Maui_Controls_Compatibility_Platform_iOS_ToolbarItemExtensions_PrimaryToolbarItem",
	};

	static readonly Dictionary<IntPtr, (IntPtr Imp, string Name)> _hooked = new();
	const int Frames = 48, Slots = 2048;
	static readonly IntPtr* _ring = (IntPtr*)NativeMemory.AllocZeroed((nuint)(Frames * Slots * IntPtr.Size));
	static readonly IntPtr* _ringClass = (IntPtr*)NativeMemory.AllocZeroed((nuint)(Slots * IntPtr.Size));
	static long _count, _printed;

	public static void Install()
	{
		var sel = sel_registerName("retainWeakReference");
		var hook = (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, byte>)&Hook;
		foreach (var name in ClassNames)
		{
			var cls = objc_getClass(name);
			if (cls == IntPtr.Zero)
			{
				Log($"class {name} not found (not loaded yet?)");
				continue;
			}
			var original = class_getMethodImplementation(cls, sel);
			_hooked[cls] = (original, name);
			class_replaceMethod(cls, sel, hook, "B@:");
			Log($"hooked {name}");
		}
		new Thread(Report) { IsBackground = true }.Start();
	}

	[UnmanagedCallersOnly]
	static byte Hook(IntPtr self, IntPtr sel)
	{
		// Find the original implementation for the hooked class this object belongs to.
		IntPtr imp = IntPtr.Zero, hookedClass = IntPtr.Zero;
		for (var c = object_getClass(self); c != IntPtr.Zero; c = class_getSuperclass(c))
		{
			if (_hooked.TryGetValue(c, out var entry))
			{
				imp = entry.Imp;
				hookedClass = c;
				break;
			}
		}
		var ok = ((delegate* unmanaged<IntPtr, IntPtr, byte>)imp)(self, sel);
		if (ok != 0)
		{
			var n = Interlocked.Increment(ref _count) - 1;
			var slot = (int)(n % Slots);
			backtrace(_ring + slot * Frames, Frames);
			_ringClass[slot] = hookedClass;
		}
		return ok;
	}

	static void Report()
	{
		while (true)
		{
			Thread.Sleep(4000);
			var total = Interlocked.Read(ref _count);
			if (total == _printed)
				continue;
			var from = Math.Max(_printed, total - Slots);
			_printed = total;

			// The first frame outside libobjc / this hook is the code that promoted the weak reference.
			var callers = new Dictionary<string, int>();
			for (var n = from; n < total; n++)
			{
				var slot = (int)(n % Slots);
				var cls = _hooked.TryGetValue(_ringClass[slot], out var e) ? e.Name.Replace("Microsoft_Maui_", "") : "?";
				var chain = new List<string>();
				var seenObjc = false;
				for (var f = 0; f < Frames && chain.Count < 4; f++)
				{
					var addr = _ring[slot * Frames + f];
					if (addr == IntPtr.Zero)
						break;
					if (dladdr(addr, out var info) == 0)
						continue;
					var lib = Path.GetFileName(Marshal.PtrToStringAnsi(info.dli_fname) ?? "?");
					var sym = info.dli_sname == IntPtr.Zero ? "?" : Marshal.PtrToStringAnsi(info.dli_sname) ?? "?";
					// Our hook's own frames come first (interpreter, backtrace); the caller starts after libobjc.
					if (lib.StartsWith("libobjc"))
					{
						seenObjc = true;
						if (chain.Count == 0) chain.Add($"libobjc`{sym}");
						continue;
					}
					if (!seenObjc)
						continue;
					chain.Add($"{lib}`{sym}");
				}
				var key = $"{cls} <- {string.Join(" <- ", chain)}";
				callers[key] = callers.GetValueOrDefault(key) + 1;
			}
			Log($"{total} promotions so far; callers since last report:");
			foreach (var (key, count) in callers.OrderByDescending(kv => kv.Value).Take(8))
				Log($"   {count,5} x {key}");
		}
	}

	static void Log(string message) => Console.WriteLine($"[WEAKSPY] {message}");
}
