using System.Runtime.Versioning;
using Microsoft.Maui.Adapters;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Platform;
using UIKit;

namespace MauiDuoPeerRepro;

/// <summary>
/// Two tabs, Overview and Timeline. When the window is wider than tall (the iPhone Duo unfolded), the
/// two tabs' view controllers are moved out of the UITabBarController into a UIArrangementViewController
/// (iOS 27.1) so they show side by side, and Timeline's toolbar item is moved onto Overview so it stays
/// reachable. Folding moves everything back.
/// </summary>
/// <remarks>
/// Switches (environment variables, pass with SIMCTL_CHILD_ prefix to xcrun simctl launch):
/// REPRO_NOSPLIT=1     never move the view controllers (no crash)
/// REPRO_NOREBUILD=1   rows don't rebuild their child views on rebind (no crash)
/// REPRO_NOPROBE=1     no section-inset change on fold (SideInsetProbe off; no crash)
/// REPRO_NOTOOLBAR=1   don't move the toolbar item between tabs
/// REPRO_GC_MS=n       GC.Collect() every n ms on a background thread (frequent collections hide the crash)
/// REPRO_WEAKSPY=1     log which native code promotes weak references to MAUI views / bar items (WeakPromotionSpy)
/// REPRO_WEAKTEST=1    run the MAUI-free weak-reference test at launch (WeakRetainTest)
/// </remarks>
public class HomePage : TabbedPage
{
	readonly ContentPage _overview;
	readonly ContentPage _timeline;
	SplitPresenter? _split;

	// Toolbar items moved from the other tab while split: (owner, index, item).
	readonly List<(ContentPage Owner, int Index, ToolbarItem Item)> _borrowed = new();
	ContentPage? _toolbarHost;

	public HomePage()
	{
		Title = "Home";

		_overview = new ContentPage { Title = "Overview", Content = BuildOverview() };
		_overview.ToolbarItems.Add(new ToolbarItem { Text = "Share", AutomationId = "share" });
		_overview.ToolbarItems.Add(new ToolbarItem { Text = "Settings", AutomationId = "settings" });

		_timeline = new ContentPage { Title = "Timeline", Content = BuildTimeline() };
		_timeline.ToolbarItems.Add(new ToolbarItem { Text = "Filter", AutomationId = "filter" });
		_timeline.ToolbarItems.Add(new ToolbarItem { Text = "Settings", AutomationId = "settings" });

		Children.Add(_overview);
		Children.Add(_timeline);

		SizeChanged += (_, _) => UpdateSplit();

		if (int.TryParse(Environment.GetEnvironmentVariable("REPRO_GC_MS"), out var gcMs) && gcMs > 0)
			new Thread(() => { while (true) { Thread.Sleep(gcMs); GC.Collect(); } }) { IsBackground = true }.Start();
	}

	protected override void OnHandlerChanged()
	{
		base.OnHandlerChanged();
		if (TabController is { } tabs && _split is null)
		{
			_split = new SplitPresenter(tabs);
			Log("attached");
		}
	}

	UITabBarController? TabController => (Handler as IPlatformViewHandler)?.ViewController as UITabBarController;

	void UpdateSplit()
	{
		if (_split is null || TabController is not { } tabs || tabs.View is not { } host || host.Window is null)
			return;

		// Re-parenting mid-transition leaves a half-built split; decide once the transition has finished.
		if (tabs.GetTransitionCoordinator() is { } coordinator)
		{
			coordinator.AnimateAlongsideTransition(_ => { }, _ => Dispatcher.Dispatch(UpdateSplit));
			return;
		}

		var bounds = host.Bounds;
		var split = bounds.Width > bounds.Height && OperatingSystem.IsIOSVersionAtLeast(27, 1) && !Flag("REPRO_NOSPLIT");
		if (split != _split.IsSplit)
			Log($"bounds={bounds} -> {(split ? "split" : "tabs")}");
		_split.SetSplit(split);
		if (!Flag("REPRO_NOTOOLBAR"))
			UpdateToolbarItems();
	}

