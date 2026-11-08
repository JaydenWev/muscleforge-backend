import copy
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
def load(filename):
    spec = importlib.util.spec_from_file_location('contract_tool', ROOT / 'scripts' / filename)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module
export = load('export-openapi.py')
FIXTURE = json.loads((ROOT / 'tests/contract_tooling/fixtures/authenticated-feature.json').read_text())
PATH = '/api/v1/contract-fixture/items/{id}'

class ContractTests(unittest.TestCase):
    def test_path_level_parameter_normalization(self):
        doc = copy.deepcopy(FIXTURE)
        operation = doc['paths'][PATH]['put']
        path_param = operation['parameters'].pop(0)
        doc['paths'][PATH]['parameters'] = [path_param]
        normalized = export.normalize(doc)
        self.assertIn(path_param, normalized['paths'][PATH]['put']['parameters'])

    def test_export_parameter_and_header_preserved_and_unknown_rejected(self):
        doc = copy.deepcopy(FIXTURE)
        op = doc['paths'][PATH]['put']
        op['parameters'][1].update(style='form', explode=True)
        op['responses']['401']['headers'] = {'WWW-Authenticate': {'required': True, 'schema': {'type': 'string'}}}
        result = export.normalize(doc)
        self.assertEqual(op['parameters'], result['paths'][PATH]['put']['parameters'])
        self.assertEqual(op['responses']['401']['headers'], result['paths'][PATH]['put']['responses']['401']['headers'])
        op['parameters'][1]['allowReserved'] = True
        with self.assertRaises(ValueError): export.normalize(doc)

    def test_export_drift_comparison(self):
        normalized = export.normalize(FIXTURE)
        output = json.dumps(normalized, indent=2, sort_keys=True)+'\n'
        with tempfile.TemporaryDirectory() as tmp:
            target = Path(tmp) / 'openapi.json'
            target.write_text(output)
            export.save_snapshot(output, target, True)
            target.write_text('{}\n')
            with self.assertRaisesRegex(SystemExit, 'OpenAPI drift'):
                export.save_snapshot(output, target, True)

if __name__ == '__main__': unittest.main()
