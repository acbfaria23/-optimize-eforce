using Google.Maps.RouteOptimization.V1;
using Google.Protobuf;

namespace Energisa.Eforce.Otimizador.Services;

public static class ResultDeserializer
{
    private static readonly JsonParser _jsonParser = new JsonParser(JsonParser.Settings.Default.WithIgnoreUnknownFields(true));

    public static OptimizeToursResponse DeserializeFromJson(string json)
    {
        return _jsonParser.Parse<OptimizeToursResponse>(json);
    }
}
