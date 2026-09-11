using System;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Windows;
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
		PasswordBox apiKeyBox =
			userControl.FindName("OpenAiApiKeyBox") as PasswordBox;
		PasswordBox deepSeekApiKeyBox =
			userControl.FindName("DeepSeekApiKeyBox") as PasswordBox;
		RoutedEventHandler passwordHandler = delegate
		{
			if (apiKeyBox != null)
				viewModel.SetPendingOpenAiApiKey(apiKeyBox.Password);
		};
		if (apiKeyBox != null)
			apiKeyBox.PasswordChanged += passwordHandler;
		RoutedEventHandler deepSeekPasswordHandler = delegate
		{
			if (deepSeekApiKeyBox != null)
				viewModel.SetPendingDeepSeekApiKey(deepSeekApiKeyBox.Password);
		};
		if (deepSeekApiKeyBox != null)
			deepSeekApiKeyBox.PasswordChanged += deepSeekPasswordHandler;
		PropertyChangedEventHandler scrollHandler = delegate(object sender, PropertyChangedEventArgs args)
		{
			if (args.PropertyName == "LogText" && logBox != null)
			{
				logBox.Dispatcher.BeginInvoke(new Action(logBox.ScrollToEnd));
			}
			if (args.PropertyName == "AiApiKeyClearRequestVersion" &&
				apiKeyBox != null)
				apiKeyBox.Dispatcher.BeginInvoke(
					new Action(apiKeyBox.Clear));
			if (args.PropertyName == "AiApiKeyClearRequestVersion" &&
				deepSeekApiKeyBox != null)
				deepSeekApiKeyBox.Dispatcher.BeginInvoke(
					new Action(deepSeekApiKeyBox.Clear));
		};
		viewModel.PropertyChanged += scrollHandler;
		userControl.Unloaded += delegate
		{
			viewModel.PropertyChanged -= scrollHandler;
			if (apiKeyBox != null)
				apiKeyBox.PasswordChanged -= passwordHandler;
			if (deepSeekApiKeyBox != null)
				deepSeekApiKeyBox.PasswordChanged -= deepSeekPasswordHandler;
		};
		return userControl;
	}
}