	// While split, move the other tab's toolbar items onto the current tab (Settings is on both).
	void UpdateToolbarItems()
	{
		var split = _split?.IsSplit == true;
		if (split && _toolbarHost is not null && _toolbarHost == CurrentPage)
			return;

		foreach (var (owner, index, item) in _borrowed.OrderBy(b => b.Index))
		{
			_toolbarHost?.ToolbarItems.Remove(item);
			owner.ToolbarItems.Insert(Math.Min(index, owner.ToolbarItems.Count), item);
		}
		_borrowed.Clear();
		_toolbarHost = null;

		if (!split || CurrentPage is not ContentPage host)
			return;
		_toolbarHost = host;

		foreach (var owner in Children.OfType<ContentPage>().Where(p => p != host))
		{
			var items = owner.ToolbarItems.ToList();
			for (var i = 0; i < items.Count; i++)
				if (!host.ToolbarItems.Any(t => t.AutomationId == items[i].AutomationId))
					_borrowed.Add((owner, i, items[i]));
		}

		foreach (var (owner, _, item) in _borrowed)
		{
			owner.ToolbarItems.Remove(item);
			var settings = host.ToolbarItems.FirstOrDefault(t => t.AutomationId == "settings");
			if (settings is null)
				host.ToolbarItems.Add(item);
			else
				host.ToolbarItems.Insert(host.ToolbarItems.IndexOf(settings), item);
		}
	}

	static View BuildOverview()
	{
		var stack = new VerticalStackLayout { Padding = 12, Spacing = 12 };
		for (var i = 0; i < 24; i++)
			stack.Add(Card($"Reading {i}", $"{i * 3 % 17}.{i % 10} ppm", "Updated today"));
		return new ScrollView { Content = stack };
	}

	static View BuildTimeline() => new VirtualListView
	{
		Adapter = new TimelineAdapter(),
		ItemTemplateSelector = new TimelineTemplateSelector(),
		GlobalHeader = Card("Header", "A global header", "Above every row"),
		IsRefreshEnabled = true,
		SelectionMode = Microsoft.Maui.SelectionMode.Single,
	};

	static View Card(string title, string value, string detail) => new Border
	{
		Padding = 16,
		StrokeShape = new RoundRectangle { CornerRadius = 16 },
		Content = new Grid
		{
			RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto) },
			Children =
			{
				new Label { Text = title, FontSize = 20, FontAttributes = FontAttributes.Bold },
				new Label { Text = value, FontSize = 28 }.Row(1),
				new Label { Text = detail }.Row(2),
			},
		},
	};

	internal static bool Flag(string name) => Environment.GetEnvironmentVariable(name) == "1";

	internal static void Log(string message) => Console.WriteLine($"[REPRO] {message}");
}

static class GridExtensions
{
	public static T Row<T>(this T view, int row) where T : BindableObject { Grid.SetRow(view, row); return view; }
	public static T Column<T>(this T view, int column) where T : BindableObject { Grid.SetColumn(view, column); return view; }
}

/// <summary>Moves the tab controller's two children into a UIArrangementViewController and back.</summary>
sealed class SplitPresenter
{
	readonly UITabBarController _tabs;
	UIViewController[]? _tabViewControllers;
	UIViewController? _container;
	bool _applying;

	public bool IsSplit { get; private set; }

	public SplitPresenter(UITabBarController tabs) => _tabs = tabs;

	public void SetSplit(bool split)
	{
		if (_applying || split == IsSplit || !OperatingSystem.IsIOSVersionAtLeast(27, 1))
			return;
		if (split)
			Enter();
		else
			Exit();
	}

	[SupportedOSPlatform("ios27.1")]
	void Enter()
	{
		var current = _tabs.ViewControllers;
		if (current is null || current.Length < 2)
			return;
		_tabViewControllers = current;

		_applying = true;
		try
		{
			// A view controller has one parent: the tab controller lets go before the arrangement adopts them.
			_tabs.ViewControllers = Array.Empty<UIViewController>();
			var arrangement = new UIArrangementViewController();
			arrangement.SetViewController(current[0], UIArrangementViewControllerViewPlacement.Primary);
			arrangement.SetViewController(current[1], UIArrangementViewControllerViewPlacement.Secondary);
			var layout = UISplitArrangement.Create();
			layout.Axes = UIAxis.Horizontal;
			arrangement.UpdateArrangement(layout);
			_container = arrangement;
			_tabs.ViewControllers = new UIViewController[] { arrangement };
			_tabs.SetTabBarHidden(true, false);
		}
		finally
		{
			_applying = false;
		}
		IsSplit = true;
		HomePage.Log("entered split");
	}

