namespace BotFactory.Core.Entities;

public static class BotInteractionModes
{
    public static bool IsValid(bool enableChat, bool enableVoice) => enableChat || enableVoice;
}
