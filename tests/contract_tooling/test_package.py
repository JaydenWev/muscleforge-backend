import hashlib
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]

class ContractTests(unittest.TestCase):
    def test_package_manifest_and_invalid_revision(self):
        revision = 'e' * 40
        with tempfile.TemporaryDirectory() as tmp:
            out = Path(tmp) / 'artifact'
            cmd = [sys.executable, str(ROOT/'scripts/package-contract.py'), '--revision', revision, '--output', str(out)]
            subprocess.run(cmd, check=True, capture_output=True, text=True)
            original = (ROOT/'contracts/openapi.json').read_bytes()
            self.assertEqual(original, (out/'openapi.json').read_bytes())
            self.assertEqual({'sourceRevision': revision, 'apiVersion': 'v1', 'documentVersion': '0.1.0-dev',
                              'openapiVersion': '3.0.3', 'sha256': hashlib.sha256(original).hexdigest()},
                             json.loads((out/'manifest.json').read_text()))
            cmd[3] = 'bad-revision'
            self.assertNotEqual(0, subprocess.run(cmd, capture_output=True).returncode)

if __name__ == '__main__': unittest.main()
