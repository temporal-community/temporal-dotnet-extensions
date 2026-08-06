using Temporalio.Api.Common.V1;
using Temporalio.Converters;

namespace Fixtures.HostProject;

// Custom encoding converter. Replace the encoding name, the type check in TryToPayload, and the
// serialization logic in both methods with your own format.
//
// The most surgical way to use this is to compose it into a DefaultPayloadConverter alongside the
// built-in converters (order matters: converters are tried in order when converting to a
// payload, so put custom ones first):
//
//   var payloadConverter = new DefaultPayloadConverter(
//       new CustomPayloadConverterName(),
//       new BinaryNullConverter(),
//       new BinaryPlainConverter(),
//       new JsonProtoConverter(),
//       new BinaryProtoConverter(),
//       new JsonPlainConverter(new System.Text.Json.JsonSerializerOptions()));
//   var dataConverter = DataConverter.Default with { PayloadConverter = payloadConverter };
//   // Set dataConverter on TemporalClientConnectOptions.DataConverter (client) and/or
//   // TemporalWorkerOptions.DataConverter (worker) before connecting/running.
//
// Composing into DefaultPayloadConverter is not the only supported option. The SDK's own
// DefaultPayloadConverter doc comment notes that subclassing DefaultPayloadConverter directly is
// also valid — reach for that instead if you need to change more than a single encoding (e.g.
// reordering or replacing several of the built-in converters at once).
public sealed class CustomPayloadConverterName : IEncodingConverter
{
    public string Encoding => "temporal-payload-converter1";

    public bool TryToPayload(object? value, out Payload? payload)
    {
        // Replace with a real type check and serialization for the value(s) this converter
        // should handle. On a match, set the "encoding" metadata on the payload to the value of
        // Encoding above (e.g. via ByteString.CopyFromUtf8(Encoding), from Google.Protobuf) and
        // return true. Return false (with payload set to null) to let the next converter in the
        // DefaultPayloadConverter chain try instead.
        payload = null;
        return false;
    }

    public object? ToValue(Payload payload, Type type) =>
        // This is only ever called for payloads whose "encoding" metadata matches Encoding
        // above, so it should always know how to convert. Replace with real deserialization
        // logic.
        throw new NotImplementedException($"Replace with real deserialization logic for {type}.");
}
