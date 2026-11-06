#!/usr/bin/env python3
"""Export a stable, inline OpenAPI snapshot from the built API. Requires Python 3.11+."""
import argparse
import json
import os
from pathlib import Path
import subprocess
import tempfile
import time
import urllib.error
import urllib.request

ROOT = Path(__file__).resolve().parents[1]
SCHEMA_KEYS = {"type", "format", "properties", "required", "items", "enum", "nullable",
               "additionalProperties", "minLength", "maxLength", "minimum", "maximum",
               "allOf", "anyOf", "oneOf", "pattern", "default"}


def normalize(document):
    """Inline schema references; exclude host URLs and documentation-only metadata."""
    security_schemes = document.get("components", {}).get("securitySchemes", {})

    def schema(value, ancestors=()):
        if isinstance(value, bool):
            return value
        if "$ref" in value:
            reference = value["$ref"]
            if reference in ancestors or not reference.startswith("#/"):
                raise ValueError(f"Unsupported recursive or external schema: {reference}")
            target = document
            for part in reference[2:].split("/"):
                target = target[part.replace("~1", "/").replace("~0", "~")]
            return schema(target, (*ancestors, reference))
        result = {}
        for key, item in value.items():
            if key not in SCHEMA_KEYS:
                if key in {"$schema", "title", "description", "example", "examples", "deprecated"}:
                    continue
                raise ValueError(f"Add support for schema keyword {key!r} before exporting")
            if key == "properties":
                result[key] = {name: schema(prop, ancestors) for name, prop in item.items()}
            elif key in {"items", "additionalProperties"}:
                result[key] = schema(item, ancestors)
            elif key in {"anyOf", "oneOf", "allOf"}:
                result[key] = [schema(prop, ancestors) for prop in item]
            else:
                result[key] = sorted(item) if key == "required" else item
        return result

    paths = {}
    for path, item in document["paths"].items():
        operations = {}
        for method, operation in item.items():
            if method not in {"get", "post", "put", "delete", "patch", "head", "options"}:
                raise ValueError(f"Unsupported path metadata: {method}")
            result = {key: operation[key] for key in ("operationId", "security") if key in operation}
            if "parameters" in operation:
                raise ValueError("Extend exporter parameter handling before adding parameterized endpoints")
            if "requestBody" in operation:
                body = operation["requestBody"]
                result["requestBody"] = {"required": body.get("required", False), "content": {
                    mime: {"schema": schema(value["schema"])} for mime, value in body["content"].items()}}
            responses = {}
            for code, response in operation["responses"].items():
                entry = {"description": f"HTTP {code}"}
                if "content" in response:
                    entry["content"] = {mime: {"schema": schema(body["schema"])}
                                        for mime, body in response["content"].items()}
                responses[code] = entry
            result["responses"] = responses
            operations[method] = result
        paths[path] = operations
    result = {"openapi": "3.0.3", "info": {key: document["info"][key] for key in ("title", "version")}, "paths": paths}
    if security_schemes:
        result["components"] = {"securitySchemes": security_schemes}
    if "security" in document:
        result["security"] = document["security"]
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true")
    parser.add_argument("--port", type=int, default=5188)
    args = parser.parse_args()
    env = dict(os.environ, ASPNETCORE_ENVIRONMENT="Development", DOTNET_ENVIRONMENT="Development")
    # Contract export must not provision an owner from developer configuration.
    for key in list(env):
        if key.replace("__", ":").lower() in {"owner:email", "owner:password", "dataprotection:keyspath", "dataprotection:certificatepath", "dataprotection:certificatepassword"}:
            del env[key]
    env["Owner__Email"] = ""
    env["Owner__Password"] = ""
    env["DataProtection__KeysPath"] = ""
    env["DataProtection__CertificatePath"] = ""
    env["DataProtection__CertificatePassword"] = ""
    env.setdefault("ConnectionStrings__Database", "Host=localhost;Database=muscleforge;Username=muscleforge")
    address = f"http://127.0.0.1:{args.port}"
    with tempfile.TemporaryFile() as log:
        process = subprocess.Popen([
            "dotnet", "run", "--project", "src/MuscleForge.Api", "--configuration", "Release",
            "--no-build", "--no-launch-profile", "--", "--urls", address
        ], cwd=ROOT, env=env, stdout=log, stderr=log)
        try:
            for _ in range(100):
                if process.poll() is not None:
                    log.seek(0)
                    raise RuntimeError(log.read().decode())
                try:
                    with urllib.request.urlopen(f"{address}/openapi/v1.json", timeout=2) as response:
                        document = json.load(response)
                    break
                except urllib.error.URLError:
                    time.sleep(0.2)
            else:
                raise RuntimeError("API did not expose OpenAPI within 20 seconds")
            output = json.dumps(normalize(document), indent=2, sort_keys=True) + "\n"
            target = ROOT / "contracts/openapi.json"
            if args.check:
                if target.read_text() != output:
                    raise SystemExit("OpenAPI drift: run python3 scripts/export-openapi.py and review the diff")
                print("OpenAPI matches the committed contract")
            else:
                target.parent.mkdir(exist_ok=True)
                target.write_text(output)
                print(f"Exported {target}")
        finally:
            if process.poll() is None:
                process.terminate()
                try:
                    process.wait(timeout=5)
                except subprocess.TimeoutExpired:
                    process.kill()
                    process.wait()


if __name__ == "__main__":
    main()
