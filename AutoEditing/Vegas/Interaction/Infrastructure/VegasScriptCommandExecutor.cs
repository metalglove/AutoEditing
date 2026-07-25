using System;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using ScriptPortal.Vegas;

namespace Core.Scripts;

internal static class VegasScriptCommandExecutor
{
	private sealed class CommandEnvelope
	{
		public string RequestId { get; set; }
		public string CommandType { get; set; }
		public string PayloadJson { get; set; }
		public string Status { get; set; }
		public string ResultJson { get; set; }
		public string Error { get; set; }
	}

	public static void Execute(Vegas vegas, IVegasCommand command)
	{
		Execute<string>(vegas, command);
	}

	public static TResult Execute<TResult>(Vegas vegas, IVegasRequest command)
	{
		if (command == null) throw new ArgumentNullException("command");
		string location = Assembly.GetExecutingAssembly().Location;
		string requestPath = GetRequestPath();
		CommandEnvelope envelope = new CommandEnvelope
		{
			RequestId = Guid.NewGuid().ToString("N"),
			CommandType = command.CommandType,
			PayloadJson = JsonConvert.SerializeObject(command),
			Status = "Pending"
		};
		Write(requestPath, envelope);
		bool completed = false;
		try
		{
			vegas.RunScriptFile(location);
			CommandEnvelope response = Read(requestPath);
			if (response.RequestId != envelope.RequestId)
			{
				throw new InvalidOperationException("VEGAS command response did not match the active request.");
			}
			if (response.Status != "Completed")
			{
				throw new InvalidOperationException("VEGAS command failed. " + (response.Error ?? "No completion status was returned."));
			}
			completed = true;
			return string.IsNullOrEmpty(response.ResultJson) ? default(TResult) : JsonConvert.DeserializeObject<TResult>(response.ResultJson);
		}
		catch (Exception invocationException)
		{
			CommandEnvelope failedResponse = TryRead(requestPath);
			string detail = failedResponse != null && failedResponse.RequestId == envelope.RequestId
				? failedResponse.Error
				: null;
			throw new InvalidOperationException("VEGAS command " + command.CommandType + " failed. " + (detail ?? "The nested script returned no detailed error."), invocationException);
		}
		finally
		{
			if (completed) TryDelete(requestPath);
		}
	}

	public static bool TryExecutePending(Vegas vegas)
	{
		string requestPath = GetRequestPath();
		if (!File.Exists(requestPath)) return false;
		CommandEnvelope envelope = Read(requestPath);
		if (envelope.Status != "Pending") return false;
		envelope.Status = "Running";
		Write(requestPath, envelope);
		try
		{
			IVegasCommandHandler handler = VegasCommandHandlerRegistry.Get(envelope.CommandType);
			envelope.ResultJson = handler.Execute(vegas, envelope.PayloadJson);
			envelope.Status = "Completed";
			envelope.Error = null;
			Write(requestPath, envelope);
			return true;
		}
		catch (Exception exception)
		{
			envelope.Status = "Failed";
			envelope.Error = exception.ToString();
			Write(requestPath, envelope);
			throw;
		}
	}

	private static string GetRequestPath()
	{
		return Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "AutoEditing.vegas-command.json");
	}

	private static CommandEnvelope Read(string path)
	{
		return JsonConvert.DeserializeObject<CommandEnvelope>(File.ReadAllText(path)) ?? throw new InvalidOperationException("Could not read the VEGAS command envelope.");
	}

	private static CommandEnvelope TryRead(string path)
	{
		try { return File.Exists(path) ? Read(path) : null; }
		catch { return null; }
	}

	private static void Write(string path, CommandEnvelope envelope)
	{
		File.WriteAllText(path, JsonConvert.SerializeObject(envelope, Formatting.Indented));
	}

	private static void TryDelete(string path)
	{
		try { File.Delete(path); }
		catch { }
	}
}
