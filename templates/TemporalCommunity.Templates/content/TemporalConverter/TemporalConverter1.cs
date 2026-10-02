namespace TemplateNamespace;

// The SDK default converters plus TemporalConverter1Encoding. Wire it up with:
// DataConverter.Default with { PayloadConverter = new TemporalConverter1() }
// See https://docs.temporal.io/develop/dotnet/best-practices/data-handling/data-conversion.
public sealed class TemporalConverter1 : global::Temporalio.Converters.DefaultPayloadConverter
{
    public TemporalConverter1()
        : base(
            new global::Temporalio.Converters.BinaryNullConverter(),
            new global::Temporalio.Converters.BinaryPlainConverter(),
            new global::Temporalio.Converters.JsonProtoConverter(),
            new global::Temporalio.Converters.BinaryProtoConverter(),
            // Converters are tried in order; JsonPlainConverter accepts any value, so stay before it.
            new TemporalConverter1Encoding(),
            new global::Temporalio.Converters.JsonPlainConverter(new global::System.Text.Json.JsonSerializerOptions()))
    {
    }

    // Implement one custom wire encoding here.
    public sealed class TemporalConverter1Encoding : global::Temporalio.Converters.IEncodingConverter
    {
        // Keep this versioned encoding stable for payloads already written.
        public string Encoding => "custom/TemplateNamespace.TemporalConverter1/v1";

        public bool TryToPayload(object? value, out global::Temporalio.Api.Common.V1.Payload? payload)
        {
            // Serialize supported values and set the payload's "encoding" metadata to Encoding.
            payload = null;
            return false;
        }

        public object? ToValue(global::Temporalio.Api.Common.V1.Payload payload, global::System.Type type) =>
            throw new global::System.NotImplementedException($"Replace with real deserialization logic for {type}.");
    }
}
