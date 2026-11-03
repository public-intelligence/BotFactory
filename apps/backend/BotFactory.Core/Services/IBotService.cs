using BotFactory.Core.Entities;

namespace BotFactory.Core.Services;

public interface IBotService
{
    Task<(List<Bot> Items, int TotalCount)> GetAllAsync(int page = 1, int pageSize = 10, bool publishedOnly = false);
    Task<Bot?> GetByIdAsync(int id, bool publishedOnly = false);
    Task<Bot?> GetBySlugAsync(string slug, bool publishedOnly = false);
    Task<Bot?> GetByPublicIdAsync(string publicId, bool publishedOnly = false);
    Task<Bot> CreateAsync(Bot bot);
    Task<Bot?> UpdateAsync(int id, Bot bot);
    Task<bool> DeleteAsync(int id);
    Task<Bot?> SetPublishedStateAsync(int id, string publishedState);
}
