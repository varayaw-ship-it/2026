#!/usr/bin/env python3
"""Niveles GEX (gamma exposure) del Nasdaq-100 a partir de la cadena de opciones de CBOE.

Calcula, para QQQ o NDX:
  · GEX por strike: gamma × open interest × 100 × spot² × 1%, en dólares por cada 1% que
    se mueve el subyacente. Convención de SqueezeMetrics: calls con signo +, puts con signo −
    (supone que los clientes venden calls y compran puts, así que los market makers quedan
    largos en calls y cortos en puts).
  · Call wall: el strike con más GEX de calls.
  · Put wall: el strike con más GEX de puts (en valor absoluto).
  · Gamma flip: el precio donde el GEX total cambia de signo, recalculando la gamma de cada
    opción con Black-Scholes a precios hipotéticos alrededor del actual.
  · Los strikes con más GEX neto, como niveles extra.

Imprime un resumen y una línea para pegar en el indicador de TradingView (grupo ⑬ GEX).

Datos: https://cdn.cboe.com/api/global/delayed_quotes/options/QQQ.json (y _NDX.json), con unos
15 minutos de retraso. El formato no está documentado por CBOE: el lector acepta varios nombres
de campo y, si falta la gamma, la calcula con la volatilidad implícita.

Uso:
  python3 gex_nasdaq.py                       # QQQ, descarga de CBOE
  python3 gex_nasdaq.py --simbolo NDX
  python3 gex_nasdaq.py --archivo QQQ.json    # cadena ya descargada
  python3 gex_nasdaq.py --nq 31050            # además convierte los niveles a NQ/MNQ
"""
from __future__ import annotations

import argparse
import json
import math
import re
import sys
import urllib.request
from dataclasses import dataclass
from datetime import datetime, timezone
from zoneinfo import ZoneInfo

NY = ZoneInfo("America/New_York")
URL = "https://cdn.cboe.com/api/global/delayed_quotes/options/{}.json"
SIMBOLOS = {"QQQ": "QQQ", "NDX": "_NDX"}
OCC = re.compile(r"^([A-Z_]+?)(\d{6})([CP])(\d{8})$")


@dataclass
class Opcion:
    strike: float
    es_call: bool
    t_anios: float
    oi: float
    iv: float | None
    gamma: float | None


def _num(x) -> float | None:
    try:
        v = float(x)
    except (TypeError, ValueError):
        return None
    return v if math.isfinite(v) else None


def _campo(d: dict, *nombres):
    for n in nombres:
        if n in d and d[n] not in (None, ""):
            return d[n]
    return None


def gamma_bs(s: float, k: float, t: float, iv: float, r: float, q: float) -> float:
    """Gamma de Black-Scholes (por acción)."""
    if s <= 0 or k <= 0 or t <= 0 or iv <= 0:
        return 0.0
    raiz = iv * math.sqrt(t)
    d1 = (math.log(s / k) + (r - q + 0.5 * iv * iv) * t) / raiz
    return math.exp(-q * t) * math.exp(-0.5 * d1 * d1) / (math.sqrt(2 * math.pi) * s * raiz)


def _anios(desde: datetime, hasta: datetime) -> float:
    """Años entre dos instantes. Se pasan a UTC: restar dos fechas con la misma zona ignora el cambio de horario."""
    return (hasta.astimezone(timezone.utc) - desde.astimezone(timezone.utc)).total_seconds() / (365.0 * 86400)


def leer_cadena(crudo: dict, ahora: datetime, max_dias: float) -> tuple[float, list[Opcion]]:
    """Devuelve (spot, opciones vigentes) desde el JSON de CBOE."""
    data = crudo.get("data", crudo)
    spot = _num(_campo(data, "current_price", "close", "last", "price", "prev_day_close"))
    if not spot:
        raise ValueError("no encontré el precio del subyacente en el JSON (current_price / close / last)")
    opciones = []
    for o in data.get("options", []):
        m = OCC.match(str(_campo(o, "option", "symbol", "contract") or ""))
        if not m:
            continue
        _, fecha, tipo, strike = m.groups()
        venc = datetime(2000 + int(fecha[:2]), int(fecha[2:4]), int(fecha[4:]), 16, 0, tzinfo=NY)
        t_anios = _anios(ahora, venc)
        if t_anios <= 0 or t_anios * 365 > max_dias:
            continue
        oi = _num(_campo(o, "open_interest", "openInterest", "oi")) or 0.0
        if oi <= 0:
            continue
        iv = _num(_campo(o, "iv", "implied_volatility", "impliedVolatility"))
        if iv is not None and iv > 3:      # algunos feeds la dan en porcentaje
            iv /= 100.0
        opciones.append(Opcion(int(strike) / 1000.0, tipo == "C", t_anios, oi, iv, _num(o.get("gamma"))))
    if not opciones:
        raise ValueError("no hay opciones vigentes con open interest en el rango de vencimientos pedido")
    return spot, opciones


def gex_opcion(op: Opcion, s: float, r: float, q: float, usar_gamma_feed: bool) -> float:
    """GEX de una opción a precio s, en dólares por 1% de movimiento."""
    if usar_gamma_feed and op.gamma is not None:
        g = op.gamma
    elif op.iv:
        g = gamma_bs(s, op.strike, op.t_anios, op.iv, r, q)
    else:
        return 0.0
    return (1 if op.es_call else -1) * g * op.oi * 100 * s * s * 0.01


