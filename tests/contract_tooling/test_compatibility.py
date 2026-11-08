import copy
import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
def load(filename):
    spec = importlib.util.spec_from_file_location('contract_tool', ROOT / 'scripts' / filename)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module
checker = load('check-contract-compatibility.py')
FIXTURE = json.loads((ROOT / 'tests/contract_tooling/fixtures/authenticated-feature.json').read_text())
PATH = '/api/v1/contract-fixture/items/{id}'

class ContractTests(unittest.TestCase):
    def setUp(self):
        self.old = copy.deepcopy(FIXTURE)
        self.new = copy.deepcopy(FIXTURE)
        self.op = self.new['paths'][PATH]['put']

    def failures(self): return checker.compare(self.old, self.new)

    def test_unchanged_and_additive_inputs(self):
        self.assertEqual([], self.failures())
        self.op['parameters'].append({'name':'optional', 'in':'query', 'schema':{'type':'string'}})
        self.op['requestBody']['content']['application/json']['schema']['properties']['mode']['enum'].append('new')
        self.assertEqual([], self.failures())

    def test_breaking_cases(self):
        cases = [lambda op: op['parameters'][1].update(required=True),
                 lambda op: op['requestBody']['content']['application/json']['schema']['properties']['mode']['enum'].pop(),
                 lambda op: op['responses']['200']['content']['application/json']['schema']['required'].remove('id'),
                 lambda op: op['responses']['200']['content']['application/json']['schema']['properties']['id'].update(format='date-time'),
                 lambda op: op['responses'].pop('200')]
        for mutate in cases:
            with self.subTest(mutate=mutate):
                self.setUp(); mutate(self.op); self.assertTrue(self.failures())

    def test_security_tightening_and_new_success_status_break(self):
        self.old['paths'][PATH]['put'].pop('security')
        self.assertTrue(any('security tightened' in e for e in self.failures()))
        self.setUp()
        self.op['responses']['201'] = copy.deepcopy(self.op['responses']['200'])
        self.assertTrue(any('success response' in e for e in self.failures()))

    def test_new_response_media_type_breaks(self):
        self.op['responses']['200']['content']['application/xml'] = copy.deepcopy(
            self.op['responses']['200']['content']['application/json'])
        self.assertTrue(any('response media type' in e for e in self.failures()))

    def test_output_enum_widening_breaks(self):
        a = self.old['paths'][PATH]['put']['responses']['200']['content']['application/json']['schema']['properties']
        b = self.op['responses']['200']['content']['application/json']['schema']['properties']
        a['kind'] = {'type':'string','enum':['a']}; b['kind'] = {'type':'string','enum':['a','b']}
        self.assertTrue(self.failures())

    def test_cli_reports_break_and_unknown_keyword(self):
        self.new['paths'].pop(PATH)
        with tempfile.TemporaryDirectory() as tmp:
            target = Path(tmp) / 'changed.json'; target.write_text(json.dumps(self.new))
            result = subprocess.run([sys.executable, str(ROOT/'scripts/check-contract-compatibility.py'), '--baseline', str(ROOT/'tests/contract_tooling/fixtures/authenticated-feature.json'), '--current', str(target)], capture_output=True, text=True)
            self.assertNotEqual(0, result.returncode); self.assertIn('operation removed', result.stderr)
        self.setUp()
        self.op['parameters'][0]['schema']['exclusiveMinimum'] = 1
        with self.assertRaises(ValueError): checker.validate(self.new)

if __name__ == '__main__': unittest.main()
