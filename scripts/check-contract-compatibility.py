#!/usr/bin/env python3
"""Compare a canonical OpenAPI contract against a provisional baseline."""
import argparse
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SCHEMA_KEYS = {'type', 'format', 'properties', 'required', 'items', 'enum', 'nullable',
               'additionalProperties', 'minLength', 'maxLength', 'minimum', 'maximum',
               'allOf', 'anyOf', 'oneOf', 'pattern', 'default'}


def keys(value, allowed, where):
    if not isinstance(value, dict) or set(value) - allowed:
        raise ValueError(f'{where}: unsupported structure or keys {set(value) - allowed if isinstance(value, dict) else value!r}')


def validate_schema(s, where):
    if isinstance(s, bool):
        return
    keys(s, SCHEMA_KEYS, where)
    for name, sub in s.get('properties', {}).items():
        validate_schema(sub, f'{where}.{name}')
    for key in ('items', 'additionalProperties'):
        if key in s:
            validate_schema(s[key], f'{where}.{key}')
    for key in ('allOf', 'anyOf', 'oneOf'):
        for sub in s.get(key, []):
            validate_schema(sub, f'{where}.{key}')


def validate(doc):
    keys(doc, {'openapi', 'info', 'paths', 'components', 'security'}, 'document')
    if doc.get('openapi') != '3.0.3':
        raise ValueError('Only canonical OpenAPI 3.0.3 is supported')
    keys(doc['info'], {'title', 'version'}, 'info')
    keys(doc.get('components', {}), {'securitySchemes'}, 'components')
    schemes = doc.get('components', {}).get('securitySchemes', {})
    for name, scheme in schemes.items():
        if scheme != {'type': 'http', 'scheme': 'bearer'}:
            raise ValueError(f'Unsupported security scheme {name}')
    def validate_security(value):
        if not isinstance(value, list):
            raise ValueError('Invalid security requirements')
        for option in value:
            if not isinstance(option, dict) or any(name not in schemes or scopes != [] for name, scopes in option.items()):
                raise ValueError('Only registered bearer security requirements without scopes are supported')
    validate_security(doc.get('security', []))
    for path, operations in doc['paths'].items():
        for method, op in operations.items():
            if method not in {'get', 'post', 'put', 'patch', 'delete', 'head', 'options'}:
                raise ValueError(f'{path}: unsupported method {method}')
            keys(op, {'operationId', 'security', 'parameters', 'requestBody', 'responses'}, path)
            validate_security(op.get('security', doc.get('security', [])))
            seen = set()
            for p in op.get('parameters', []):
                keys(p, {'name', 'in', 'required', 'style', 'explode', 'schema'}, path)
                identity = (p['in'], p['name'])
                if p['in'] not in ('path', 'query') or identity in seen or (p['in'] == 'path' and not p.get('required')):
                    raise ValueError(f'{path}: invalid or unsupported parameter {identity}')
                seen.add(identity)
                validate_schema(p['schema'], path)
            def check_content(container):
                for mime, media in container.get('content', {}).items():
                    keys(media, {'schema'}, mime)
                    validate_schema(media['schema'], mime)
            if 'requestBody' in op:
                keys(op['requestBody'], {'required', 'content'}, path)
                check_content(op['requestBody'])
            for code, response in op['responses'].items():
                keys(response, {'description', 'content', 'headers'}, code)
                check_content(response)
                for name, header in response.get('headers', {}).items():
                    if name.lower() not in ('retry-after', 'www-authenticate'):
                        raise ValueError(f'Unsupported header {name}')
                    keys(header, {'required', 'schema', 'style', 'explode'}, name)
                    if 'schema' in header:
                        validate_schema(header['schema'], name)


