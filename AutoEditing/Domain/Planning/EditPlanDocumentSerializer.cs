using System;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace Core.Domain.Planning;

public static class EditPlanDocumentSerializer
{
	private static readonly JsonSerializerSettings Settings = CreateSettings();

	public static EditPlanningRequest DeserializeRequest(string json)
	{
		EditPlanningRequest request = JsonConvert.DeserializeObject<EditPlanningRequest>(json, Settings);
		EditPlanningRequestValidator.ValidateAndNormalize(request);
		return request;
	}

	public static EditPlanDocument DeserializePlan(string json)
	{
		EditPlanDocument document = JsonConvert.DeserializeObject<EditPlanDocument>(json, Settings);
		EditPlanDocumentValidator.ValidateAndNormalize(document);
		return document;
	}

	public static string SerializePlan(EditPlanDocument document)
	{
		EditPlanDocumentValidator.ValidateAndNormalize(document);
		return JsonConvert.SerializeObject(document, Formatting.Indented, Settings);
	}

	public static string SerializeRequest(EditPlanningRequest request)
	{
		EditPlanningRequestValidator.ValidateAndNormalize(request);
		return JsonConvert.SerializeObject(request, Formatting.Indented, Settings);
	}

	public static EditPlanningRequest ReadRequest(string path)
	{
		return DeserializeRequest(File.ReadAllText(path, Encoding.UTF8));
	}

	public static EditPlanDocument ReadPlan(string path)
	{
		return DeserializePlan(File.ReadAllText(path, Encoding.UTF8));
	}

	public static void WritePlanNew(string path, EditPlanDocument document)
	{
		string fullPath = Path.GetFullPath(path);
		string directory = Path.GetDirectoryName(fullPath);
		if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
			throw new DirectoryNotFoundException("The edit plan output directory does not exist: " + directory);
		if (File.Exists(fullPath))
			throw new IOException("The edit plan output already exists: " + fullPath);

		string temporaryPath = Path.Combine(directory, "." + Path.GetFileName(fullPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");
		try
		{
			byte[] bytes = new UTF8Encoding(false).GetBytes(SerializePlan(document));
			using (FileStream stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
			{
				stream.Write(bytes, 0, bytes.Length);
				stream.Flush();
			}
			File.Move(temporaryPath, fullPath);
		}
		finally
		{
			if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
		}
	}

	private static JsonSerializerSettings CreateSettings()
	{
		JsonSerializerSettings settings = new JsonSerializerSettings
		{
			ContractResolver = new CamelCasePropertyNamesContractResolver(),
			Culture = CultureInfo.InvariantCulture,
			NullValueHandling = NullValueHandling.Include
		};
		settings.Converters.Add(new StringEnumConverter());
		return settings;
	}
}
