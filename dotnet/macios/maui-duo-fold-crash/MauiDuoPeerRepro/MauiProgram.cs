namespace MauiDuoPeerRepro;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder.UseMauiApp<App>().UseVirtualListView();
#if IOS
		if (Environment.GetEnvironmentVariable("REPRO_WEAKSPY") == "1")
			WeakPromotionSpy.Install();
		if (Environment.GetEnvironmentVariable("REPRO_WEAKTEST") == "1")
			_ = WeakRetainTest.Run();
		if (Environment.GetEnvironmentVariable("REPRO_NOPROBE") != "1")
			SideInsetProbe.Register();
#endif
		return builder.Build();
	}
}

public class App : Application
{
	protected override Window CreateWindow(IActivationState? activationState)
		=> new(new NavigationPage(new HomePage()));
}
