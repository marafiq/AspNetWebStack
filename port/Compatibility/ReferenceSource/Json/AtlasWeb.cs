// English resource values extracted from the pinned Microsoft Reference Source.
namespace System.Web.Script.Serialization {
    internal static class AtlasWeb {
        internal const string JSON_CannotSerializeMemberGeneric = "Cannot serialize member '{0}' on type '{1}'.";
        internal const string JSON_CircularReference = "A circular reference was detected while serializing an object of type '{0}'.";
        internal const string JSON_DepthLimitExceeded = "RecursionLimit exceeded.";
        internal const string JSON_DictionaryTypeNotSupported = "Type '{0}' is not supported for serialization/deserialization of a dictionary, keys must be strings or objects.";
        internal const string JSON_InvalidEnumType = "Enums based on System.Int64 or System.UInt64 are not JSON-serializable because JavaScript does not support the necessary precision.";
        internal const string JSON_InvalidMaxJsonLength = "Value must be a positive integer.";
        internal const string JSON_InvalidRecursionLimit = "RecursionLimit must be a positive integer.";
        internal const string JSON_MaxJsonLengthExceeded = "Error during serialization or deserialization using the JSON JavaScriptSerializer. The length of the string exceeds the value set on the maxJsonLength property.";
    }
}
