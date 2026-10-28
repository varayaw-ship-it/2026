# Primer IFVG para NinjaTrader 8

Indicador `PrimerIFVG.cs`, versión 0.1. Es la parte de análisis del indicador de TradingView (`primer_ifvg.pine`), sin trades, sin bias y sin panel.

Qué dibuja:

- **IFVG de 1, 2, 3, 4 y 5 minutos**, con vista previa punteada mientras la vela todavía no cierra. La vista previa muestra los segundos que faltan para el cierre.
- **CISD** de 1 a 5 minutos, también con vista previa y cuenta regresiva.
- **FVG de 15m, 1h y 4h** con su CE (50 %). Como en TradingView, cada caja llega hasta el borde derecho. El contorno negro es más oscuro y más grueso cuanto mayor es la temporalidad: 1, 2 y 3 px.
  - Cada borde (techo y piso) lleva escrita su temporalidad y dirección, por ejemplo `4h ▲`, a la derecha de la última vela. Si el borde queda fuera de la pantalla, el rótulo se queda en el borde de la pantalla.
  - Solo va rellena la caja más angosta que contiene el precio. Las demás quedan solo con su contorno, para que no se forme una mancha al superponerse.
- **Niveles de liquidez**: PDH/PDL, Asia, Londres y pre-market, swings de 15m y 5m, y EQH/EQL. Un nivel desaparece cuando el precio lo barre.
- **GEX de QQQ**, calculado solo desde CBOE cada 15 minutos y convertido a NQ: call wall, put wall, gamma flip y los strikes con más GEX.

Arriba del gráfico aparecen seis botones: **IFVG · CISD · FVG HTF · Niveles · GEX · Sonido**. Cada uno muestra u oculta su parte al instante. En verde está encendido y en gris, apagado.

El sonido suena en dos casos:

- Cuando se confirma un IFVG de 3m o más.
- Cuando el precio cruza el call wall, el put wall o el gamma flip. El mismo nivel no vuelve a sonar antes de 5 minutos.

Los avisos también quedan en la ventana Alerts de NinjaTrader.

## Instalar

1. En Windows, abrir este enlace en el navegador:
   `https://raw.githubusercontent.com/varayaw-ship-it/2026/claude/pine-script-mnq-improvements-ivt5gg/ninjatrader/PrimerIFVG.cs`
2. Guardar la página con clic derecho → *Guardar como*. El destino es la carpeta `Documentos\NinjaTrader 8\bin\Custom\Indicators` y el nombre debe ser `PrimerIFVG.cs`. Hay que revisar que no quede como `PrimerIFVG.cs.txt`.
3. En NinjaTrader: **New → NinjaScript Editor**. Abrir *Indicators → PrimerIFVG* y apretar **F5** para compilar. Si aparece algún error abajo, copiar la lista completa, con su número de línea.
4. Abrir un gráfico de MNQ o NQ de 30 s o de 1 a 5 minutos, con **Days to load de al menos 5**. Agregar el indicador: clic derecho → Indicators → PrimerIFVG.

Sin conexión a datos no se dibuja nada. El indicador dibuja cuando el gráfico pasa a tiempo real, que con Tradovate conectado es apenas termina de cargar. También funciona con Playback.

## Ajustes

| Grupo | Qué controla |
|---|---|
| 0. Mostrar | Lo mismo que los botones. Queda guardado con el workspace. |
| 1. Señal IFVG / CISD | Temporalidades de señal (1–5m), tamaño mínimo del FVG, plazo para invertirse (en velas o en minutos uniformes) y velas a cada lado de los pivotes. |
| 2. Visual IFVG / CISD | Vista previa, IFVG muertos en gris, ocultar IFVG tras N minutos, deduplicación entre temporalidades, CE, texto y colores. |
| 3. FVG 15m / 1h / 4h | Cuáles se ven, cuántos por arriba y por abajo, relleno de la caja donde está el precio, color, opacidad y grosor del contorno por temporalidad, CE. |
| 4. Liquidez | Qué niveles se ven, cuántas etiquetas por lado y horario de las sesiones (hora NY). |
| 5. GEX | Cada cuántos minutos se actualiza, vencimientos incluidos (45 días), strikes extra, razón NQ/QQQ manual y colores. |
| 6. Sonido | Archivos .wav (de la carpeta `sounds` de NinjaTrader o con ruta completa) y minutos entre avisos del mismo nivel GEX. |

## GEX

- Fuente: `https://cdn.cboe.com/api/global/delayed_quotes/options/QQQ.json`. Son datos gratuitos con unos 15 minutos de retraso.
- Fórmula: la misma de `gex/gex_nasdaq.py`. Las pruebas comprueban que dan exactamente el mismo resultado.
- **Razón NQ/QQQ.** En horario regular se usa el precio de NQ de hace 15 minutos, para empatar el retraso de CBOE. Fuera de horario se usa el NQ de las 16:00 NY de la última sesión. La razón aparece arriba a la derecha junto con el régimen: sobre el flip, gamma positiva; bajo el flip, gamma negativa.
- Si la descarga falla, el error aparece arriba a la derecha y se reintenta a los 2 minutos.

## Limitaciones de esta versión

- Todavía no se ha compilado en NinjaTrader. La lógica tiene 84 comprobaciones, y el archivo completo compila contra una imitación de la API de NinjaTrader. Aun así, la primera compilación con F5 puede mostrar algún error de la API real.
- El formato del JSON de CBOE no está documentado y no se ha probado con una descarga real.
- Un IFVG de 1m en un gráfico de 5m puede verse muy angosto: su caja cabe dentro de una sola vela del gráfico.

## Pruebas (para desarrollo)

`pruebas/correr.sh` hace dos cosas, con .NET 8 y C# 7.3 como NinjaTrader:

1. Compila el archivo completo contra `pruebas/compilacion/Imitacion.cs`.
2. Extrae el núcleo (entre `NUCLEO INICIO` y `NUCLEO FIN`) y corre `pruebas/Pruebas.cs`.

`pruebas/generar_cadena.py` vuelve a generar las cadenas de opciones sintéticas y los valores de referencia de la calculadora en Python.
