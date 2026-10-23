# Calculadora GEX del Nasdaq-100

Calcula los niveles de gamma exposure (call wall, put wall, gamma flip y strikes con más GEX) de QQQ o NDX con la cadena de opciones gratuita de CBOE, que tiene unos 15 minutos de retraso. Entrega una línea lista para pegar en el indicador `primer_ifvg.pine`, en el grupo ⑬ GEX.

## Uso

```
python3 gex_nasdaq.py                   # QQQ, descarga los datos de CBOE
python3 gex_nasdaq.py --simbolo NDX     # NDX
python3 gex_nasdaq.py --nq 31050        # además convierte los niveles a NQ/MNQ
python3 gex_nasdaq.py --archivo QQQ.json  # usa un JSON ya descargado
```

El JSON se puede bajar desde el navegador en `https://cdn.cboe.com/api/global/delayed_quotes/options/QQQ.json` (o `_NDX.json`).

Solo usa la biblioteca estándar de Python 3.9 o superior.

## Qué calcula

- GEX por strike = gamma × open interest × 100 × spot² × 1 %, en dólares por cada 1 % de movimiento. Calls con signo +, puts con signo − (convención de SqueezeMetrics).
- Call wall: strike con más GEX de calls. Put wall: strike con más GEX de puts.
- Gamma flip: precio donde el GEX total cambia de signo, recalculando la gamma con Black-Scholes entre −12 % y +12 % del precio actual.
- Por defecto incluye vencimientos de hasta 45 días (`--max-dias`).

## Limitaciones

- CBOE no documenta el formato del JSON. El lector acepta varios nombres de campo y calcula la gamma si falta, pero no está probado con una descarga real: las pruebas usan una cadena sintética (`python3 -m unittest test_gex_nasdaq`).
- La posición real de los market makers no es pública; el signo de cada opción es un supuesto.
- El open interest es del cierre anterior: el flujo 0DTE del día no aparece.
