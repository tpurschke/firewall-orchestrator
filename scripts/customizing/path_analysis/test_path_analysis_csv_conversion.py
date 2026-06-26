import tempfile
import unittest
from pathlib import Path

from scripts.customizing.path_analysis.convert_tsq_csv import convert_csv_file


class PathAnalysisCsvConversionTest(unittest.TestCase):
    def test_convert_csv_file_writes_json_payload_shape(self) -> None:
        csv_text = """Version,Zone,Netz,Maske,TSQ-Root,TSQ-Internet,TSQ-Internet-Filter
v,A,10.1.0.0,16,Start|FW-A|Root,Start|FW-A|FW-E|Internet,#
v,INTERNET,! RFC1918,,Start|-|Root,Start|-|Internet,#
"""
        with tempfile.TemporaryDirectory() as tmpdir:
            csv_path = Path(tmpdir) / "tsq.csv"
            csv_path.write_text(csv_text, encoding="utf-8")

            payload = convert_csv_file(csv_path, "tsq.csv")

        self.assertEqual(payload["source_name"], "tsq.csv")
        self.assertEqual(
            payload["entries"],
            [
                {
                    "version": "v",
                    "zone": "A",
                    "network": "10.1.0.0/16",
                    "root_path": "Start|FW-A|Root",
                    "internet_path": "Start|FW-A|FW-E|Internet",
                },
                {
                    "version": "v",
                    "zone": "INTERNET",
                    "network": "! RFC1918",
                    "root_path": "Start|-|Root",
                    "internet_path": "Start|-|Internet",
                },
            ],
        )


if __name__ == "__main__":
    unittest.main()
