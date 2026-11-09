#!/usr/bin/env dotnet
#:property PublishAot=false
#:property SelfContained=false
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

if (args.Length != 4 || args[0] != "--revision" || args[2] != "--output" || !Regex.IsMatch(args[1], "^[0-9a-f]{40}$"))
{
    Console.Error.WriteLine("Usage: dotnet run scripts/package-contract.cs -- --revision <40-character lowercase SHA> --output <directory>");
    return 2;
}
var source = Path.Combine(Directory.GetCurrentDirectory(), "contracts", "openapi.json");
var bytes = File.ReadAllBytes(source);
using var document = JsonDocument.Parse(bytes);
var root = document.RootElement;
var versions = root.GetProperty("paths").EnumerateObject().Select(path => Regex.Match(path.Name, @"^/api/(v[0-9]+)(?:/|$)"))
    .Select(match => match.Success ? match.Groups[1].Value : throw new InvalidDataException("API path lacks a version prefix"))
    .Distinct().ToArray();
if (versions.Length != 1) throw new InvalidDataException("Expected exactly one API version");
var documentVersion = root.GetProperty("info").GetProperty("version").GetString();
if (documentVersion is null || !Regex.IsMatch(documentVersion, @"^[A-Za-z0-9][A-Za-z0-9._-]*$")) throw new InvalidDataException("Invalid document version");
var openapiVersion = root.GetProperty("openapi").GetString();
if (openapiVersion is null || !openapiVersion.StartsWith("3.0.")) throw new InvalidDataException("Expected OpenAPI 3.0");
Directory.CreateDirectory(args[3]);
File.WriteAllBytes(Path.Combine(args[3], "openapi.json"), bytes);
var manifest = new { sourceRevision = args[1], apiVersion = versions[0], documentVersion, openapiVersion, sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)) };
File.WriteAllText(Path.Combine(args[3], "manifest.json"), JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }) + "\n");
if (Environment.GetEnvironmentVariable("GITHUB_OUTPUT") is { Length: > 0 } output)
    File.AppendAllText(output, $"api_version={versions[0]}\ndocument_version={documentVersion}\n");
Console.WriteLine(JsonSerializer.Serialize(manifest));
return 0;
