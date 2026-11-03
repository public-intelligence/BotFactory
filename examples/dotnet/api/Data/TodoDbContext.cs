using Microsoft.EntityFrameworkCore;
using Api.Models;

namespace Api.Data;

public class TodoDbContext(DbContextOptions<TodoDbContext> options) : DbContext(options)
{
    public DbSet<Todo> Todos => Set<Todo>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Todo>()
            .Property(t => t.Embedding)
            .HasColumnType("vector(768)");
    }
}
