using System.ComponentModel.DataAnnotations;

namespace BotFactory.Core.Services;

public class BlobStorageOptions
{
    [Required] public string ConnectionString { get; set; } = string.Empty;
    [Required] public string ContainerName { get; set; } = "kb-documents";
}
