using System;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Windows.Controls;
using System.Windows.Markup;

namespace Core.Scripts;

public static class ShotReviewView
{
	private const string ResourceName = "Core.Scripts.ShotReviewWindow.xaml";

	public static UserControl Create(ShotReviewViewModel viewModel)
	{
		Assembly executingAssembly = Assembly.GetExecutingAssembly();
		using Stream stream = executingAssembly.GetManifestResourceStream("Core.Scripts.ShotReviewWindow.xaml");
		if (stream == null)
		{
			throw new InvalidOperationException("Embedded WPF view was not found: Core.Scripts.ShotReviewWindow.xaml");
		}
		if (!(XamlReader.Load(stream) is UserControl userControl))
		{
			throw new InvalidOperationException("Embedded WPF view did not produce a UserControl.");
		}
		userControl.DataContext = viewModel;
		TextBox logBox = userControl.FindName("LogBox") as TextBox;
		PropertyChangedEventHandler scrollHandler = delegate(object sender, PropertyChangedEventArgs args)
		{
			if (args.PropertyName == "LogText" && logBox != null)
			{
				logBox.Dispatcher.BeginInvoke(new Action(logBox.ScrollToEnd));
			}
		};
		viewModel.PropertyChanged += scrollHandler;
		userControl.Unloaded += delegate
		{
			viewModel.PropertyChanged -= scrollHandler;
		};
		return userControl;
	}
}
