using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Microsoft.Data.SqlTypes;

namespace Api.Models;

public class Todo
{
    public int Id { get; set; }
    public required string Title { get; set; }
    public bool IsCompleted { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column(TypeName = "vector(768)")]
    [JsonIgnore]
    public SqlVector<float> Embedding { get; set; }
}
