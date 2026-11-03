using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace BotFactory.Api.OpenApi;

/// <summary>
/// Describes application/octet-stream responses as `type: string, format: binary`.
///
/// The blob-streaming endpoints declare .Produces&lt;byte[]&gt;(200, "application/octet-stream"),
/// which the generator renders as format: "byte" — OpenAPI's base64 form. ng-openapi-gen
/// reads that as a string and emits a client returning `string` instead of `Blob`, so the
/// downloads arrive corrupted. "binary" is the raw-bytes form and is what the frontend
/// client needs.
///
/// This lives in code rather than as an edit to BotFactory.Api.json because that file is
/// generated: an earlier hand-patch of exactly these bytes was silently reverted the first
/// time the document was regenerated. CI would not have caught it — the "Check OpenAPI spec
/// is up to date" step runs `git diff` after a build with OpenApiGenerateDocumentsOnBuild=false,
/// so it compares the file against itself.
/// </summary>
public sealed class BinaryResponseTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        foreach (var path in document.Paths.Values)
        {
            foreach (var operation in path.Operations.Values)
            {
                if (operation.Responses is null) continue;

                foreach (var response in operation.Responses.Values)
                {
                    if (response.Content is null) continue;
                    if (!response.Content.TryGetValue("application/octet-stream", out var media)) continue;
                    if (media.Schema is null) continue;

                    // IOpenApiSchema is read-only in Microsoft.OpenApi 2.x; replace
                    // the schema rather than mutating it.
                    media.Schema = new OpenApiSchema
                    {
                        Type = JsonSchemaType.String,
                        Format = "binary",
                    };
                }
            }
        }

        return Task.CompletedTask;
    }
}
