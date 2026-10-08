namespace TemplateNamespace;

// The SDK default converters plus TemporalConverter1Encoding. Wire it up with:
// DataConverter.Default with { PayloadConverter = new TemporalConverter1() }
// See https://docs.temporal.io/develop/dotnet/best-practices/data-handling/data-conversion.
public sealed class TemporalConverter1 : Temporalio.Converters.DefaultPayloadConverter
{
    public TemporalConverter1()
        : base(
            new Temporalio.Converters.BinaryNullConverter(),
            new Temporalio.Converters.BinaryPlainConverter(),
            new Temporalio.Converters.JsonProtoConverter(),
            new Temporalio.Converters.BinaryProtoConverter(),
            // Converters are tried in order; JsonPlainConverter accepts any value, so stay before it.
            new TemporalConverter1Encoding(),
            new Temporalio.Converters.JsonPlainConverter(new System.Text.Json.JsonSerializerOptions()))
    {
    }

    // Implement one custom wire encoding here.
    public sealed class TemporalConverter1Encoding : Temporalio.Converters.IEncodingConverter
    {
        // Keep this versioned encoding stable for payloads already written.
        public string Encoding => "custom/TemplateNamespace.TemporalConverter1/v1";

        public bool TryToPayload(object? value, out Temporalio.Api.Common.V1.Payload? payload)
        {
            // Serialize supported values and set the payload's "encoding" metadata to Encoding.
            payload = null;
            return false;
        }

        public object? ToValue(Temporalio.Api.Common.V1.Payload payload, System.Type type) =>
            throw new System.NotImplementedException($"Replace with real deserialization logic for {type}.");
    }
}
