using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace AutoEditing.InferenceMonitor;

public partial class MainWindow : Window
{
	private readonly string sessionsRoot;
	private readonly DispatcherTimer refreshTimer;
	private string? selectedSessionPath;
	private string? selectedExchangePath;

	public MainWindow()
	{
		InitializeComponent();
		sessionsRoot = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
			"AutoEditing", "automation", "sessions");
		RootText.Text = sessionsRoot;
		refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
		refreshTimer.Tick += (_, _) => Refresh();
		Loaded += (_, _) =>
		{
			Refresh();
			refreshTimer.Start();
		};
		Closed += (_, _) => refreshTimer.Stop();
	}

	private void Refresh()
	{
		try
		{
			RefreshSessions();
			RefreshExchanges();
			RefreshConversation();
			ConnectionText.Text = "● Watching live";
		}
		catch (IOException)
		{
			ConnectionText.Text = "Session files are changing…";
		}
		catch (UnauthorizedAccessException)
		{
			ConnectionText.Text = "Waiting for file access…";
		}
	}

	private void RefreshSessions()
	{
		Directory.CreateDirectory(sessionsRoot);
		string? selected = selectedSessionPath;
		SessionItem[] items = Directory.EnumerateDirectories(sessionsRoot)
			.Select(path => new SessionItem(path))
			.OrderByDescending(item => item.LastWriteUtc)
			.ToArray();
		if (!SamePaths(SessionsList.Items.Cast<SessionItem>().Select(item => item.Path), items.Select(item => item.Path)))
		{
			SessionsList.ItemsSource = items;
			SessionsList.SelectedItem = items.FirstOrDefault(item =>
				string.Equals(item.Path, selected, StringComparison.OrdinalIgnoreCase)) ?? items.FirstOrDefault();
		}
	}

	private void RefreshExchanges()
	{
		if (selectedSessionPath == null)
		{
			ExchangesList.ItemsSource = null;
			return;
		}
		string root = Path.Combine(selectedSessionPath, "inference", "exchanges");
		ExchangeItem[] items = Directory.Exists(root)
			? Directory.EnumerateDirectories(root).Select(path => new ExchangeItem(path))
				.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray()
			: Array.Empty<ExchangeItem>();
		if (!SamePaths(ExchangesList.Items.Cast<ExchangeItem>().Select(item => item.Path), items.Select(item => item.Path)))
		{
			string? selected = selectedExchangePath;
			ExchangesList.ItemsSource = items;
			ExchangesList.SelectedItem = items.FirstOrDefault(item =>
				string.Equals(item.Path, selected, StringComparison.OrdinalIgnoreCase)) ?? items.LastOrDefault();
		}
	}

	private void RefreshConversation()
	{
		if (selectedExchangePath == null) return;
		string requestPath = Path.Combine(selectedExchangePath, "request.json");
		if (File.Exists(requestPath))
		{
			using JsonDocument request = ReadJsonShared(requestPath);
			JsonElement root = request.RootElement;
			SystemPromptText.Text = String(root, "SystemPrompt");
			UserPromptText.Text = String(root, "UserPrompt");
			EvidenceText.Text = FormatEvidence(root);
		}

		string finalPath = Path.Combine(selectedExchangePath, "assistant.txt");
		string partialPath = Path.Combine(selectedExchangePath, "assistant.partial.txt");
		bool completed = File.Exists(finalPath);
		string responsePath = completed ? finalPath : partialPath;
		string text = File.Exists(responsePath) ? ReadTextShared(responsePath) : "";
		if (!string.Equals(AssistantText.Text, text, StringComparison.Ordinal))
		{
			AssistantText.Text = text;
			AssistantText.CaretIndex = AssistantText.Text.Length;
			AssistantText.ScrollToEnd();
		}
		bool failed = File.Exists(Path.Combine(selectedExchangePath, "error.json"));
		GeneratingIndicator.Visibility = !completed && !failed
			? Visibility.Visible
			: Visibility.Collapsed;
		ConversationStatus.Text = failed ? "Failed" : completed ? "Completed" : "Receiving streamed response…";
		MetricsText.Text = ReadOptional(Path.Combine(selectedExchangePath, "response.json"));
		ErrorText.Text = ReadOptional(Path.Combine(selectedExchangePath, "error.json"));
		FooterText.Text = $"{Path.GetFileName(selectedSessionPath)}  •  {Path.GetFileName(selectedExchangePath)}  •  " +
			$"{text.Length:N0} generated characters";
	}

	private void SessionsList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		selectedSessionPath = (SessionsList.SelectedItem as SessionItem)?.Path;
		selectedExchangePath = null;
		RefreshExchanges();
	}

	private void ExchangesList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		selectedExchangePath = (ExchangesList.SelectedItem as ExchangeItem)?.Path;
		ConversationTitle.Text = (ExchangesList.SelectedItem as ExchangeItem)?.Display ?? "Select a request";
		RefreshConversation();
	}

	private static JsonDocument ReadJsonShared(string path)
	{
		using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
		return JsonDocument.Parse(stream);
	}

	private static string ReadTextShared(string path)
	{
		using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
		using StreamReader reader = new(stream);
		return reader.ReadToEnd();
	}

	private static string ReadOptional(string path) =>
		File.Exists(path) ? ReadTextShared(path) : "Not available yet.";

	private static string String(JsonElement root, string name) =>
		root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
			? value.GetString() ?? ""
			: "";

	private static string FormatEvidence(JsonElement root)
	{
		if (!root.TryGetProperty("visualEvidence", out JsonElement evidence) ||
			evidence.ValueKind != JsonValueKind.Array ||
			evidence.GetArrayLength() == 0)
			return "No visual evidence.";
		return string.Join(
			Environment.NewLine + Environment.NewLine,
			evidence.EnumerateArray().Select((item, index) =>
				$"{index + 1}. {String(item, "Description")}\n" +
				$"{String(item, "mediaType")} • {Number(item, "encodedCharacters"):N0} encoded characters\n" +
				$"SHA-256 {String(item, "sha256")}"));
	}

	private static long Number(JsonElement root, string name) =>
		root.TryGetProperty(name, out JsonElement value) && value.TryGetInt64(out long number)
			? number
			: 0;

	private static bool SamePaths(IEnumerable<string> left, IEnumerable<string> right) =>
		left.SequenceEqual(right, StringComparer.OrdinalIgnoreCase);

	private sealed class SessionItem
	{
		public SessionItem(string path)
		{
			Path = path;
			LastWriteUtc = Directory.GetLastWriteTimeUtc(path);
		}

		public string Path { get; }
		public DateTime LastWriteUtc { get; }
		public override string ToString() => $"{System.IO.Path.GetFileName(Path)}\n{LastWriteUtc.ToLocalTime():g}";
	}

	private sealed class ExchangeItem
	{
		public ExchangeItem(string path)
		{
			Path = path;
			Name = System.IO.Path.GetFileName(path);
		}

		public string Path { get; }
		public string Name { get; }
		public string Display => Name.Replace('-', ' ');
		public override string ToString() => Display;
	}
}