def calcular(spot: float, opciones: list[Opcion], r: float, q: float, n_extra: int) -> dict:
    por_strike: dict[float, list[float]] = {}
    for op in opciones:
        v = gex_opcion(op, spot, r, q, usar_gamma_feed=True)
        c = por_strike.setdefault(op.strike, [0.0, 0.0])
        c[0 if op.es_call else 1] += v
    strikes = sorted(por_strike)
    call_wall = max(strikes, key=lambda k: por_strike[k][0])
    put_wall = min(strikes, key=lambda k: por_strike[k][1])      # el más negativo
    total = sum(c + p for c, p in por_strike.values())

    # perfil: GEX total a precios hipotéticos (±12 % en pasos de 0,25 %), con gamma de Black-Scholes
    perfil = []
    for i in range(-48, 49):
        s = spot * (1 + i * 0.0025)
        perfil.append((s, sum(gex_opcion(op, s, r, q, usar_gamma_feed=False) for op in opciones)))
    flip = None
    for (s0, g0), (s1, g1) in zip(perfil, perfil[1:]):
        if g0 == 0 or (g0 < 0) != (g1 < 0):
            cruce = s0 if g0 == 0 else s0 + (s1 - s0) * (-g0) / (g1 - g0)
            if flip is None or abs(cruce - spot) < abs(flip - spot):
                flip = cruce

    # strikes extra: los de más GEX neto, siempre que pesen al menos 10 % del mayor (si no, son ruido)
    usados = {call_wall, put_wall}
    netos = {k: abs(sum(por_strike[k])) for k in strikes}
    tope = max(netos.values()) if netos else 0.0
    extra = [k for k in sorted(strikes, key=lambda k: -netos[k]) if k not in usados and netos[k] >= 0.1 * tope][:n_extra]
    return {
        "spot": spot,
        "gex_total": total,
        "call_wall": call_wall,
        "put_wall": put_wall,
        "flip": flip,
        "extra": sorted(extra),
        "por_strike": {k: por_strike[k] for k in strikes},
    }


def _fmt(v: float) -> str:
    return f"{v:.2f}".rstrip("0").rstrip(".")


def texto_para_pegar(res: dict, factor: float | None = None) -> str:
    f = (lambda v: _fmt(v * factor)) if factor else _fmt
    partes = [f"Call wall {f(res['call_wall'])}", f"Put wall {f(res['put_wall'])}"]
    if res["flip"] is not None:
        partes.append(f"Flip {f(res['flip'])}")
    partes += [f"GEX {f(k)}" for k in res["extra"]]
    return ", ".join(partes)


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description="Niveles GEX del Nasdaq-100 (QQQ o NDX) con datos de CBOE.")
    ap.add_argument("--simbolo", choices=sorted(SIMBOLOS), default="QQQ")
    ap.add_argument("--archivo", help="JSON de CBOE ya descargado (si no, se descarga)")
    ap.add_argument("--max-dias", type=float, default=45, help="vencimientos incluidos (días; por defecto 45)")
    ap.add_argument("--extra", type=int, default=3, help="strikes extra con más GEX neto (por defecto 3)")
    ap.add_argument("--tasa", type=float, default=0.04, help="tasa libre de riesgo anual (por defecto 0.04)")
    ap.add_argument("--dividendo", type=float, default=None, help="rendimiento por dividendo (QQQ 0.006, NDX 0.0)")
    ap.add_argument("--nq", type=float, help="precio actual de NQ/MNQ para convertir los niveles")
    ap.add_argument("--ahora", help="fecha y hora NY para el cálculo, AAAA-MM-DD HH:MM (pruebas)")
    a = ap.parse_args(argv)

    if a.archivo:
        with open(a.archivo, encoding="utf-8") as fh:
            crudo = json.load(fh)
    else:
        req = urllib.request.Request(URL.format(SIMBOLOS[a.simbolo]), headers={"User-Agent": "Mozilla/5.0"})
        with urllib.request.urlopen(req, timeout=60) as resp:
            crudo = json.load(resp)

    ahora = datetime.strptime(a.ahora, "%Y-%m-%d %H:%M").replace(tzinfo=NY) if a.ahora else datetime.now(timezone.utc).astimezone(NY)
    q = a.dividendo if a.dividendo is not None else (0.006 if a.simbolo == "QQQ" else 0.0)
    spot, opciones = leer_cadena(crudo, ahora, a.max_dias)
    res = calcular(spot, opciones, a.tasa, q, a.extra)

    print(f"{a.simbolo} {spot:.2f} · {len(opciones)} opciones con open interest · vencimientos hasta {a.max_dias:g} días")
    print(f"GEX total: {res['gex_total'] / 1e9:+.2f} mil millones USD por 1 % "
          f"({'gamma + (rango)' if res['gex_total'] > 0 else 'gamma − (tendencia)'})")
    print(f"Call wall {res['call_wall']:g} · Put wall {res['put_wall']:g} · "
          f"Flip {('%.2f' % res['flip']) if res['flip'] is not None else 'sin cruce en ±12 %'}")
    print()
    print(f"Pegar en el indicador (fuente {a.simbolo}):")
    print(texto_para_pegar(res))
    if a.nq:
        factor = a.nq / spot if a.simbolo == "QQQ" else None
        print()
        if factor:
            print(f"En NQ/MNQ (razón {factor:.4f}), fuente NQ:")
            print(texto_para_pegar(res, factor))
        else:
            base = a.nq - spot
            print(f"En NQ/MNQ (NQ − NDX = {base:+.2f}), fuente NQ:")
            r2 = dict(res, call_wall=res["call_wall"] + base, put_wall=res["put_wall"] + base,
                      flip=None if res["flip"] is None else res["flip"] + base,
                      extra=[k + base for k in res["extra"]])
            print(texto_para_pegar(r2))
    return 0


if __name__ == "__main__":
    sys.exit(main())
