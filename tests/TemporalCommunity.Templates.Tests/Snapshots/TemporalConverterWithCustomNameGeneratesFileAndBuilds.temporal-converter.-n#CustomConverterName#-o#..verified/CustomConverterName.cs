namespace Fixtures.HostProject;

// The SDK default converters plus CustomConverterNameEncoding. Wire it up with:
// DataConverter.Default with { PayloadConverter = new CustomConverterName() }
// See https://docs.temporal.io/develop/dotnet/best-practices/data-handling/data-conversion.
public sealed class CustomConverterName : Temporalio.Converters.DefaultPayloadConverter
{
    public CustomConverterName()
        : base(
            new Temporalio.Converters.BinaryNullConverter(),
            new Temporalio.Converters.BinaryPlainConverter(),
            new Temporalio.Converters.JsonProtoConverter(),
            new Temporalio.Converters.BinaryProtoConverter(),
            // Converters are tried in order; JsonPlainConverter accepts any value, so stay before it.
            new CustomConverterNameEncoding(),
            new Temporalio.Converters.JsonPlainConverter(new System.Text.Json.JsonSerializerOptions()))
    {
    }

    // Implement one custom wire encoding here.
    public sealed class CustomConverterNameEncoding : Temporalio.Converters.IEncodingConverter
    {
        // Keep this versioned encoding stable for payloads already written.
        public string Encoding => "custom/Fixtures.HostProject.CustomConverterName/v1";

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
