#!/usr/bin/env bash
# 1. Compila PrimerIFVG.cs completo contra la imitación de NinjaTrader (compilacion/).
# 2. Extrae el núcleo (entre NUCLEO INICIO y NUCLEO FIN) y corre las pruebas.
set -euo pipefail
cd "$(dirname "$0")"
dotnet build --nologo -v q compilacion/Compilacion.csproj
sed -n '/\/\/ ===== NUCLEO INICIO =====/,/\/\/ ===== NUCLEO FIN =====/p' ../PrimerIFVG.cs > Nucleo.g.cs
grep -q 'NUCLEO FIN' Nucleo.g.cs || { echo "no encontré los marcadores del núcleo" >&2; exit 1; }
dotnet run --nologo -c Release
