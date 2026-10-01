
namespace MagicRemoteService {
	// JSON through the serializer built into the .NET Framework, so the program needs no library next to it
	internal static class Json {
		public static T Deserialize<T>(string strJson) {
			using(System.IO.MemoryStream msJson = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(strJson))) {
				return (T)new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(T)).ReadObject(msJson);
			}
		}
		public static string Serialize<T>(T oValue, bool bIndent = false) {
			using(System.IO.MemoryStream msJson = new System.IO.MemoryStream()) {
				using(System.Xml.XmlDictionaryWriter xdwJson = System.Runtime.Serialization.Json.JsonReaderWriterFactory.CreateJsonWriter(msJson, System.Text.Encoding.UTF8, false, bIndent)) {
					new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(T)).WriteObject(xdwJson, oValue);
				}
				return System.Text.Encoding.UTF8.GetString(msJson.ToArray());
			}
		}
		// Malformed JSON, or a value of an unexpected type
		public static bool IsParseError(System.Exception eException) {
			return eException is System.Runtime.Serialization.SerializationException || eException is System.Xml.XmlException || eException is System.FormatException || eException is System.OverflowException;
		}
	}
}