	[SupportedOSPlatform("ios27.1")]
	void Exit()
	{
		_applying = true;
		try
		{
			if (_container is UIArrangementViewController arrangement)
			{
				var panes = arrangement.ChildViewControllers;
				arrangement.SetViewController(null, UIArrangementViewControllerViewPlacement.Primary);
				arrangement.SetViewController(null, UIArrangementViewControllerViewPlacement.Secondary);
				// The arrangement applies those lazily; if that pass runs after the tab controller has adopted
				// the panes again, it takes one away from the tab controller. Flush it and finish detaching here.
				arrangement.View?.LayoutIfNeeded();
				foreach (var pane in panes)
				{
					if (pane.ParentViewController != arrangement)
						continue;
					pane.WillMoveToParentViewController(null);
					pane.View?.RemoveFromSuperview();
					pane.RemoveFromParentViewController();
				}
			}
			_tabs.ViewControllers = _tabViewControllers ?? Array.Empty<UIViewController>();
			_tabs.SetTabBarHidden(false, false);
		}
		finally
		{
			_applying = false;
		}
		_container?.Dispose();
		_container = null;
		IsSplit = false;
		HomePage.Log("exited split");
	}
}

sealed class TimelineAdapter : VirtualListViewAdapterBase<object, string>
{
	public override int GetNumberOfSections() => 1;
	public override object GetSection(int sectionIndex) => "";
	public override int GetNumberOfItemsInSection(int sectionIndex) => 300;
	public override string GetItem(int sectionIndex, int itemIndex) => $"Entry {itemIndex}";
}

sealed class TimelineTemplateSelector : VirtualListViewItemTemplateSelector
{
	readonly DataTemplate _template = new(() => new TimelineRow());

	public override DataTemplate SelectTemplate(object item, int sectionIndex, int itemIndex) => _template;
}

/// <summary>
/// A row that rebuilds its chips and note whenever it is bound, the way a row shows a varying set of facts
/// about its entry: the old child views are thrown away and new ones made.
/// </summary>
sealed class TimelineRow : Microsoft.Maui.Controls.ContentView
{
	readonly Label _title = new() { FontSize = 17, FontAttributes = FontAttributes.Bold };
	readonly FlexLayout _chips = new() { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
	readonly Microsoft.Maui.Controls.ContentView _note = new();

	public TimelineRow()
	{
		Content = new Border
		{
			Margin = new Thickness(12, 6),
			Padding = 12,
			StrokeShape = new RoundRectangle { CornerRadius = 12 },
			Content = new VerticalStackLayout { Spacing = 8, Children = { _title, _chips, _note } },
		};
	}

	protected override void OnBindingContextChanged()
	{
		base.OnBindingContextChanged();
		if (BindingContext is not string text)
			return;
		_title.Text = text;
		if (HomePage.Flag("REPRO_NOREBUILD") && _chips.Children.Count > 0)
			return;

		var n = text.Length;
		_chips.Children.Clear();
		for (var i = 0; i < 2 + n % 4; i++)
			_chips.Children.Add(Chip($"chip {i}", $"{n * i}"));

		_note.Content = n % 2 == 0 ? Quote($"A note about {text}, long enough to wrap onto a second line in a narrow pane.") : null;
	}

	static View Chip(string label, string value) => new Grid
	{
		Margin = new Thickness(0, 0, 6, 6),
		Children =
		{
			new Border { StrokeShape = new RoundRectangle { CornerRadius = 10 }, BackgroundColor = Colors.LightBlue, StrokeThickness = 0 },
			new HorizontalStackLayout { Spacing = 5, Padding = new Thickness(8, 4, 10, 4), Children = { new Label { Text = label }, new Label { Text = value, FontAttributes = FontAttributes.Bold } } },
		},
	};

	static View Quote(string text) => new Grid
	{
		ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) },
		ColumnSpacing = 8,
		Children = { new Border { WidthRequest = 2.5, StrokeThickness = 0, BackgroundColor = Colors.Gray, StrokeShape = new RoundRectangle { CornerRadius = 1.25 } }, new Label { Text = text }.Column(1) },
	};
}
