"""Genera la cadena sintética de QQQ (la misma de gex/test_gex_nasdaq.py) y los valores de referencia
que la calculadora en Python obtiene con ella. Las pruebas en C# comparan contra estos números.

    python3 generar_cadena.py
"""
import json
import os
import sys
from datetime import datetime

AQUI = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(AQUI, "..", "..", "gex"))
import gex_nasdaq as g  # noqa: E402
import test_gex_nasdaq as t  # noqa: E402


def main():
    ahora = datetime.strptime(t.AHORA, "%Y-%m-%d %H:%M").replace(tzinfo=g.NY)
    con_extra = t.cadena()
    for o in con_extra["data"]["options"]:
        if o["option"] in ("QQQ261106C00780000", "QQQ261106P00720000", "QQQ261030C00770000"):
            o["open_interest"] = 15000
    salidas = {
        "cadena_qqq_sintetica.json": t.cadena(),
        "cadena_qqq_sin_gamma.json": t.cadena(iv_pct=True, sin_gamma=True),
        "cadena_qqq_con_extra.json": con_extra,
    }
    for nombre, crudo in salidas.items():
        with open(os.path.join(AQUI, "datos", nombre), "w", encoding="utf-8") as fh:
            json.dump(crudo, fh)
        spot, ops = g.leer_cadena(crudo, ahora, 45)
        res = g.calcular(spot, ops, 0.04, 0.006, 3)
        print(f"{nombre}: opciones {len(ops)} · call wall {res['call_wall']:g} · put wall {res['put_wall']:g} · "
              f"flip {res['flip']!r} · total {res['gex_total']!r} · extra {res['extra']}")


if __name__ == "__main__":
    main()
