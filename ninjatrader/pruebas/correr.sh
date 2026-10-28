#!/usr/bin/env bash
# 1. Compila PrimerIFVG.cs completo contra la imitación de NinjaTrader (compilacion/).
# 2. Extrae el núcleo (entre NUCLEO INICIO y NUCLEO FIN) y corre las pruebas.
set -euo pipefail
cd "$(dirname "$0")"
# NinjaTrader arma el bloque "generated code" con el primer namespace del archivo
primer=$(grep -m1 '^namespace ' ../PrimerIFVG.cs | tr -d '\r')
[ "$primer" = "namespace NinjaTrader.NinjaScript.Indicators" ] || { echo "el primer namespace debe ser NinjaTrader.NinjaScript.Indicators (es: $primer)" >&2; exit 1; }
dotnet build --nologo -v q compilacion/Compilacion.csproj
sed -n '/\/\/ ===== NUCLEO INICIO =====/,/\/\/ ===== NUCLEO FIN =====/p' ../PrimerIFVG.cs > Nucleo.g.cs
grep -q 'NUCLEO FIN' Nucleo.g.cs || { echo "no encontré los marcadores del núcleo" >&2; exit 1; }
dotnet run --nologo -c Release
