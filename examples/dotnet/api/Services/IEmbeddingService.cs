using Microsoft.Data.SqlTypes;

namespace Api.Services;

public interface IEmbeddingService
{
    Task<SqlVector<float>> GenerateAsync(string text);
}
