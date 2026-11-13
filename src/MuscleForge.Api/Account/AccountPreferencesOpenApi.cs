using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using MuscleForge.Api.Contracts;

namespace MuscleForge.Api.Account;

public static class AccountPreferencesOpenApi
{
    public static void AddAccountPreferencesContractMetadata(this OpenApiOptions options) =>
        options.AddSchemaTransformer((schema, context, _) =>
        {
            if (context.JsonTypeInfo.Type != typeof(UpdateAccountPreferencesRequest) &&
                context.JsonTypeInfo.Type != typeof(AccountPreferencesResponse)) return Task.CompletedTask;
            if (schema.Properties is not { } properties) return Task.CompletedTask;
            if (properties["weightUnit"] is OpenApiSchema unit)
            {
                unit.Type = JsonSchemaType.String;
                unit.Enum = [JsonValue.Create("kg")!, JsonValue.Create("lb")!];
            }
            if (properties["timeZoneId"] is OpenApiSchema zone)
            {
                zone.Type = JsonSchemaType.String;
                zone.MinLength = 1;
                zone.MaxLength = 100;
                zone.Description = "A known IANA time zone identifier.";
            }
            return Task.CompletedTask;
        });
}
