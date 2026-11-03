using Microsoft.Data.SqlTypes;
using Microsoft.Extensions.AI;

namespace Api.Services;

public class LmStudioEmbeddingService(
    IEmbeddingGenerator<string, Embedding<float>> embedder) : IEmbeddingService
{
    public async Task<SqlVector<float>> GenerateAsync(string text)
    {
        var result = await embedder.GenerateAsync(text);
        return new SqlVector<float>(result.Vector);
    }
}
