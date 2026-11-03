using System.ClientModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Embeddings;
using Api.Data;
using Api.Models;
using Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<TodoDbContext>(opt =>
    opt.UseSqlServer(builder.Configuration.GetConnectionString("TodoDb")));

// LM Studio embedding generator (OpenAI-compatible API)
builder.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(
    new EmbeddingClient(
        "nomic-embed-text",
        new ApiKeyCredential("lm-studio"),
        new OpenAIClientOptions { Endpoint = new Uri("http://localhost:1234/v1") }
    ).AsIEmbeddingGenerator());

builder.Services.AddSingleton<IEmbeddingService, LmStudioEmbeddingService>();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost:4200")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

// Auto-migrate database on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<TodoDbContext>();
    db.Database.Migrate();
}

app.UseCors();

var todosApi = app.MapGroup("/api/todos");

todosApi.MapGet("/", async (TodoDbContext db) =>
    await db.Todos
        .OrderByDescending(t => t.CreatedAt)
        .Select(t => new { t.Id, t.Title, t.IsCompleted, t.CreatedAt })
        .ToListAsync());

todosApi.MapGet("/{id:int}", async (int id, TodoDbContext db) =>
    await db.Todos.FindAsync(id) is Todo todo
        ? Results.Ok(new { todo.Id, todo.Title, todo.IsCompleted, todo.CreatedAt })
        : Results.NotFound());

todosApi.MapPost("/", async (Todo todo, TodoDbContext db, IEmbeddingService embedder) =>
{
    todo.CreatedAt = DateTime.UtcNow;
    todo.Embedding = await embedder.GenerateAsync(todo.Title);
    db.Todos.Add(todo);
    await db.SaveChangesAsync();
    return Results.Created($"/api/todos/{todo.Id}",
        new { todo.Id, todo.Title, todo.IsCompleted, todo.CreatedAt });
});

todosApi.MapPut("/{id:int}", async (int id, Todo input, TodoDbContext db, IEmbeddingService embedder) =>
{
    var todo = await db.Todos.FindAsync(id);
    if (todo is null) return Results.NotFound();

    todo.Title = input.Title;
    todo.IsCompleted = input.IsCompleted;
    todo.Embedding = await embedder.GenerateAsync(todo.Title);
    await db.SaveChangesAsync();
    return Results.Ok(new { todo.Id, todo.Title, todo.IsCompleted, todo.CreatedAt });
});

todosApi.MapPatch("/{id:int}/toggle", async (int id, TodoDbContext db) =>
{
    var todo = await db.Todos.FindAsync(id);
    if (todo is null) return Results.NotFound();

    todo.IsCompleted = !todo.IsCompleted;
    await db.SaveChangesAsync();
    return Results.Ok(new { todo.Id, todo.Title, todo.IsCompleted, todo.CreatedAt });
});

todosApi.MapDelete("/{id:int}", async (int id, TodoDbContext db) =>
{
    var todo = await db.Todos.FindAsync(id);
    if (todo is null) return Results.NotFound();

    db.Todos.Remove(todo);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

todosApi.MapGet("/search", async (string q, TodoDbContext db, IEmbeddingService embedder) =>
{
    var queryVector = await embedder.GenerateAsync(q);

    var results = await db.Todos
        .Select(t => new
        {
            t.Id,
            t.Title,
            t.IsCompleted,
            t.CreatedAt,
            Distance = EF.Functions.VectorDistance("cosine", t.Embedding, queryVector)
        })
        .OrderBy(t => t.Distance)
        .Take(10)
        .ToListAsync();

    return Results.Ok(results);
});

app.Run();
