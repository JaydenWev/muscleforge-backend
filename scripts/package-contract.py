#!/usr/bin/env python3
"""Package the verified canonical contract with immutable source provenance."""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import re
import shutil

ROOT = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--revision', required=True, help='full 40-character source commit SHA')
parser.add_argument('--output', type=Path, required=True)
args = parser.parse_args()
if not re.fullmatch(r'[0-9a-f]{40}', args.revision):
    parser.error('revision must be a full lowercase Git commit SHA')
source = ROOT / 'contracts/openapi.json'
content = source.read_bytes()
doc = json.loads(content)
spec = importlib.util.spec_from_file_location('contract_checker', ROOT / 'scripts/check-contract-compatibility.py')
checker = importlib.util.module_from_spec(spec)
spec.loader.exec_module(checker)
try:
    checker.validate(doc)
except (ValueError, KeyError, TypeError) as exc:
    parser.error(f'contract is not canonical: {exc}')
versions = set()
for path in doc['paths']:
    match = re.match(r'^/api/(v[0-9]+)(?:/|$)', path)
    if not match:
        parser.error(f'path does not have a versioned API prefix: {path}')
    versions.add(match.group(1))
if len(versions) != 1:
    parser.error('contract paths must share exactly one API version')
document_version = doc['info']['version']
if not isinstance(document_version, str) or not re.fullmatch(r'[A-Za-z0-9][A-Za-z0-9._-]*', document_version):
    parser.error('document version must be nonempty and safe for artifact names')
if doc['openapi'] != '3.0.3':
    parser.error('expected canonical OpenAPI 3.0.3')
args.output.mkdir(parents=True, exist_ok=True)
shutil.copyfile(source, args.output / 'openapi.json')
manifest = {'sourceRevision': args.revision, 'apiVersion': versions.pop(), 'documentVersion': document_version,
            'openapiVersion': doc['openapi'], 'sha256': hashlib.sha256(content).hexdigest()}
(args.output / 'manifest.json').write_text(json.dumps(manifest, indent=2, sort_keys=True) + '\n')
print(json.dumps(manifest, sort_keys=True))