def compare_schema(old, new, direction, where, errors):
    def fail(message): errors.append(f'{where}: {message}')
    if isinstance(old, bool) or isinstance(new, bool):
        if old != new: fail('boolean schema changed')
        return
    # Composition and arbitrary regex/default changes cannot be safely ordered.
    for key in ('allOf', 'anyOf', 'oneOf', 'pattern', 'default', 'additionalProperties'):
        if old.get(key) != new.get(key): fail(f'{key} changed (cannot prove compatibility)')
    for key in ('type', 'format'):
        if old.get(key) != new.get(key): fail(f'{key} changed')
    if direction == 'input' and old.get('nullable', False) and not new.get('nullable', False): fail('null no longer accepted')
    if direction == 'output' and not old.get('nullable', False) and new.get('nullable', False): fail('output may now be null')
    old_enum, new_enum = old.get('enum'), new.get('enum')
    if direction == 'input' and new_enum is not None and (old_enum is None or not all(v in new_enum for v in old_enum)): fail('input enum narrowed')
    if direction == 'output' and old_enum is not None and (new_enum is None or not all(v in old_enum for v in new_enum)): fail('output enum widened')
    for key in ('minLength', 'minimum', 'maxLength', 'maximum'):
        a, b = old.get(key), new.get(key)
        lower = key in ('minLength', 'minimum')
        if direction == 'input': incompatible = b is not None and (a is None or (b > a if lower else b < a))
        else: incompatible = a is not None and (b is None or (b < a if lower else b > a))
        if incompatible: fail(f'{key} constraint changed incompatibly')
    old_props, new_props = old.get('properties', {}), new.get('properties', {})
    old_req, new_req = set(old.get('required', [])), set(new.get('required', []))
    if direction == 'input':
        if new_req - old_req: fail(f'new required properties: {sorted(new_req - old_req)}')
        # Removing accepted fields can reject previously valid requests.
        missing = set(old_props) - set(new_props)
    else:
        if old_req - new_req: fail(f'output properties no longer required: {sorted(old_req - new_req)}')
        missing = set(old_props) - set(new_props)
    if missing: fail(f'properties removed: {sorted(missing)}')
    for name in old_props.keys() & new_props.keys():
        compare_schema(old_props[name], new_props[name], direction, f'{where}.{name}', errors)
    if ('items' in old) != ('items' in new): fail('array items changed')
    elif 'items' in old: compare_schema(old['items'], new['items'], direction, f'{where}[]', errors)


def compare(old, new):
    validate(old); validate(new)
    errors = []
    if old.get('components', {}).get('securitySchemes', {}) != new.get('components', {}).get('securitySchemes', {}):
        errors.append('security schemes changed (cannot prove compatibility)')
    def security(doc, op): return op.get('security', doc.get('security', []))
    def alternatives(value):
        # An alternative requiring no more schemes/scopes admits the old alternative.
        return [set((k, scope) for k, scopes in option.items() for scope in (scopes or [''])) for option in value] or [set()]
    def compare_content(a, b, direction, where):
        if direction == 'output':
            for mime in b.get('content', {}).keys() - a.get('content', {}).keys():
                errors.append(f'{where}: response media type {mime} added')
        for mime, media in a.get('content', {}).items():
            if mime not in b.get('content', {}): errors.append(f'{where}: media type {mime} removed')
            else: compare_schema(media['schema'], b['content'][mime]['schema'], direction, f'{where} {mime}', errors)
    for path, operations in old['paths'].items():
        for method, op in operations.items():
            where = f'{method.upper()} {path}'
            current = new['paths'].get(path, {}).get(method)
            if current is None:
                errors.append(f'{where}: operation removed'); continue
            if any(not any(n <= o for n in alternatives(security(new, current))) for o in alternatives(security(old, op))):
                errors.append(f'{where}: security tightened')
            before = {(p['in'], p['name']): p for p in op.get('parameters', [])}
            after = {(p['in'], p['name']): p for p in current.get('parameters', [])}
            for ident, p in after.items():
                if ident not in before and p.get('required', False): errors.append(f'{where}: new required parameter {ident}')
            for ident, p in before.items():
                if ident not in after: errors.append(f'{where}: parameter removed {ident}'); continue
                q = after[ident]
                if not p.get('required', False) and q.get('required', False): errors.append(f'{where}: parameter required {ident}')
                if any(p.get(k) != q.get(k) for k in ('style', 'explode')): errors.append(f'{where}: parameter serialization changed {ident}')
                compare_schema(p['schema'], q['schema'], 'input', f'{where} {ident}', errors)
            a, b = op.get('requestBody'), current.get('requestBody')
            if a and not b: errors.append(f'{where}: request body removed')
            elif b and not a and b.get('required'): errors.append(f'{where}: required request body added')
            elif a and b:
                if not a.get('required') and b.get('required'): errors.append(f'{where}: request body required')
                compare_content(a, b, 'input', where)
            for code in current['responses'].keys() - op['responses'].keys():
                if code.startswith('2'):
                    errors.append(f'{where}: success response {code} added')
            for code, response in op['responses'].items():
                if code not in current['responses']: errors.append(f'{where}: response {code} removed'); continue
                target = current['responses'][code]
                compare_content(response, target, 'output', f'{where} {code}')
                if response.get('headers', {}) != target.get('headers', {}): errors.append(f'{where} {code}: headers changed')
    return errors


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--baseline', type=Path, default=ROOT / 'contracts/baselines/mf11-provisional.json')
    parser.add_argument('--current', type=Path, default=ROOT / 'contracts/openapi.json')
    args = parser.parse_args()
    try:
        errors = compare(json.loads(args.baseline.read_text()), json.loads(args.current.read_text()))
    except (ValueError, KeyError, TypeError, OSError) as e:
        raise SystemExit(f'Contract check failed: {e}')
    if errors:
        raise SystemExit('Contract compatibility failures:\n' + '\n'.join(sorted(errors)))
    print('Contract is compatible with provisional baseline')


if __name__ == '__main__': main()
