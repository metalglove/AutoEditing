using System;
using AutoEditing.Iteration.Contracts.Automation;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace AutoEditing.Iteration.Contracts.Serialization;

public static class ContractSerializer
{
	private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
	{
		Formatting = Formatting.Indented,
		MissingMemberHandling = MissingMemberHandling.Error,
		NullValueHandling = NullValueHandling.Include,
		DateParseHandling = DateParseHandling.DateTimeOffset,
		Converters = { new StringEnumConverter() }
	};

	public static string Serialize(object value) =>
		JsonConvert.SerializeObject(value ?? throw new ArgumentNullException(nameof(value)), Settings);

	public static T Deserialize<T>(string json)
	{
		T result = JsonConvert.DeserializeObject<T>(json, Settings);
		if (result == null)
			throw new JsonSerializationException("The contract payload was null.");
		return result;
	}

	public static VegasJobEnvelope DeserializeAndValidateEnvelope(string json, DateTimeOffset now)
	{
		VegasJobEnvelope envelope = Deserialize<VegasJobEnvelope>(json);
		VegasContractValidator.Validate(envelope, now);
		return envelope;
	}
}
