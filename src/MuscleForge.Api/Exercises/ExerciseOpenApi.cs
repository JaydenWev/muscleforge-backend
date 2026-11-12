using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using MuscleForge.Api.Contracts;

namespace MuscleForge.Api.Exercises;

public static class ExerciseOpenApi
{
    // Validation runs after trimming and applies defaults to omitted fields. These
    // rules cannot be inferred from IValidatableObject or nullable input types.
    public static void AddExerciseContractMetadata(this OpenApiOptions options)
    {
        options.AddSchemaTransformer((schema, context, _) =>
        {
            var type = context.JsonTypeInfo.Type;
            var input = type == typeof(CreateExerciseRequest) || type == typeof(UpdateExerciseRequest);
            if (input || type == typeof(ExerciseResponse))
            {
                if (schema.Properties is not { } properties) return Task.CompletedTask;
                foreach (var name in new[] { "name", "equipment", "trackingType", "loadConvention" })
                    if (properties[name] is OpenApiSchema field) field.Type = JsonSchemaType.String;
                if (properties["trackingType"] is OpenApiSchema tracking)
                    tracking.Enum = [JsonValue.Create("ExternalWeightAndReps")!];
                if (properties["loadConvention"] is OpenApiSchema load)
                    load.Enum = [JsonValue.Create("TotalLoad")!, JsonValue.Create("PerDumbbell")!];
                foreach (var name in new[] { "name", "equipment" })
                    if (properties[name] is OpenApiSchema field)
                    {
                        field.Description = "Required nonblank text; 1–100 characters after trimming surrounding whitespace.";
                        if (!input) { field.MinLength = 1; field.MaxLength = 100; }
                    }
                if (input)
                {
                    schema.Required?.Remove("trackingType");
                    schema.Required?.Remove("loadConvention");
                    if (properties["trackingType"] is OpenApiSchema trackingDefault)
                        trackingDefault.Default = JsonValue.Create("ExternalWeightAndReps");
                    if (properties["loadConvention"] is OpenApiSchema loadDefault)
                        loadDefault.Default = JsonValue.Create("TotalLoad");
                    if (type == typeof(UpdateExerciseRequest) && properties["isArchived"] is OpenApiSchema archive)
                    {
                        archive.Type = JsonSchemaType.Boolean;
                        archive.Description = "Required. Archiving is permanent; false on an archived exercise returns 409 state_conflict.";
                    }
                }
            }
            if (type == typeof(ExercisePageResponse) && schema.Properties is { } page)
            {
                foreach (var name in new[] { "offset", "limit", "totalCount" })
                    if (page[name] is OpenApiSchema field) Integer(field);
            }
            return Task.CompletedTask;
        });
        options.AddOperationTransformer((operation, context, _) =>
        {
            if (context.Description.RelativePath?.TrimEnd('/') != "api/v1/exercises"
                || context.Description.HttpMethod != "GET") return Task.CompletedTask;
            foreach (var parameter in operation.Parameters ?? [])
            {
                if (parameter.Schema is not OpenApiSchema schema) continue;
                switch (parameter.Name)
                {
                    case "offset":
                        Integer(schema);
                        schema.Minimum = "0";
                        schema.Default = JsonValue.Create(0);
                        break;
                    case "limit":
                        Integer(schema);
                        schema.Minimum = "1";
                        schema.Maximum = "100";
                        schema.Default = JsonValue.Create(50);
                        break;
                    case "includeArchived":
                        schema.Default = JsonValue.Create(false);
                        parameter.Description = "Include archived records for management; normal selection excludes them.";
                        break;
                    case "search":
                        parameter.Description = "Literal case-insensitive substring of name or equipment, at most 100 characters after trimming. Wildcards have no special meaning.";
                        break;
                }
            }
            return Task.CompletedTask;
        });
    }

    private static void Integer(OpenApiSchema schema)
    {
        schema.Type = JsonSchemaType.Integer;
        schema.Format = "int32";
        schema.Pattern = null;
        schema.AnyOf = null;
    }
}
