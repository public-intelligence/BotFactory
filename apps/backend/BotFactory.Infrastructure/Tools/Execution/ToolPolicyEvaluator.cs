using BotFactory.Core.Entities;
using BotFactory.Core.Tools;
using BotFactory.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BotFactory.Infrastructure.Tools.Execution;

/// <summary>Deterministic allow/deny decision. The model is never the authority.</summary>
public record ToolPolicyDecision(bool Allowed, string? ReasonCode = null, string? UserMessage = null)
{
    public static readonly ToolPolicyDecision Allow = new(true);

    public static ToolPolicyDecision Deny(string reasonCode, string userMessage) =>
        new(false, reasonCode, userMessage);
}

/// <summary>
/// Evaluates the tool's structured policy before any external call. Status
/// checks repeat the registry's filter on purpose (defense in depth: config can
/// change mid-conversation). Reason codes are stable machine strings recorded
/// on the execution row; user messages are Spanish and safe to relay.
/// </summary>
public class ToolPolicyEvaluator
{
    private readonly BotFactoryDbContext _db;

    public ToolPolicyEvaluator(BotFactoryDbContext db)
    {
        _db = db;
    }

    public async Task<ToolPolicyDecision> EvaluateAsync(
        ResolvedBotTool tool,
        ToolContext context,
        CancellationToken cancellationToken)
    {
        if (tool.Definition.Status != ToolDefinitionStatus.Published)
        {
            return ToolPolicyDecision.Deny(
                "tool_not_published",
                "Esta consulta no está disponible en este momento.");
        }

        if (tool.Connection.Status != ToolConnectionStatus.Active)
        {
            return ToolPolicyDecision.Deny(
                "connection_disabled",
                "El servicio externo está deshabilitado temporalmente.");
        }

        // v1 is read-only by decision; the consequential/confirmation flow is
        // Phase 4. Publishing consequential tools is blocked at the API too.
        if (tool.Definition.IsConsequential)
        {
            return ToolPolicyDecision.Deny(
                "consequential_not_supported",
                "Esta operación no está habilitada todavía.");
        }

        if (tool.Definition.RequiresAuthenticatedUser
            && string.Equals(
                context.Conversation.AccessMode,
                ConversationAccessModes.Public,
                StringComparison.Ordinal))
        {
            return ToolPolicyDecision.Deny(
                "auth_required",
                "Para realizar esta consulta es necesario iniciar sesión.");
        }

        var cap = tool.Association.MaxCallsPerConversationOverride
            ?? tool.Definition.MaxCallsPerConversation;

        if (cap is > 0)
        {
            // Attempts that reached (or tried to reach) the external system
            // count; validation/policy rejections don't consume the budget.
            var used = await _db.ToolExecutions.CountAsync(
                e => e.ConversationId == context.Conversation.Id
                    && e.ToolDefinitionId == tool.Definition.Id
                    && e.Outcome != ToolExecutionOutcome.ValidationFailed
                    && e.Outcome != ToolExecutionOutcome.PolicyBlocked,
                cancellationToken);

            if (used >= cap.Value)
            {
                return ToolPolicyDecision.Deny(
                    "max_calls_per_conversation",
                    "Se alcanzó el límite de consultas a este servicio en esta conversación.");
            }
        }

        return ToolPolicyDecision.Allow;
    }
}
