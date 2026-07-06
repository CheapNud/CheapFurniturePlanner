using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CheapFurniturePlanner.Domain.Serialization;

public static class CanonicalJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize<T>(T subject) => JsonSerializer.Serialize(subject, Options);

    public static string Sha256Hex<T>(T subject)
    {
        var bytes = Encoding.UTF8.GetBytes(Serialize(subject));
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }
}
