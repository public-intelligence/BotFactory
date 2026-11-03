using System.ComponentModel.DataAnnotations;

namespace BotFactory.Core.Auth;

public class GobIdOptions
{
    [Required] public string ApiUrl { get; set; } = "";
    public string Audience { get; set; } = "gobid-users";
    [Required] public string ApplicationId { get; set; } = "";
    [Required] public string AppSecret { get; set; } = "";
    [Required] public string LoginBaseUrl { get; set; } = "";
}
