namespace AutoEditing.LlmEditor.RoughCut;

internal static class RoughCutAuditSchema
{
	public const string Json = """
	{
	  "type":"object",
	  "additionalProperties":false,
	  "required":["schemaVersion","sessionId","summary","findings","corrections"],
	  "properties":{
	    "schemaVersion":{"type":"integer","const":1},
	    "sessionId":{"type":"string","minLength":1},
	    "summary":{"type":"string","minLength":1},
	    "findings":{"type":"array","items":{
	      "type":"object","additionalProperties":false,
	      "required":["findingId","category","severity","summary","details","startSeconds","endSeconds","affectedCheckpoints","evidenceIds","confidence"],
	      "properties":{
	        "findingId":{"type":"string","minLength":1},
	        "category":{"type":"string","enum":["pacing","continuity","repetition","gap","music_event_coverage","reservation_fulfillment"]},
	        "severity":{"type":"string","enum":["information","warning","error"]},
	        "summary":{"type":"string","minLength":1},
	        "details":{"type":"string","minLength":1},
	        "startSeconds":{"type":["number","null"],"minimum":0},
	        "endSeconds":{"type":["number","null"],"exclusiveMinimum":0},
	        "affectedCheckpoints":{"type":"array","items":{"type":"integer","minimum":1},"uniqueItems":true},
	        "evidenceIds":{"type":"array","minItems":1,"items":{"type":"string","minLength":1},"uniqueItems":true},
	        "confidence":{"type":"number","minimum":0,"maximum":1}
	      }
	    }},
	    "corrections":{"type":"array","items":{
	      "type":"object","additionalProperties":false,
	      "required":["correctionId","findingIds","targetCheckpoints","operationType","instruction","expectedOutcome","risk","confidence"],
	      "properties":{
	        "correctionId":{"type":"string","minLength":1},
	        "findingIds":{"type":"array","minItems":1,"items":{"type":"string","minLength":1},"uniqueItems":true},
	        "targetCheckpoints":{"type":"array","minItems":1,"items":{"type":"integer","minimum":1},"uniqueItems":true},
	        "operationType":{"type":"string","enum":["move","trim","duration","constant_speed"]},
	        "instruction":{"type":"string","minLength":1},
	        "expectedOutcome":{"type":"string","minLength":1},
	        "risk":{"type":"string","minLength":1},
	        "confidence":{"type":"number","minimum":0,"maximum":1}
	      }
	    }}
	  }
	}
	""";
}
