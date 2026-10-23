"""Pruebas de gex_nasdaq.py con una cadena sintética en el formato de CBOE (sin red)."""
import io
import json
import os
import sys
import tempfile
import unittest
from contextlib import redirect_stdout
from datetime import datetime

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gex_nasdaq as g  # noqa: E402

AHORA = "2026-10-23 10:00"


def cadena(spot=750.0, oi_call_760=20000, oi_put_740=24000, iv_pct=False, sin_gamma=False):
    ahora = datetime.strptime(AHORA, "%Y-%m-%d %H:%M").replace(tzinfo=g.NY)
    opts = []
    for venc in ("261030", "261106", "261120"):
        t = (datetime(2026, int(venc[2:4]), int(venc[4:]), 16, tzinfo=g.NY) - ahora).total_seconds() / (365 * 86400)
        for k in range(700, 801, 5):
            for tipo in "CP":
                oi = 1000
                if tipo == "C" and k == 760:
                    oi = oi_call_760
                if tipo == "P" and k == 740:
                    oi = oi_put_740
                iv = 0.22
                o = {"option": f"QQQ{venc}{tipo}{k * 1000:08d}", "open_interest": oi, "iv": iv * 100 if iv_pct else iv}
                if not sin_gamma:
                    o["gamma"] = g.gamma_bs(spot, k, t, iv, 0.04, 0.006)
                opts.append(o)
    # una opción vencida y otra sin open interest, que deben ignorarse
    opts.append({"option": "QQQ261016C00750000", "open_interest": 50000, "iv": 0.2, "gamma": 0.1})
    opts.append({"option": "QQQ261030C00755000", "open_interest": 0, "iv": 0.2, "gamma": 0.1})
    return {"timestamp": "2026-10-23 10:00:00", "data": {"current_price": spot, "options": opts}}


class Pruebas(unittest.TestCase):
    def _calc(self, crudo):
        ahora = datetime.strptime(AHORA, "%Y-%m-%d %H:%M").replace(tzinfo=g.NY)
        spot, ops = g.leer_cadena(crudo, ahora, 45)
        return g.calcular(spot, ops, 0.04, 0.006, 3), ops

    def test_paredes_y_flip(self):
        res, ops = self._calc(cadena())
        self.assertEqual(res["call_wall"], 760)
        self.assertEqual(res["put_wall"], 740)
        self.assertIsNotNone(res["flip"])
        self.assertTrue(740 < res["flip"] < 760, res["flip"])
        self.assertEqual(len(ops), 21 * 2 * 3)   # sin la vencida ni la de OI 0

    def test_flip_sube_con_mas_puts(self):
        base, _ = self._calc(cadena())
        mas_puts, _ = self._calc(cadena(oi_put_740=28000))
        self.assertIsNotNone(mas_puts["flip"])
        self.assertGreater(mas_puts["flip"], base["flip"])

    def test_sin_flip_si_dominan_las_puts(self):
        res, _ = self._calc(cadena(oi_put_740=60000))
        self.assertIsNone(res["flip"])          # GEX negativo en todo el rango de ±12 %
        self.assertLess(res["gex_total"], 0)
        self.assertNotIn("Flip", g.texto_para_pegar(res))

    def test_iv_en_porcentaje_y_sin_gamma(self):
        con, _ = self._calc(cadena())
        sin, _ = self._calc(cadena(iv_pct=True, sin_gamma=True))
        self.assertEqual(sin["call_wall"], con["call_wall"])
        self.assertEqual(sin["put_wall"], con["put_wall"])
        self.assertAlmostEqual(sin["flip"], con["flip"], places=6)
        self.assertAlmostEqual(sin["gex_total"], con["gex_total"], delta=abs(con["gex_total"]) * 1e-6)

    def test_texto_y_conversion(self):
        res, _ = self._calc(cadena())
        txt = g.texto_para_pegar(res)
        self.assertTrue(txt.startswith("Call wall 760, Put wall 740, Flip "), txt)
        self.assertNotIn("GEX ", txt)   # con OI parejo no hay strikes extra que pesen
        nq = g.texto_para_pegar(res, 41.4)
        self.assertIn("Call wall 31464,", nq)

    def test_cli_con_archivo(self):
        with tempfile.NamedTemporaryFile("w", suffix=".json", delete=False) as fh:
            json.dump(cadena(), fh)
        try:
            out = io.StringIO()
            with redirect_stdout(out):
                rc = g.main(["--archivo", fh.name, "--ahora", AHORA, "--nq", "31050"])
        finally:
            os.unlink(fh.name)
        self.assertEqual(rc, 0)
        self.assertIn("Pegar en el indicador (fuente QQQ):", out.getvalue())
        self.assertIn("En NQ/MNQ (razón 41.4000)", out.getvalue())

    def test_json_sin_precio(self):
        ahora = datetime.strptime(AHORA, "%Y-%m-%d %H:%M").replace(tzinfo=g.NY)
        with self.assertRaises(ValueError):
            g.leer_cadena({"data": {"options": []}}, ahora, 45)


if __name__ == "__main__":
    unittest.main()
