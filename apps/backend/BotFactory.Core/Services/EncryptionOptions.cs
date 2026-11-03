using System.ComponentModel.DataAnnotations;

namespace BotFactory.Core.Services;

public class EncryptionOptions
{
    [Required]
    public string MasterKey { get; set; } = "";
}
