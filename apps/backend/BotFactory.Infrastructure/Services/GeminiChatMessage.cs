using BotFactory.Core.Entities;

namespace BotFactory.Infrastructure.Services;

public record GeminiChatMessage(MessageRole Role, string Content);
