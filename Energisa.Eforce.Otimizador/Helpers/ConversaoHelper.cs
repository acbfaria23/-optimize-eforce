namespace Energisa.Eforce.Otimizador.Helpers;

/// <summary>
/// Funções utilitárias para a camada de conversão E-force ? Google GMPRO (§14).
/// </summary>
public static class ConversaoHelper
{
    private const double RaioTerraKm = 6371.0;

    /// <summary>
    /// Converte um DateTime para o formato ISO 8601 aceito pela API Google (§14.1).
    /// Formato: "yyyy-MM-ddTHH:mm:ssZ"
    /// </summary>
    public static string? FormatToGoogleDate(DateTime? dt)
    {
        if (dt is null)
            return null;

        return dt.Value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
    }

    /// <summary>
    /// Calcula a distância em quilômetros entre dois pares de coordenadas
    /// usando a fórmula de Haversine (§14.2).
    /// </summary>
    public static double Haversine(double lat1, double lon1, double lat2, double lon2)
    {
        var dLat = GrausParaRadianos(lat2 - lat1);
        var dLon = GrausParaRadianos(lon2 - lon1);

        var lat1Rad = GrausParaRadianos(lat1);
        var lat2Rad = GrausParaRadianos(lat2);

        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
              + Math.Cos(lat1Rad) * Math.Cos(lat2Rad)
              * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        var c = 2 * Math.Asin(Math.Sqrt(a));

        return RaioTerraKm * c;
    }

    /// <summary>
    /// Aplica normalização MinMaxScaler sobre um array de valores (§14.3).
    /// Resultado no intervalo [0, 1]. Se Xmax == Xmin, retorna 0.
    /// </summary>
    public static double[] MinMaxScale(double[] values)
    {
        if (values.Length == 0)
            return [];

        var min = values.Min();
        var max = values.Max();
        var range = max - min;

        if (range == 0)
            return new double[values.Length]; // todos zero

        var result = new double[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            result[i] = (values[i] - min) / range;
        }

        return result;
    }

    private static double GrausParaRadianos(double graus) => graus * Math.PI / 180.0;
}
