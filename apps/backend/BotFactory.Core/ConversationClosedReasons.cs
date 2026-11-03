namespace BotFactory.Core;

public static class ConversationClosedReasons
{
    public const string UserEnded = "user_ended";
    public const string UserAborted = "user_aborted";
    public const string Timeout = "timeout";

    public static bool IsClientReason(string reason) =>
        reason is UserEnded or UserAborted;
}
