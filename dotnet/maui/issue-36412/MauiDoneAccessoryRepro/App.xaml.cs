namespace MauiDoneAccessoryRepro;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		// NavigationPage so there are several visible fields and a scrollable page.
		return new Window(new NavigationPage(new MainPage()));
	}
}
