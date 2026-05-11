using System.Collections.Generic;
using System.Linq;

namespace Issue35378;

public partial class MainPage : ContentPage
{
	public IReadOnlyList<string> Items { get; } =
		Enumerable.Range(1, 50).Select(i => $"Item {i}").ToList();

	public MainPage()
	{
		InitializeComponent();
		BindingContext = this;
	}
}
