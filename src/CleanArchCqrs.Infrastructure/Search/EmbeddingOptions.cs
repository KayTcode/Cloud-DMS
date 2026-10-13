using System.Security.Cryptography;
using System.Text;
namespace CleanArchCqrs.Infrastructure.Search;
public sealed class EmbeddingOptions
{
    public const string PinnedModel = "intfloat/multilingual-e5-small@614241f622f53c4eeff9890bdc4f31cfecc418b3";
    public const int Dimensions = 384;
    public string Endpoint { get; set; } = "http://127.0.0.1:8001/";
    public string ModelVersion { get; set; } = PinnedModel;
    public int TimeoutSeconds { get; set; } = 120;
    public int BatchSize { get; set; } = 16;
    public static void ValidateVector(float[] vector)
    {
        if (vector.Length != Dimensions || vector.Any(v => !float.IsFinite(v))
            || Math.Abs(vector.Sum(v => (double)v * v) - 1) > 0.002)
            throw new InvalidDataException("EMBEDDING_VECTOR_CONTRACT");
    }
}
public sealed class QdrantOptions
{
    public string Endpoint { get; set; } = "http://127.0.0.1:6333/";
    public string? ApiKey { get; set; }
    public int TimeoutSeconds { get; set; } = 120;
    // Reserved application namespace, including retired model collections for exact cleanup.
    public const string CollectionPrefix = "clouddms_e5_";
    public static string Collection(string model) => CollectionPrefix + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(model))).ToLowerInvariant();
}
