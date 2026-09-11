namespace AutoEditing.LlmEditor.Planning;

internal static class LlmDecisionSchema
{
	public const string Json = """
	{
	  "type": "object",
	  "additionalProperties": false,
	  "required": ["schemaVersion", "requestId", "placements", "diagnostics"],
	  "properties": {
	    "schemaVersion": { "type": "integer", "const": 1 },
	    "requestId": { "type": "string", "minLength": 1 },
	    "placements": {
	      "type": "array",
	      "minItems": 1,
	      "items": {
	        "type": "object",
	        "additionalProperties": false,
	        "required": ["clipPath", "sourceStartSeconds", "sourceEndSeconds", "speed", "primarySync", "additionalSyncs"],
	        "properties": {
	          "clipPath": { "type": "string", "minLength": 1 },
	          "sourceStartSeconds": { "type": "number", "minimum": 0 },
	          "sourceEndSeconds": { "type": "number", "exclusiveMinimum": 0 },
	          "speed": { "type": "number", "minimum": 0.25, "maximum": 4 },
	          "primarySync": { "$ref": "#/$defs/sync" },
	          "additionalSyncs": {
	            "type": "array",
	            "items": { "$ref": "#/$defs/sync" }
	          }
	        }
	      }
	    },
	    "diagnostics": {
	      "type": "array",
	      "items": {
	        "type": "object",
	        "additionalProperties": false,
	        "required": ["severity", "code", "message"],
	        "properties": {
	          "severity": { "type": "string", "enum": ["Info", "Warning"] },
	          "code": { "type": "string", "minLength": 1 },
	          "message": { "type": "string", "minLength": 1 }
	        }
	      }
	    }
	  },
	  "$defs": {
	    "sync": {
	      "type": "object",
	      "additionalProperties": false,
	      "required": ["killIndex", "musicEventId"],
	      "properties": {
	        "killIndex": { "type": "integer", "minimum": 0 },
	        "musicEventId": { "type": "string", "minLength": 1 }
	      }
	    }
	  }
	}
	""";
}
