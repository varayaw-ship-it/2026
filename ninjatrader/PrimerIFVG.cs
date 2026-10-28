// ══════════════════════════════════════════════════════════════════════════════
//  PRIMER IFVG · NinjaTrader 8 · v0.5 (2026-10-28)
//  Versión para NinjaTrader del indicador "Primer IFVG" de TradingView (núcleo):
//  IFVG de 1–5m con vista previa punteada, CISD, FVG de 15m/1h/4h con su CE,
//  niveles de liquidez y niveles GEX de QQQ descargados solos de CBOE.
//  Sin trades, sin bias y sin panel.
//
//  INSTALAR (ver ninjatrader/README.md)
//  · Guardar este archivo como PrimerIFVG.cs en
//    Documentos\NinjaTrader 8\bin\Custom\Indicators, abrir New → NinjaScript
//    Editor, abrir el indicador y compilar con F5.
//  · Agregarlo a un gráfico de MNQ o NQ (30 s, 1, 2, 3, 4 o 5 min) con al menos
//    5 días cargados ("Days to load"), para tener PDH/PDL y FVG de 4h.
//
//  CÓMO TRABAJA
//  · Pide la serie de 1 minuto del mismo instrumento y arma con ella las velas
//    de 1–5m, 15m, 1h y 4h alineadas a la hora de Nueva York (las de 4h desde
//    las 18:00). Todo se evalúa con la vela de 1 minuto cerrada; solo la vista
//    previa sigue al precio en vivo. Se dibuja una vez por segundo.
//  · Botones arriba del gráfico: IFVG · CISD · FVG HTF · Niveles · GEX · Sonido.
//  · GEX: descarga la cadena de opciones de QQQ de CBOE (unos 15 min de
//    retraso) cada N minutos, calcula call wall, put wall, gamma flip y los
//    strikes con más GEX, y los pasa a NQ con la razón NQ/QQQ.
//  · Sonido: IFVG confirmado de 3m o más, y precio que cruza un nivel GEX.
//
//  El núcleo (entre NUCLEO INICIO y NUCLEO FIN) es C# puro y tiene pruebas en
//  ninjatrader/pruebas; el resto usa la API de NinjaTrader.
// ══════════════════════════════════════════════════════════════════════════════

#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using PrimerIFVGNucleo;
#endregion


namespace NinjaTrader.NinjaScript.Indicators
{
    public class PrimerIFVG : Indicator
    {
        // ── estado ──────────────────────────────────────────────────────────
        Motor motor;
        readonly object motorLock = new object();     // el motor se alimenta en el hilo de datos y se lee en el de dibujo
        TimeZoneInfo tzNy, tzLocal;
        DateTime ultimo1m = DateTime.MinValue;
        readonly object gexLock = new object();
        GexResultado gexRes;
        string gexError;
        bool reintentar;
        volatile bool gexDescargando;
        DateTime proximaDescargaUtc = DateTime.MinValue;
        double gexRazon = double.NaN;
        DateTime gexHora;
        GexResultado gexResAplicado;
        double precioPrevio = double.NaN;
        readonly Dictionary<string, DateTime> ultimoAvisoGex = new Dictionary<string, DateTime>();
        HashSet<string> tagsPrevios = new HashSet<string>(), tagsAhora = new HashSet<string>();
        SimpleFont fuente;
        TimeSpan barra = TimeSpan.FromMinutes(1);       // duración de la vela del gráfico
        DateTime relojNy;                               // hora actual en Nueva York, para la cuenta regresiva
        // etiquetas pegadas al borde derecho del gráfico (niveles, GEX, FVG 15m/1h/4h), dibujadas en OnRender
        class EtiquetaDer { public double P; public string Texto; public Brush Color; public bool Arriba; }
        class FilaRotulo { public float Y; public string Texto; public Brush Color; }
        List<EtiquetaDer> etiquetasNuevas = new List<EtiquetaDer>();
        volatile List<EtiquetaDer> etiquetasDerecha = new List<EtiquetaDer>();
        // FVG 15m/1h/4h a rotular en su techo y en su piso, junto a la última vela (también en OnRender)
        class CajaRotulo { public double Top, Bot; public string Texto; public Brush Color; }
        class Hueco { public float X, Y, W, H; }
        List<CajaRotulo> cajasNuevas = new List<CajaRotulo>();
        volatile List<CajaRotulo> cajasHtf = new List<CajaRotulo>();
        System.Windows.Threading.DispatcherTimer timer;
        NinjaTrader.Gui.Chart.Chart chartWindow;
        readonly List<System.Windows.Controls.Button> botones = new List<System.Windows.Controls.Button>();
        bool botonesVisibles;
        static readonly SolidColorBrush bOn = Congelar(Color.FromRgb(38, 120, 110)), bOff = Congelar(Color.FromRgb(90, 90, 90));
        static readonly SolidColorBrush cPd = Congelar(Color.FromRgb(255, 179, 0)), cSes = Congelar(Color.FromRgb(66, 165, 245)),
                                        cSw = Congelar(Color.FromRgb(158, 158, 158)), cEq = Congelar(Color.FromRgb(171, 71, 188));

        static SolidColorBrush Congelar(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

        static SolidColorBrush ConOpacidad(Brush b, int opacidad)
        {
            Color c = b is SolidColorBrush ? ((SolidColorBrush)b).Color : Colors.Gray;
            return Congelar(Color.FromArgb((byte)Math.Max(0, Math.Min(255, opacidad * 255 / 100)), c.R, c.G, c.B));
        }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Primer IFVG: IFVG 1–5m con vista previa, CISD, FVG 15m/1h/4h con CE, niveles de liquidez y GEX de QQQ.";
                Name = "PrimerIFVG";
                Calculate = Calculate.OnPriceChange;
                IsOverlay = true;
                DisplayInDataBox = false;
                DrawOnPricePanel = true;
                PaintPriceMarkers = false;
                IsSuspendedWhileInactive = false;
                ScaleJustification = NinjaTrader.Gui.Chart.ScaleJustification.Right;

                MostrarIFVG = true; MostrarCISD = true; MostrarHTF = true; MostrarNiveles = true; MostrarGEX = true; Sonido = true;

                Usar1m = true; Usar2m = true; Usar3m = true; Usar4m = true; Usar5m = true;
                FvgMinTicks = 0; MaxVelasInv = 60; LimiteEnMinutos = false; MaxMinutosInv = 60; VelasPivote = 2;

                VistaPrevia = true; MostrarMuertos = false; OcultarIfvgMinutos = 0; DedupMinutos = 10; DedupPuntos = 2;
                MarcarCE = true; TamanoTexto = 10;
                ColorIfvgAlcista = Congelar(Color.FromRgb(0x26, 0xA6, 0x9A));
                ColorIfvgBajista = Congelar(Color.FromRgb(0xEF, 0x53, 0x50));

                Usar15m = true; Usar1h = true; Usar4h = true; HtfCercanos = 3; HtfOpuestos = true; ConteoHtf = true; HtfOpacidadRelleno = 7;
                ColorHtfAlcista = Congelar(Color.FromRgb(0x1B, 0x5E, 0x20));
                ColorHtfBajista = Congelar(Color.FromRgb(0x80, 0x19, 0x22));
                ColorContorno = Brushes.Black;
                Contorno15m = 20; Contorno1h = 45; Contorno4h = 75; Grosor15m = 1; Grosor1h = 2; Grosor4h = 3; CeEnHtf = true;

                MostrarPdhPdl = true; MostrarSesiones = true; MostrarSwings15 = true; MostrarSwings5 = true; EtiquetasNiveles = 3;
                SesionAsia = "1900-0100"; SesionLondres = "0200-0600"; SesionPremarket = "0700-0930";

                GexMinutos = 15; GexMaxDias = 45; GexStrikesExtra = 3; GexRazonManual = 0;
                ColorCallWall = Congelar(Color.FromRgb(0xE9, 0x1E, 0x63));
                ColorPutWall = Congelar(Color.FromRgb(0x00, 0xAC, 0xC1));
                ColorFlip = Congelar(Color.FromRgb(0x39, 0x49, 0xAB));
                ColorGexOtros = Brushes.Gray;

                SonidoIfvg = "Alert2.wav"; SonidoGex = "Alert4.wav"; MinutosEntreAvisosGex = 5;
            }
            else if (State == State.Configure)
            {
                AddDataSeries(BarsPeriodType.Minute, 1);
            }
            else if (State == State.DataLoaded)
            {
                tzNy = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
                tzLocal = Core.Globals.GeneralOptions.TimeZoneInfo;
                var cfg = new ConfigMotor
                {
                    VelasPivote = VelasPivote,
                    FvgMinPuntos = FvgMinTicks * TickSize,
                    MaxVelasInv = MaxVelasInv,
                    LimiteEnMinutos = LimiteEnMinutos,
                    MaxMinutosInv = MaxMinutosInv
                };
                int a, b;
                if (Utiles.LeerSesion(SesionAsia, out a, out b)) { cfg.AsiaIni = a; cfg.AsiaFin = b; }
                if (Utiles.LeerSesion(SesionLondres, out a, out b)) { cfg.LonIni = a; cfg.LonFin = b; }
                if (Utiles.LeerSesion(SesionPremarket, out a, out b)) { cfg.PmIni = a; cfg.PmFin = b; }
                motor = new Motor(cfg);
                fuente = new SimpleFont("Arial", TamanoTexto);
                if (BarsPeriod.BarsPeriodType == BarsPeriodType.Second) barra = TimeSpan.FromSeconds(BarsPeriod.Value);
                else if (BarsPeriod.BarsPeriodType == BarsPeriodType.Minute) barra = TimeSpan.FromMinutes(BarsPeriod.Value);
            }
            else if (State == State.Historical)
            {
                if (ChartControl != null) ChartControl.Dispatcher.InvokeAsync(CrearBotones);
            }
            else if (State == State.Realtime)
            {
                if (ChartControl != null)
                    ChartControl.Dispatcher.InvokeAsync(() =>
                    {
                        timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                        timer.Tick += (s, e) => Tick();
                        timer.Start();
                    });
            }
            else if (State == State.Terminated)
            {
                if (ChartControl != null)
                    ChartControl.Dispatcher.InvokeAsync(() =>
                    {
                        if (timer != null) { timer.Stop(); timer = null; }
                        QuitarBotones();
                    });
            }
        }

        // ── datos ───────────────────────────────────────────────────────────

        DateTime ANy(DateTime local)
        {
            try { return TimeZoneInfo.ConvertTime(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), tzLocal, tzNy); }
            catch (ArgumentException) { return local; }
        }

        DateTime ALocal(DateTime ny)
        {
            try { return TimeZoneInfo.ConvertTime(DateTime.SpecifyKind(ny, DateTimeKind.Unspecified), tzNy, tzLocal); }
            catch (ArgumentException) { return ny; }
        }

        bool[] TfOn() { return new[] { false, Usar1m, Usar2m, Usar3m, Usar4m, Usar5m }; }

        protected override void OnBarUpdate()
        {
            if (motor == null) return;
            if (BarsInProgress == 1)
            {
                // vela de 1 minuto cerrada: en histórico es la [0]; en tiempo real, la [1] en el primer tick de la siguiente
                int idx = State == State.Historical ? 0 : (IsFirstTickOfBar ? 1 : -1);
                if (idx < 0 || CurrentBars[1] < idx) return;
                DateTime tcLocal = Times[1][idx];
                // la vela en curso al cargar el gráfico todavía no cerró: se procesa en tiempo real
                if (tcLocal <= ultimo1m || (State == State.Historical && tcLocal > Core.Globals.Now)) return;
                ultimo1m = tcLocal;
                DateTime tcNy = ANy(tcLocal);
                List<Fvg> nuevos;
                lock (motorLock)
                {
                    motor.ProcesarMinuto(tcNy.AddMinutes(-1), tcNy, Opens[1][idx], Highs[1][idx], Lows[1][idx], Closes[1][idx]);
                    nuevos = new List<Fvg>(motor.NuevosIfvg);
                    motor.DescartarEventos();
                }
                if (State == State.Realtime) AvisarIfvg(nuevos);
                return;
            }
            if (BarsInProgress == 0 && State == State.Realtime)
                AvisarGex(Closes[0][0]);
        }

        // ── sonido ──────────────────────────────────────────────────────────

        // nombre de un .wav de la carpeta sounds de NinjaTrader, o una ruta completa
        static string RutaSonido(string archivo)
        {
            return System.IO.Path.IsPathRooted(archivo) ? archivo : System.IO.Path.Combine(Core.Globals.InstallDir, "sounds", archivo);
        }

        void AvisarIfvg(List<Fvg> nuevos)
        {
            if (!Sonido) return;
            bool[] on = TfOn();
            foreach (Fvg f in nuevos)
            {
                if (f.Tf < 3 || !on[f.Tf]) continue;
                int dirI = -f.Dir;
                Alert("PIFVG_ifvg_" + f.Id, Priority.Medium,
                      string.Format(CultureInfo.InvariantCulture, "IFVG {0}m {1} · {2:0.00}–{3:0.00}", f.Tf, dirI == 1 ? "alcista" : "bajista", f.Bot, f.Top),
                      RutaSonido(SonidoIfvg), 0, Brushes.Black, dirI == 1 ? ColorIfvgAlcista : ColorIfvgBajista);
            }
        }

        void AvisarGex(double precio)
        {
            double prev = precioPrevio;
            precioPrevio = precio;
            if (!Sonido || !MostrarGEX || double.IsNaN(prev) || gexResAplicado == null || double.IsNaN(gexRazon)) return;
            var niveles = new List<KeyValuePair<string, double>>
            {
                new KeyValuePair<string, double>("Call wall", gexResAplicado.CallWall * gexRazon),
                new KeyValuePair<string, double>("Put wall", gexResAplicado.PutWall * gexRazon)
            };
            if (!double.IsNaN(gexResAplicado.Flip)) niveles.Add(new KeyValuePair<string, double>("Gamma flip", gexResAplicado.Flip * gexRazon));
            foreach (KeyValuePair<string, double> kv in niveles)
            {
                double lvl = kv.Value;
                bool cruza = (prev < lvl && precio >= lvl) || (prev > lvl && precio <= lvl);
                if (!cruza) continue;
                DateTime ult;
                if (ultimoAvisoGex.TryGetValue(kv.Key, out ult) && (DateTime.Now - ult).TotalMinutes < MinutosEntreAvisosGex) continue;
                ultimoAvisoGex[kv.Key] = DateTime.Now;
                Alert("PIFVG_gex_" + kv.Key, Priority.Medium,
                      string.Format(CultureInfo.InvariantCulture, "GEX {0} {1:0.00} tocado", kv.Key, lvl),
                      RutaSonido(SonidoGex), 0, Brushes.Black, Brushes.Orange);
            }
        }

        // ── GEX: descarga en segundo plano ──────────────────────────────────

        void DescargarGexSiToca()
        {
            lock (gexLock)
                if (reintentar) { reintentar = false; proximaDescargaUtc = DateTime.UtcNow.AddMinutes(2); }
            if (!MostrarGEX || gexDescargando || DateTime.UtcNow < proximaDescargaUtc) return;
            gexDescargando = true;
            proximaDescargaUtc = DateTime.UtcNow.AddMinutes(Math.Max(1, GexMinutos));
            DateTime ahoraUtc = DateTime.UtcNow;
            TimeZoneInfo tz = tzNy;
            double maxDias = GexMaxDias;
            int extra = GexStrikesExtra;
            Task.Run(() =>
            {
                try
                {
                    ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                    string json;
                    using (var wc = new WebClient())
                    {
                        wc.Headers.Add("User-Agent", "Mozilla/5.0");
                        json = wc.DownloadString("https://cdn.cboe.com/api/global/delayed_quotes/options/QQQ.json");
                    }
                    var ops = new List<GexOpcion>();
                    double spot = GexCalc.Leer(json, ahoraUtc, tz, maxDias, ops);
                    GexResultado res = GexCalc.Calcular(spot, ops, 0.04, 0.006, extra);
                    res.Calculado = TimeZoneInfo.ConvertTimeFromUtc(ahoraUtc, tz);
                    lock (gexLock) { gexRes = res; gexError = null; }
                }
                catch (Exception ex)
                {
                    lock (gexLock) { gexError = ex.Message; reintentar = true; }
                }
                finally
                {
                    gexDescargando = false;
                }
            });
        }

        // Razón NQ/QQQ: con el precio de NQ de hace 15 minutos en horario regular (CBOE trae QQQ con ese retraso);
        // fuera de horario, con el NQ de las 16:00 NY de la última sesión.
        void AplicarGex()
        {
            GexResultado res;
            lock (gexLock) res = gexRes;
            if (res == null || ReferenceEquals(res, gexResAplicado)) return;
            gexResAplicado = res;
            if (GexRazonManual > 0) { gexRazon = GexRazonManual; gexHora = res.Calculado; return; }
            DateTime refNy = res.Calculado.AddMinutes(-15);
            int m = refNy.Hour * 60 + refNy.Minute;
            bool habil = refNy.DayOfWeek != DayOfWeek.Saturday && refNy.DayOfWeek != DayOfWeek.Sunday;
            if (!(habil && m >= 570 && m <= 960))
            {
                DateTime d = m < 570 ? refNy.Date.AddDays(-1) : refNy.Date;
                while (d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday) d = d.AddDays(-1);
                refNy = d.AddHours(16);
            }
            int idx = BarsArray[1].GetBar(ALocal(refNy));
            double nq = idx >= 0 ? BarsArray[1].GetClose(idx) : Closes[1][0];
            gexRazon = nq / res.Spot;
            gexHora = refNy;
        }

        // ── botones del gráfico ─────────────────────────────────────────────

        void CrearBotones()
        {
            chartWindow = Window.GetWindow(ChartControl.Parent) as NinjaTrader.Gui.Chart.Chart;
            if (chartWindow == null) return;
            string[] nombres = { "IFVG", "CISD", "FVG HTF", "Niveles", "GEX", "Sonido" };
            for (int i = 0; i < nombres.Length; i++)
            {
                int k = i;
                var b = new System.Windows.Controls.Button { Content = nombres[i], Margin = new Thickness(2, 0, 2, 0), Padding = new Thickness(6, 1, 6, 1), Foreground = Brushes.White, FontSize = 11 };
                b.Click += (s, e) => { Alternar(k); PintarBoton(b, Estado(k)); Redibujar(); };
                PintarBoton(b, Estado(k));
                botones.Add(b);
            }
            if (TabSeleccionada()) MostrarBotones();
            chartWindow.MainTabControl.SelectionChanged += CambioDePestana;
        }

        bool Estado(int k)
        {
            switch (k)
            {
                case 0: return MostrarIFVG;
                case 1: return MostrarCISD;
                case 2: return MostrarHTF;
                case 3: return MostrarNiveles;
                case 4: return MostrarGEX;
                default: return Sonido;
            }
        }

        void Alternar(int k)
        {
            switch (k)
            {
                case 0: MostrarIFVG = !MostrarIFVG; break;
                case 1: MostrarCISD = !MostrarCISD; break;
                case 2: MostrarHTF = !MostrarHTF; break;
                case 3: MostrarNiveles = !MostrarNiveles; break;
                case 4: MostrarGEX = !MostrarGEX; break;
                default: Sonido = !Sonido; break;
            }
        }

        static void PintarBoton(System.Windows.Controls.Button b, bool on) { b.Background = on ? bOn : bOff; b.Opacity = on ? 1.0 : 0.6; }

        void MostrarBotones()
        {
            if (botonesVisibles || chartWindow == null) return;
            foreach (System.Windows.Controls.Button b in botones) chartWindow.MainMenu.Add(b);
            botonesVisibles = true;
        }

        void OcultarBotones()
        {
            if (!botonesVisibles || chartWindow == null) return;
            foreach (System.Windows.Controls.Button b in botones) chartWindow.MainMenu.Remove(b);
            botonesVisibles = false;
        }

        void QuitarBotones()
        {
            OcultarBotones();
            if (chartWindow != null) chartWindow.MainTabControl.SelectionChanged -= CambioDePestana;
            botones.Clear();
        }

        bool TabSeleccionada()
        {
            foreach (System.Windows.Controls.TabItem tab in chartWindow.MainTabControl.Items)
            {
                var ct = tab.Content as ChartTab;
                if (ct != null && ct.ChartControl == ChartControl && tab == chartWindow.MainTabControl.SelectedItem) return true;
            }
            return false;
        }

        void CambioDePestana(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count <= 0 || !(e.AddedItems[0] is System.Windows.Controls.TabItem)) return;
            if (TabSeleccionada()) MostrarBotones(); else OcultarBotones();
        }

        // ── dibujo ──────────────────────────────────────────────────────────

        void Tick()
        {
            if (State != State.Realtime) { if (timer != null) { timer.Stop(); timer = null; } return; }
            DescargarGexSiToca();
            Redibujar();
        }

        void Redibujar()
        {
            if (State != State.Realtime) return;
            TriggerCustomEvent(o =>
            {
                try { Dibujar(); }
                catch (Exception ex) { Print("PrimerIFVG: " + ex.Message); }
            }, 0, null);
            ForceRefresh();
        }

        Brush TextoGrafico { get { return ChartControl != null && ChartControl.Properties != null ? ChartControl.Properties.ChartText : Brushes.Gray; } }

        void Caja(string tag, DateTime t1Ny, double top, DateTime t2Ny, double bot, Brush borde, Brush relleno, int opRelleno, DashStyleHelper estilo, int ancho)
        {
            Rectangle r = Draw.Rectangle(this, tag, false, ALocal(t1Ny), top, ALocal(t2Ny), bot, borde, relleno, opRelleno);
            r.OutlineStroke = new Stroke(borde, estilo, ancho);
            r.IsLocked = true;
            tagsAhora.Add(tag);
        }

        void Linea(string tag, DateTime t1Ny, DateTime t2Ny, double y, Brush color, DashStyleHelper estilo, int ancho, bool extender)
        {
            if (extender) Draw.Ray(this, tag, ALocal(t1Ny), y, ALocal(t2Ny), y, color, estilo, ancho).IsAutoScale = false;
            else Draw.Line(this, tag, false, ALocal(t1Ny), y, ALocal(t2Ny), y, color, estilo, ancho);
            tagsAhora.Add(tag);
        }

        void Texto(string tag, DateTime tNy, double y, string txt, Brush color, int offset)
        {
            Draw.Text(this, tag, false, txt, ALocal(tNy), y, offset, color, fuente, TextAlignment.Left, Brushes.Transparent, Brushes.Transparent, 0);
            tagsAhora.Add(tag);
        }

        void Etiqueta(double p, string txt, Brush color, bool arriba)
        {
            etiquetasNuevas.Add(new EtiquetaDer { P = p, Texto = txt, Color = color, Arriba = arriba });
        }

        // Etiquetas al borde derecho, sobre o bajo su precio; si se pisan, se corren hacia abajo.
        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            base.OnRender(chartControl, chartScale);
            List<EtiquetaDer> lista = etiquetasDerecha;
            List<CajaRotulo> cajas = cajasHtf;
            bool hayLista = lista != null && lista.Count > 0, hayCajas = cajas != null && cajas.Count > 0 && ChartBars != null;
            if (IsInHitTest || fuente == null || !(hayLista || hayCajas)) return;
            using (SharpDX.DirectWrite.TextFormat formato = fuente.ToDirectWriteTextFormat())
            using (SharpDX.Direct2D1.Brush fondo = chartControl.Properties.ChartBackground.ToDxBrush(RenderTarget))
            {
                fondo.Opacity = 0.75f;
                if (hayLista) RotulosBordeDerecho(lista, chartScale, formato, fondo);
                if (hayCajas) RotulosHtf(cajas, chartControl, chartScale, formato, fondo);
            }
        }

        // Niveles y GEX: al borde derecho, sobre o bajo su precio; si se pisan, se corren hacia abajo.
        void RotulosBordeDerecho(List<EtiquetaDer> lista, ChartScale chartScale, SharpDX.DirectWrite.TextFormat formato, SharpDX.Direct2D1.Brush fondo)
        {
            var filas = new List<FilaRotulo>();
            foreach (EtiquetaDer e in lista)
            {
                float y = chartScale.GetYByValue(e.P);
                filas.Add(new FilaRotulo { Y = e.Arriba ? y - formato.FontSize - 4 : y + 1, Texto = e.Texto, Color = e.Color });
            }
            filas = filas.OrderBy(f => f.Y).ThenBy(f => f.Texto, StringComparer.Ordinal).ToList();
            // del mismo color y a la misma altura: una sola línea
            for (int i = filas.Count - 1; i > 0; i--)
                if (filas[i].Y - filas[i - 1].Y < 3 && ReferenceEquals(filas[i].Color, filas[i - 1].Color))
                {
                    filas[i - 1].Texto += " · " + filas[i].Texto;
                    filas.RemoveAt(i);
                }
            float ocupadoHasta = float.MinValue;
            foreach (FilaRotulo f in filas)
            {
                using (var layout = new SharpDX.DirectWrite.TextLayout(NinjaTrader.Core.Globals.DirectWriteFactory, f.Texto, formato, 600, formato.FontSize + 6))
                using (SharpDX.Direct2D1.Brush pincel = f.Color.ToDxBrush(RenderTarget))
                {
                    float alto = layout.Metrics.Height, ancho = layout.Metrics.Width;
                    float y = Math.Max(f.Y, ocupadoHasta);
                    if (y > ChartPanel.Y + ChartPanel.H || y + alto < ChartPanel.Y) continue;
                    float x = ChartPanel.X + ChartPanel.W - ancho - 6;
                    RenderTarget.FillRectangle(new SharpDX.RectangleF(x - 2, y, ancho + 4, alto), fondo);
                    RenderTarget.DrawTextLayout(new SharpDX.Vector2(x, y), layout, pincel);
                    ocupadoHasta = y + alto;
                }
            }
        }

        // FVG 15m/1h/4h: su temporalidad escrita en el techo y en el piso de cada caja, por dentro, a la derecha de la
        // última vela. Si un borde queda fuera de la pantalla, el rótulo se queda en el borde de la pantalla; si la caja
        // es muy angosta, un solo rótulo al medio; si dos rótulos chocan, se ponen uno al lado del otro.
        void RotulosHtf(List<CajaRotulo> cajas, ChartControl chartControl, ChartScale chartScale, SharpDX.DirectWrite.TextFormat formato, SharpDX.Direct2D1.Brush fondo)
        {
            float arriba = ChartPanel.Y, abajo = ChartPanel.Y + ChartPanel.H, derecha = ChartPanel.X + ChartPanel.W;
            float x0 = chartControl.GetXByBarIndex(ChartBars, ChartBars.ToIndex) + 12;
            var ocupados = new List<Hueco>();
            foreach (CajaRotulo c in cajas)
            {
                float yTop = chartScale.GetYByValue(c.Top), yBot = chartScale.GetYByValue(c.Bot);
                if (yBot < arriba || yTop > abajo) continue;
                using (var layout = new SharpDX.DirectWrite.TextLayout(NinjaTrader.Core.Globals.DirectWriteFactory, c.Texto, formato, 300, formato.FontSize + 6))
                using (SharpDX.Direct2D1.Brush pincel = c.Color.ToDxBrush(RenderTarget))
                {
                    float alto = layout.Metrics.Height, ancho = layout.Metrics.Width;
                    float techo = Math.Max(yTop, arriba), piso = Math.Min(yBot, abajo);
                    float yA = techo + 1, yB = piso - alto - 1;
                    float[] ys = yB - yA < alto + 2 ? new[] { (techo + piso - alto) / 2 } : new[] { yA, yB };
                    foreach (float y in ys)
                    {
                        float x = x0;
                        bool corrido = true;
                        while (corrido)
                        {
                            corrido = false;
                            foreach (Hueco h in ocupados)
                                if (x < h.X + h.W && x + ancho > h.X && y < h.Y + h.H && y + alto > h.Y) { x = h.X + h.W + 6; corrido = true; }
                        }
                        x = Math.Min(x, derecha - ancho - 2);
                        ocupados.Add(new Hueco { X = x - 2, Y = y, W = ancho + 4, H = alto });
                        RenderTarget.FillRectangle(new SharpDX.RectangleF(x - 2, y, ancho + 4, alto), fondo);
                        RenderTarget.DrawTextLayout(new SharpDX.Vector2(x, y), layout, pincel);
                    }
                }
            }
        }

        // segundos que faltan para que cierre la vela en formación (con el reloj de NinjaTrader, que respeta Playback)
        string Quedan(DateTime finNy)
        {
            int seg = (int)Math.Ceiling((finNy - relojNy).TotalSeconds);
            return "quedan " + Math.Max(0, seg) + " s";
        }

        static string P(double v) { return v.ToString("0.00", CultureInfo.InvariantCulture); }
        static string H(DateTime t) { return t.ToString("HH:mm", CultureInfo.InvariantCulture); }

        void Dibujar()
        {
            if (motor == null || CurrentBars[0] < 1 || CurrentBars[1] < 1) return;
            tagsAhora = new HashSet<string>();
            etiquetasNuevas = new List<EtiquetaDer>();
            cajasNuevas = new List<CajaRotulo>();
            relojNy = ANy(Core.Globals.Now);
            double precio = Closes[0][0];
            DateTime ahoraNy = ANy(Times[1][0]);                 // cierre de la vela de 1m en curso
            DateTime tUlt = ahoraNy.AddMinutes(-1);              // su apertura
            DateTime finNy = ANy(Times[0][0]);
            bool[] on = TfOn();

            List<Fvg> conf;
            List<CisdEvento> cisdConf;
            var prevI = new List<Fvg>();
            var prevC = new List<CisdEvento>();
            List<FvgHtf> htf = null;
            List<Nivel> niveles = null;
            string conteo = null;
            DateTime diaNy;
            lock (motorLock)
            {
                conf = motor.IfvgCandidatos(ahoraNy, on, MostrarMuertos, OcultarIfvgMinutos);
                cisdConf = motor.CisdCandidatos(on);
                if (VistaPrevia) motor.Previa(precio, tUlt, on, DedupMinutos, DedupPuntos, conf, cisdConf, prevI, prevC);
                if (MostrarHTF) htf = motor.HtfSeleccion(precio, Usar15m, Usar1h, Usar4h, HtfCercanos, HtfOpuestos);
                if (MostrarHTF && ConteoHtf) conteo = motor.HtfConteo(Usar15m, Usar1h, Usar4h);
                if (MostrarNiveles) niveles = motor.Liq.Niveles.Where(L => !L.Oculto && !L.Tomado).ToList();
                diaNy = motor.DayStart;
            }

            if (htf != null) DibujarHtf(htf, precio, finNy);
            if (niveles != null) DibujarNiveles(niveles, precio);
            if (MostrarIFVG) DibujarIfvg(conf, prevI, tUlt);
            if (MostrarCISD) DibujarCisd(cisdConf, prevC);
            AplicarGex();
            string estadoGex = MostrarGEX ? DibujarGex(precio, finNy, diaNy) : null;

            // arriba a la derecha: estado del GEX y cuántos FVG 15m/1h/4h hay vivos
            string estado = string.Join("\n", new[] { estadoGex, conteo == null ? null : "FVG vivos · " + conteo }.Where(x => x != null));
            if (estado.Length > 0)
            {
                Draw.TextFixed(this, "PIFVG_estado", estado, TextPosition.TopRight, TextoGrafico, fuente, Brushes.Transparent, Brushes.Transparent, 0);
                tagsAhora.Add("PIFVG_estado");
            }

            foreach (string tag in tagsPrevios) if (!tagsAhora.Contains(tag)) RemoveDrawObject(tag);
            tagsPrevios = tagsAhora;
            etiquetasDerecha = etiquetasNuevas;
            cajasHtf = cajasNuevas;
        }

        void DibujarIfvg(List<Fvg> conf, List<Fvg> previa, DateTime tUlt)
        {
            foreach (Fvg f in conf)
            {
                if (Motor.TapadoIfvg(f, f.TInv, conf, g => g.TInv, DedupMinutos)) continue;
                int dirI = -f.Dir;
                bool vivo = f.Estado == 1;
                Brush col = vivo ? (dirI == 1 ? ColorIfvgAlcista : ColorIfvgBajista) : Brushes.Gray;
                Caja("PIFVG_i" + f.Id, f.TBirth, f.Top, f.TcInv, f.Bot, ConOpacidad(col, 65), col, 14, DashStyleHelper.Solid, 1);
                Texto("PIFVG_it" + f.Id, f.TcInv, dirI == 1 ? f.Bot : f.Top, "IFVG " + f.Tf + "m", col, dirI == 1 ? -10 : 10);
                if (MarcarCE) Linea("PIFVG_ic" + f.Id, f.TBirth, f.TcInv, f.Ce, ConOpacidad(col, 65), DashStyleHelper.Dash, 1, false);
            }
            foreach (Fvg f in previa)
            {
                int dirI = -f.Dir;
                Brush col = dirI == 1 ? ColorIfvgAlcista : ColorIfvgBajista;
                DateTime fin = Motor.FinVelaAbierta(tUlt, f.Tf);
                Caja("PIFVG_p" + f.Id, f.TBirth, f.Top, fin, f.Bot, ConOpacidad(col, 80), col, 0, DashStyleHelper.Dot, 1);
                Texto("PIFVG_pt" + f.Id, fin, dirI == 1 ? f.Bot : f.Top, "IFVG " + f.Tf + "m en formación · " + Quedan(fin), col, dirI == 1 ? -10 : 10);
                if (MarcarCE) Linea("PIFVG_pc" + f.Id, f.TBirth, fin, f.Ce, ConOpacidad(col, 60), DashStyleHelper.Dash, 1, false);
            }
        }

        void DibujarCisd(List<CisdEvento> conf, List<CisdEvento> previa)
        {
            foreach (CisdEvento e in conf)
            {
                if (Motor.TapadoCisd(e, conf, DedupMinutos, DedupPuntos)) continue;
                Brush col = e.Dir == 1 ? ColorIfvgAlcista : ColorIfvgBajista;
                string id = "PIFVG_c" + e.Tf + (e.Dir == 1 ? "a" : "b");
                Linea(id, e.TStart, e.T, e.Lvl, col, DashStyleHelper.Solid, 1, false);
                Texto(id + "t", e.T, e.Lvl, "CISD " + e.Tf + "m", col, e.Dir == 1 ? -10 : 10);
            }
            foreach (CisdEvento e in previa)
            {
                Brush col = e.Dir == 1 ? ColorIfvgAlcista : ColorIfvgBajista;
                string id = "PIFVG_cp" + e.Tf + (e.Dir == 1 ? "a" : "b");
                Linea(id, e.TStart, e.T, e.Lvl, col, DashStyleHelper.Dot, 1, false);
                Texto(id + "t", e.T, e.Lvl, "CISD " + e.Tf + "m en formación · " + Quedan(e.T), col, e.Dir == 1 ? -10 : 10);
            }
        }

        // Como en TradingView: la caja sigue hasta el borde derecho del gráfico. Cada TF tiene su grosor y su opacidad de
        // contorno, y su nombre ("FVG 15m #1 ▲") escrito en el techo y en el piso (RotulosHtf). Solo va rellena la caja más angosta
        // que contiene el precio; las demás quedan con su contorno, para que no se forme una mancha al superponerse.
        void DibujarHtf(List<FvgHtf> sel, double precio, DateTime finNy)
        {
            DateTime derecha = finNy + TimeSpan.FromTicks(barra.Ticks * 500);     // más allá del margen derecho
            FvgHtf rellena = null;
            foreach (FvgHtf f in sel)
                if (f.Bot <= precio && f.Top >= precio && (rellena == null || f.Top - f.Bot < rellena.Top - rellena.Bot)) rellena = f;
            Dictionary<FvgHtf, int> num = Motor.NumerarHtf(sel);
            // 4h primero: sus rótulos quedan pegados a la última vela y los de 1h y 15m, si chocan, al lado
            foreach (FvgHtf f in sel.OrderBy(x => x.Tf == "4h" ? 0 : x.Tf == "1h" ? 1 : 2))
            {
                Brush col = f.Dir == 1 ? ColorHtfAlcista : ColorHtfBajista;
                int op = f.Tf == "15m" ? Contorno15m : f.Tf == "1h" ? Contorno1h : Contorno4h;
                int grosor = f.Tf == "15m" ? Grosor15m : f.Tf == "1h" ? Grosor1h : Grosor4h;
                Caja("PIFVG_h" + f.Id, f.TBirth, f.Top, derecha, f.Bot, ConOpacidad(ColorContorno, op), col, f == rellena ? HtfOpacidadRelleno : 0, DashStyleHelper.Solid, grosor);
                cajasNuevas.Add(new CajaRotulo { Top = f.Top, Bot = f.Bot, Texto = "FVG " + f.Tf + " #" + num[f] + (f.Dir == 1 ? " ▲" : " ▼"), Color = col });
                if (CeEnHtf) Linea("PIFVG_hc" + f.Id, f.TBirth, f.TBirth + barra, (f.Top + f.Bot) / 2, ConOpacidad(col, 60), DashStyleHelper.Dash, 1, true);
            }
        }

        static string NombreNivel(string k)
        {
            switch (k)
            {
                case "ASIA_H": return "Asia H";
                case "ASIA_L": return "Asia L";
                case "LON_H": return "Londres H";
                case "LON_L": return "Londres L";
                case "PM_H": return "Pre-mkt H";
                case "PM_L": return "Pre-mkt L";
                case "SW15_H": case "SW15_L": return "Swing 15m";
                case "SW5_H": case "SW5_L": return "Swing 5m";
                default: return k;
            }
        }

        static Brush ColorNivel(string k)
        {
            if (k == "PDH" || k == "PDL") return cPd;
            if (k.StartsWith("EQ")) return cEq;
            if (k.StartsWith("SW")) return cSw;
            return cSes;
        }

        void DibujarNiveles(List<Nivel> niveles, double precio)
        {
            var visibles = new List<Nivel>();
            foreach (Nivel L in niveles)
            {
                bool mostrar = L.Tipo.StartsWith("SW5") ? MostrarSwings5
                             : L.Tipo.StartsWith("SW15") || L.Tipo.StartsWith("EQ") ? MostrarSwings15
                             : L.Tipo == "PDH" || L.Tipo == "PDL" ? MostrarPdhPdl : MostrarSesiones;
                if (!mostrar) continue;
                visibles.Add(L);
                Linea("PIFVG_n" + L.Id, L.T, L.T + barra, L.P, ConOpacidad(ColorNivel(L.Tipo), 85), L.Htf ? DashStyleHelper.Solid : DashStyleHelper.Dot, 1, true);
            }
            // etiquetas solo en los N más cercanos por arriba y por abajo
            var arriba = visibles.Where(L => L.P > precio).OrderBy(L => L.P).Take(EtiquetasNiveles);
            var abajo = visibles.Where(L => L.P <= precio).OrderByDescending(L => L.P).Take(EtiquetasNiveles);
            foreach (Nivel L in arriba.Concat(abajo))
                Etiqueta(L.P, NombreNivel(L.Tipo) + " " + P(L.P), ColorNivel(L.Tipo), true);
        }

        // dibuja los niveles GEX y devuelve el texto de estado
        string DibujarGex(double precio, DateTime finNy, DateTime diaNy)
        {
            string error;
            lock (gexLock) error = gexError;
            GexResultado r = gexResAplicado;
            string estado;
            if (r == null || double.IsNaN(gexRazon))
                estado = error != null ? "GEX: sin datos (" + error + ")" : "GEX: descargando QQQ de CBOE…";
            else
            {
                DateTime desde = diaNy > DateTime.MinValue ? diaNy : finNy.AddDays(-1);
                double cw = r.CallWall * gexRazon, pw = r.PutWall * gexRazon, fl = r.Flip * gexRazon;
                Linea("PIFVG_gcw", desde, desde + barra, cw, ColorCallWall, DashStyleHelper.Solid, 2, true);
                Etiqueta(cw, "GEX Call wall " + P(cw), ColorCallWall, true);
                Linea("PIFVG_gpw", desde, desde + barra, pw, ColorPutWall, DashStyleHelper.Solid, 2, true);
                Etiqueta(pw, "GEX Put wall " + P(pw), ColorPutWall, true);
                if (!double.IsNaN(r.Flip))
                {
                    Linea("PIFVG_gfl", desde, desde + barra, fl, ColorFlip, DashStyleHelper.Dash, 2, true);
                    Etiqueta(fl, "GEX Gamma flip " + P(fl), ColorFlip, true);
                }
                for (int i = 0; i < r.Extra.Count; i++)
                {
                    double v = r.Extra[i] * gexRazon;
                    Linea("PIFVG_gx" + i, desde, desde + barra, v, ConOpacidad(ColorGexOtros, 70), DashStyleHelper.Dot, 1, true);
                    Etiqueta(v, "GEX " + P(v), ColorGexOtros, true);
                }
                string regimen = double.IsNaN(r.Flip) ? (r.Total >= 0 ? "gamma + (sin flip cercano)" : "gamma − (sin flip cercano)")
                               : precio >= fl ? "sobre el flip: gamma + (rango, reversión)" : "bajo el flip: gamma − (tendencia, más volatilidad)";
                string razon = "razón NQ/QQQ " + gexRazon.ToString("0.000", CultureInfo.InvariantCulture) + (GexRazonManual > 0 ? " (manual)" : " (NQ de las " + H(gexHora) + ")");
                estado = "GEX QQQ " + H(r.Calculado) + " NY · " + razon + " · " + regimen
                         + (error != null ? " · última descarga falló: " + error : "");
            }
            return estado;
        }

        #region Propiedades

        [Display(Name = "IFVG", Order = 1, GroupName = "0. Mostrar (también con los botones del gráfico)")]
        public bool MostrarIFVG { get; set; }
        [Display(Name = "CISD", Order = 2, GroupName = "0. Mostrar (también con los botones del gráfico)")]
        public bool MostrarCISD { get; set; }
        [Display(Name = "FVG 15m / 1h / 4h", Order = 3, GroupName = "0. Mostrar (también con los botones del gráfico)")]
        public bool MostrarHTF { get; set; }
        [Display(Name = "Niveles de liquidez", Order = 4, GroupName = "0. Mostrar (también con los botones del gráfico)")]
        public bool MostrarNiveles { get; set; }
        [Display(Name = "GEX", Order = 5, GroupName = "0. Mostrar (también con los botones del gráfico)")]
        public bool MostrarGEX { get; set; }
        [Display(Name = "Sonido", Order = 6, GroupName = "0. Mostrar (también con los botones del gráfico)")]
        public bool Sonido { get; set; }

        [Display(Name = "Señal en 1m", Order = 1, GroupName = "1. Señal IFVG / CISD")]
        public bool Usar1m { get; set; }
        [Display(Name = "Señal en 2m", Order = 2, GroupName = "1. Señal IFVG / CISD")]
        public bool Usar2m { get; set; }
        [Display(Name = "Señal en 3m", Order = 3, GroupName = "1. Señal IFVG / CISD")]
        public bool Usar3m { get; set; }
        [Display(Name = "Señal en 4m", Order = 4, GroupName = "1. Señal IFVG / CISD")]
        public bool Usar4m { get; set; }
        [Display(Name = "Señal en 5m", Order = 5, GroupName = "1. Señal IFVG / CISD")]
        public bool Usar5m { get; set; }
        [Range(0, int.MaxValue)]
        [Display(Name = "Tamaño mínimo del FVG (ticks)", Order = 6, GroupName = "1. Señal IFVG / CISD")]
        public int FvgMinTicks { get; set; }
        [Range(0, int.MaxValue)]
        [Display(Name = "Máx. velas (de su TF) para invertirse; 0 = sin límite", Order = 7, GroupName = "1. Señal IFVG / CISD")]
        public int MaxVelasInv { get; set; }
        [Display(Name = "Límite uniforme en minutos (en vez de velas)", Order = 8, GroupName = "1. Señal IFVG / CISD")]
        public bool LimiteEnMinutos { get; set; }
        [Range(0, int.MaxValue)]
        [Display(Name = "Máx. minutos para invertirse; 0 = sin límite", Order = 9, GroupName = "1. Señal IFVG / CISD")]
        public int MaxMinutosInv { get; set; }
        [Range(1, 10)]
        [Display(Name = "Velas a cada lado para pivotes (CISD, swings 5m)", Order = 10, GroupName = "1. Señal IFVG / CISD")]
        public int VelasPivote { get; set; }

        [Display(Name = "Vista previa en formación (punteado)", Order = 1, GroupName = "2. Visual IFVG / CISD")]
        public bool VistaPrevia { get; set; }
        [Display(Name = "Dejar los IFVG muertos del día (gris)", Order = 2, GroupName = "2. Visual IFVG / CISD")]
        public bool MostrarMuertos { get; set; }
        [Range(0, int.MaxValue)]
        [Display(Name = "Ocultar IFVG tras N minutos de invertirse (0 = mientras vivan)", Order = 3, GroupName = "2. Visual IFVG / CISD")]
        public int OcultarIfvgMinutos { get; set; }
        [Range(1, int.MaxValue)]
        [Display(Name = "Deduplicar entre TFs: ventana (minutos)", Order = 4, GroupName = "2. Visual IFVG / CISD")]
        public int DedupMinutos { get; set; }
        [Range(0, double.MaxValue)]
        [Display(Name = "Deduplicar CISD: niveles a menos de (puntos)", Order = 5, GroupName = "2. Visual IFVG / CISD")]
        public double DedupPuntos { get; set; }
        [Display(Name = "Marcar el CE (50 %)", Order = 6, GroupName = "2. Visual IFVG / CISD")]
        public bool MarcarCE { get; set; }
        [Range(6, 30)]
        [Display(Name = "Tamaño del texto", Order = 7, GroupName = "2. Visual IFVG / CISD")]
        public int TamanoTexto { get; set; }
        [XmlIgnore]
        [Display(Name = "Color alcista (IFVG y CISD)", Order = 8, GroupName = "2. Visual IFVG / CISD")]
        public Brush ColorIfvgAlcista { get; set; }
        [Browsable(false)]
        public string ColorIfvgAlcistaSerializable { get { return Serialize.BrushToString(ColorIfvgAlcista); } set { ColorIfvgAlcista = Serialize.StringToBrush(value); } }
        [XmlIgnore]
        [Display(Name = "Color bajista (IFVG y CISD)", Order = 9, GroupName = "2. Visual IFVG / CISD")]
        public Brush ColorIfvgBajista { get; set; }
        [Browsable(false)]
        public string ColorIfvgBajistaSerializable { get { return Serialize.BrushToString(ColorIfvgBajista); } set { ColorIfvgBajista = Serialize.StringToBrush(value); } }

        [Display(Name = "15m", Order = 1, GroupName = "3. FVG 15m / 1h / 4h")]
        public bool Usar15m { get; set; }
        [Display(Name = "1h", Order = 2, GroupName = "3. FVG 15m / 1h / 4h")]
        public bool Usar1h { get; set; }
        [Display(Name = "4h", Order = 3, GroupName = "3. FVG 15m / 1h / 4h")]
        public bool Usar4h { get; set; }
        [Range(0, 6)]
        [Display(Name = "Los N más cercanos por arriba y por abajo", Order = 4, GroupName = "3. FVG 15m / 1h / 4h")]
        public int HtfCercanos { get; set; }
        [Display(Name = "Siempre el bajista más cercano por arriba y el alcista más cercano por abajo", Order = 5, GroupName = "3. FVG 15m / 1h / 4h")]
        public bool HtfOpuestos { get; set; }
        [Display(Name = "Mostrar cuántos hay vivos (arriba a la derecha)", Order = 6, GroupName = "3. FVG 15m / 1h / 4h")]
        public bool ConteoHtf { get; set; }
        [Range(0, 100)]
        [Display(Name = "Opacidad del relleno de la caja donde está el precio (%)", Order = 7, GroupName = "3. FVG 15m / 1h / 4h")]
        public int HtfOpacidadRelleno { get; set; }
        [XmlIgnore]
        [Display(Name = "Color alcista", Order = 8, GroupName = "3. FVG 15m / 1h / 4h")]
        public Brush ColorHtfAlcista { get; set; }
        [Browsable(false)]
        public string ColorHtfAlcistaSerializable { get { return Serialize.BrushToString(ColorHtfAlcista); } set { ColorHtfAlcista = Serialize.StringToBrush(value); } }
        [XmlIgnore]
        [Display(Name = "Color bajista", Order = 9, GroupName = "3. FVG 15m / 1h / 4h")]
        public Brush ColorHtfBajista { get; set; }
        [Browsable(false)]
        public string ColorHtfBajistaSerializable { get { return Serialize.BrushToString(ColorHtfBajista); } set { ColorHtfBajista = Serialize.StringToBrush(value); } }
        [XmlIgnore]
        [Display(Name = "Color del contorno", Order = 10, GroupName = "3. FVG 15m / 1h / 4h")]
        public Brush ColorContorno { get; set; }
        [Browsable(false)]
        public string ColorContornoSerializable { get { return Serialize.BrushToString(ColorContorno); } set { ColorContorno = Serialize.StringToBrush(value); } }
        [Range(0, 100)]
        [Display(Name = "Opacidad del contorno 15m (%)", Order = 11, GroupName = "3. FVG 15m / 1h / 4h")]
        public int Contorno15m { get; set; }
        [Range(0, 100)]
        [Display(Name = "Opacidad del contorno 1h (%)", Order = 12, GroupName = "3. FVG 15m / 1h / 4h")]
        public int Contorno1h { get; set; }
        [Range(0, 100)]
        [Display(Name = "Opacidad del contorno 4h (%)", Order = 13, GroupName = "3. FVG 15m / 1h / 4h")]
        public int Contorno4h { get; set; }
        [Range(1, 6)]
        [Display(Name = "Grosor del contorno 15m (px)", Order = 14, GroupName = "3. FVG 15m / 1h / 4h")]
        public int Grosor15m { get; set; }
        [Range(1, 6)]
        [Display(Name = "Grosor del contorno 1h (px)", Order = 15, GroupName = "3. FVG 15m / 1h / 4h")]
        public int Grosor1h { get; set; }
        [Range(1, 6)]
        [Display(Name = "Grosor del contorno 4h (px)", Order = 16, GroupName = "3. FVG 15m / 1h / 4h")]
        public int Grosor4h { get; set; }
        [Display(Name = "Marcar el CE (50 %)", Order = 17, GroupName = "3. FVG 15m / 1h / 4h")]
        public bool CeEnHtf { get; set; }

        [Display(Name = "PDH / PDL", Order = 1, GroupName = "4. Liquidez")]
        public bool MostrarPdhPdl { get; set; }
        [Display(Name = "Sesiones (Asia, Londres, pre-market)", Order = 2, GroupName = "4. Liquidez")]
        public bool MostrarSesiones { get; set; }
        [Display(Name = "Swings 15m y EQH/EQL", Order = 3, GroupName = "4. Liquidez")]
        public bool MostrarSwings15 { get; set; }
        [Display(Name = "Swings 5m", Order = 4, GroupName = "4. Liquidez")]
        public bool MostrarSwings5 { get; set; }
        [Range(0, 10)]
        [Display(Name = "Etiquetas en los N más cercanos por lado", Order = 5, GroupName = "4. Liquidez")]
        public int EtiquetasNiveles { get; set; }
        [Display(Name = "Asia (hora NY, HHMM-HHMM)", Order = 6, GroupName = "4. Liquidez")]
        public string SesionAsia { get; set; }
        [Display(Name = "Londres (hora NY, HHMM-HHMM)", Order = 7, GroupName = "4. Liquidez")]
        public string SesionLondres { get; set; }
        [Display(Name = "Pre-market NY (hora NY, HHMM-HHMM)", Order = 8, GroupName = "4. Liquidez")]
        public string SesionPremarket { get; set; }

        [Range(1, 240)]
        [Display(Name = "Actualizar cada (minutos)", Order = 1, GroupName = "5. GEX (QQQ, CBOE)")]
        public int GexMinutos { get; set; }
        [Range(1, 365)]
        [Display(Name = "Vencimientos incluidos (días)", Order = 2, GroupName = "5. GEX (QQQ, CBOE)")]
        public int GexMaxDias { get; set; }
        [Range(0, 10)]
        [Display(Name = "Strikes extra con más GEX", Order = 3, GroupName = "5. GEX (QQQ, CBOE)")]
        public int GexStrikesExtra { get; set; }
        [Range(0, double.MaxValue)]
        [Display(Name = "Razón NQ/QQQ manual (0 = automática)", Order = 4, GroupName = "5. GEX (QQQ, CBOE)")]
        public double GexRazonManual { get; set; }
        [XmlIgnore]
        [Display(Name = "Color call wall", Order = 5, GroupName = "5. GEX (QQQ, CBOE)")]
        public Brush ColorCallWall { get; set; }
        [Browsable(false)]
        public string ColorCallWallSerializable { get { return Serialize.BrushToString(ColorCallWall); } set { ColorCallWall = Serialize.StringToBrush(value); } }
        [XmlIgnore]
        [Display(Name = "Color put wall", Order = 6, GroupName = "5. GEX (QQQ, CBOE)")]
        public Brush ColorPutWall { get; set; }
        [Browsable(false)]
        public string ColorPutWallSerializable { get { return Serialize.BrushToString(ColorPutWall); } set { ColorPutWall = Serialize.StringToBrush(value); } }
        [XmlIgnore]
        [Display(Name = "Color gamma flip", Order = 7, GroupName = "5. GEX (QQQ, CBOE)")]
        public Brush ColorFlip { get; set; }
        [Browsable(false)]
        public string ColorFlipSerializable { get { return Serialize.BrushToString(ColorFlip); } set { ColorFlip = Serialize.StringToBrush(value); } }
        [XmlIgnore]
        [Display(Name = "Color otros strikes", Order = 8, GroupName = "5. GEX (QQQ, CBOE)")]
        public Brush ColorGexOtros { get; set; }
        [Browsable(false)]
        public string ColorGexOtrosSerializable { get { return Serialize.BrushToString(ColorGexOtros); } set { ColorGexOtros = Serialize.StringToBrush(value); } }

        [Display(Name = "Sonido IFVG (3m o más)", Order = 1, GroupName = "6. Sonido")]
        public string SonidoIfvg { get; set; }
        [Display(Name = "Sonido nivel GEX tocado", Order = 2, GroupName = "6. Sonido")]
        public string SonidoGex { get; set; }
        [Range(0, 240)]
        [Display(Name = "Minutos entre avisos del mismo nivel GEX", Order = 3, GroupName = "6. Sonido")]
        public int MinutosEntreAvisosGex { get; set; }

        #endregion
    }
}

// ===== NUCLEO INICIO =====
// Va en su propio espacio de nombres, fuera de NinjaTrader.NinjaScript.Indicators y después del indicador:
// NinjaTrader arma el bloque "NinjaScript generated code" con el primer namespace del archivo.
namespace PrimerIFVGNucleo
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text.RegularExpressions;

    public class Vela { public double O, H, L, C; public DateTime T, Tc; public int Seq; }

    public class Fvg
    {
        public int Id, Tf, Dir;            // Dir: 1 FVG alcista, -1 bajista (el IFVG va en dirección contraria)
        public double Top, Bot, Ce;
        public DateTime TBirth, TcBirth;   // apertura de la vela 1 · cierre de la vela 3
        public int SeqBirth;
        public int Estado;                 // 0 vivo · 1 invertido (IFVG) · 2 muerto · 3 caducado sin invertirse
        public DateTime TInv, TcInv, TMuerte;
    }

    public class CisdNivel { public bool Armado; public double Lvl; public DateTime TStart; }

    public class CisdEvento { public DateTime T, TStart; public int Dir, Tf; public double Lvl; }

    public class FvgHtf { public int Id; public string Tf; public int Dir; public double Top, Bot; public DateTime TBirth; public bool Vivo = true; }

    public class Nivel { public int Id; public string Tipo; public double P; public int Lado; public DateTime T; public bool Htf, Tomado, Oculto; }

    public class ConfigMotor
    {
        public int VelasPivote = 2;
        public double FvgMinPuntos = 0;
        public int MaxVelasInv = 60;
        public bool LimiteEnMinutos = false;
        public int MaxMinutosInv = 60;
        public double EqTolPuntos = 1.0;
        public double SepAtr = 0.5;
        public int AsiaIni = 19 * 60, AsiaFin = 1 * 60, LonIni = 2 * 60, LonFin = 6 * 60, PmIni = 7 * 60, PmFin = 9 * 60 + 30;
    }

    public static class Utiles
    {
        public static bool EnSesion(int m, int a, int b) { return a <= b ? (m >= a && m < b) : (m >= a || m < b); }

        public static bool EsPivote(List<Vela> v, int mid, int L, int dir)
        {
            Vela m = v[mid];
            for (int i = mid - L; i <= mid + L; i++)
            {
                if (i == mid) continue;
                Vela a = v[i];
                bool rompe = dir == 1 ? (i < mid ? a.H >= m.H : a.H > m.H) : (i < mid ? a.L <= m.L : a.L < m.L);
                if (rompe) return false;
            }
            return true;
        }

        // "1900-0100" → (1140, 60)
        public static bool LeerSesion(string s, out int a, out int b)
        {
            a = b = 0;
            if (s == null) return false;
            string[] p = s.Trim().Split('-');
            if (p.Length != 2 || p[0].Length != 4 || p[1].Length != 4) return false;
            int h1, m1, h2, m2;
            if (!int.TryParse(p[0].Substring(0, 2), out h1) || !int.TryParse(p[0].Substring(2, 2), out m1)) return false;
            if (!int.TryParse(p[1].Substring(0, 2), out h2) || !int.TryParse(p[1].Substring(2, 2), out m2)) return false;
            a = h1 * 60 + m1; b = h2 * 60 + m2;
            return true;
        }
    }

    // Arma velas de N minutos con velas de 1 minuto, alineadas al reloj de Nueva York.
    // Desfase: minuto del día desde el que se cuentan los bloques (las de 4h empiezan a las 18:00).
    // Si faltan minutos (sin operaciones o pausa del mercado), la vela se cierra al empezar el bloque siguiente.
    public class Agregador
    {
        static readonly DateTime Origen = new DateTime(2000, 1, 1);
        public readonly int N, Desfase;
        bool abierta; double o, h, l, c; DateTime t, tcUlt; long bloque;
        public int SeqN;
        public double Atr = double.NaN;
        public readonly List<Vela> Velas = new List<Vela>();

        public Agregador(int n, int desfase) { N = n; Desfase = desfase; }

        public List<Vela> Alimentar(double o1, double h1, double l1, double c1, DateTime t1, DateTime tc1)
        {
            var cerradas = new List<Vela>(2);
            long abs = (long)Math.Floor((t1 - Origen).TotalMinutes) - Desfase;
            long b = abs >= 0 ? abs / N : (abs - N + 1) / N;
            if (abierta && b != bloque) cerradas.Add(Cerrar(tcUlt));
            if (!abierta) { abierta = true; bloque = b; o = o1; h = h1; l = l1; c = c1; t = t1; }
            else { h = Math.Max(h, h1); l = Math.Min(l, l1); c = c1; }
            tcUlt = tc1;
            if ((abs + 1) % N == 0) cerradas.Add(Cerrar(tc1));
            return cerradas;
        }

        Vela Cerrar(DateTime tc)
        {
            SeqN++;
            var v = new Vela { O = o, H = h, L = l, C = c, T = t, Tc = tc, Seq = SeqN };
            if (Velas.Count > 0)
            {
                Vela p = Velas[Velas.Count - 1];
                double tr = Math.Max(v.H - v.L, Math.Max(Math.Abs(v.H - p.C), Math.Abs(v.L - p.C)));
                Atr = double.IsNaN(Atr) ? tr : (Atr * 13 + tr) / 14;
            }
            Velas.Add(v);
            if (Velas.Count > 400) Velas.RemoveAt(0);
            abierta = false;
            return v;
        }
    }

    // FVG / IFVG y CISD de una temporalidad de señal (1–5m)
    public class MotorTF
    {
        public readonly int N;
        public readonly Agregador Agg;
        public readonly List<Fvg> Fvgs = new List<Fvg>();
        public readonly CisdNivel CBear = new CisdNivel(), CBull = new CisdNivel();
        public CisdEvento UltAlc, UltBaj;
        readonly Motor motor;

        public MotorTF(Motor m, int n) { motor = m; N = n; Agg = new Agregador(n, 0); }

        public void Procesar(Vela c)
        {
            List<Vela> v = Agg.Velas;
            int n = v.Count;
            if (n < 3) return;
            ConfigMotor cfg = motor.Cfg;
            int L = cfg.VelasPivote;
            // 1. pivotes (L velas a cada lado) → nivel CISD y swings de 5m
            int mid = n - 1 - L;
            if (mid >= L)
            {
                Vela m = v[mid];
                if (Utiles.EsPivote(v, mid, L, 1))
                {
                    ArmarCisd(mid, 1);
                    if (N == 5) motor.Liq.AgregarSwing(m.H, 1, false, m.T, Agg.Atr, "SW5_H");
                }
                if (Utiles.EsPivote(v, mid, L, -1))
                {
                    ArmarCisd(mid, -1);
                    if (N == 5) motor.Liq.AgregarSwing(m.L, -1, false, m.T, Agg.Atr, "SW5_L");
                }
            }
            // 2. CISD: ¿cerró a través del nivel armado?
            if (CBear.Armado && c.C < CBear.Lvl) Disparar(CBear, c, 1);
            if (CBull.Armado && c.C > CBull.Lvl) Disparar(CBull, c, -1);
            // 3. FVG nuevo con las velas n-3, n-2 y n-1 (mechas de la 1 y la 3 sin solaparse)
            Vela c1 = v[n - 3];
            int dirF = c1.H < c.L ? 1 : c1.L > c.H ? -1 : 0;
            if (dirF != 0)
            {
                double top = dirF == 1 ? c.L : c1.L, bot = dirF == 1 ? c1.H : c.H;
                if (top - bot >= cfg.FvgMinPuntos && c1.T >= motor.DayStart)
                    Fvgs.Add(new Fvg { Id = motor.NuevoId(), Tf = N, Dir = dirF, Top = top, Bot = bot, Ce = (top + bot) / 2, TBirth = c1.T, TcBirth = c.Tc, SeqBirth = c.Seq });
            }
            // 4. estado de cada FVG: caducidad, inversión (cierre a través de todo el gap) y muerte (cierre a través del CE en contra)
            foreach (Fvg f in Fvgs)
            {
                if (f.SeqBirth >= c.Seq) continue;
                if (f.Estado == 0)
                {
                    bool caduca = cfg.LimiteEnMinutos
                        ? (cfg.MaxMinutosInv > 0 && (c.Tc - f.TcBirth).TotalMinutes > cfg.MaxMinutosInv)
                        : (cfg.MaxVelasInv > 0 && c.Seq - f.SeqBirth > cfg.MaxVelasInv);
                    if (caduca) f.Estado = 3;
                }
                if (f.Estado == 0)
                {
                    bool inv = f.Dir == 1 ? c.C < f.Bot : c.C > f.Top;
                    if (inv) { f.Estado = 1; f.TInv = c.T; f.TcInv = c.Tc; motor.NuevosIfvg.Add(f); }
                }
                if (f.Estado == 1 && f.TcInv != c.Tc)
                {
                    bool muere = -f.Dir == 1 ? c.C < f.Ce : c.C > f.Ce;
                    if (muere) { f.Estado = 2; f.TMuerte = c.Tc; }
                }
            }
            // 5. limpieza
            if (Fvgs.Count > 40)
            {
                Fvgs.RemoveAll(f => f.Estado == 2 || f.Estado == 3 || (f.Estado == 0 && f.TBirth < motor.DayStart));
                while (Fvgs.Count > 40) Fvgs.RemoveAt(0);
            }
        }

        // CISD según TTrades: la serie es la racha de velas que creó el swing; el nivel es el open de su
        // primera vela y queda armado hasta que una vela CIERRA a través.
        void ArmarCisd(int mid, int dirPiv)
        {
            List<Vela> v = Agg.Velas;
            Vela m = v[mid];
            int i = mid;
            bool up = m.C > m.O;
            if ((dirPiv == 1 && !up) || (dirPiv == -1 && up)) i = mid - 1;
            int first = -1;
            while (i >= 0)
            {
                Vela a = v[i];
                bool enSerie = dirPiv == 1 ? a.C > a.O : a.C < a.O;
                if (!enSerie) break;
                first = i;
                i--;
            }
            if (first < 0) return;
            Vela f0 = v[first];
            CisdNivel cl = dirPiv == 1 ? CBear : CBull;
            cl.Armado = true; cl.Lvl = f0.O; cl.TStart = f0.T;
            for (int j = mid + 1; j < v.Count && cl.Armado; j++)
            {
                Vela b = v[j];
                if (dirPiv == 1 ? b.C < cl.Lvl : b.C > cl.Lvl) Disparar(cl, b, dirPiv);
            }
        }

        void Disparar(CisdNivel cl, Vela b, int dirPiv)
        {
            cl.Armado = false;
            var e = new CisdEvento { T = b.Tc, Dir = -dirPiv, Tf = N, Lvl = cl.Lvl, TStart = cl.TStart };
            motor.NuevosCisd.Add(e);
            if (e.Dir == 1) UltAlc = e; else UltBaj = e;
        }
    }

    // Niveles de liquidez: PDH/PDL, sesiones, swings 15m/5m y EQH/EQL. Un nivel tocado se oculta.
    public class Liquidez
    {
        public readonly List<Nivel> Niveles = new List<Nivel>();
        readonly Motor motor;
        double diaH = double.NaN, diaL = double.NaN;
        bool enAsia, enLon, enPm;
        double aH = double.NaN, aL = double.NaN, loH = double.NaN, loL = double.NaN, pmH = double.NaN, pmL = double.NaN;

        public Liquidez(Motor m) { motor = m; }

        void Agregar(string tipo, double p, int lado, bool htf, DateTime t)
        {
            if (double.IsNaN(p)) return;
            Niveles.Add(new Nivel { Id = motor.NuevoId(), Tipo = tipo, P = p, Lado = lado, Htf = htf, T = t });
            if (Niveles.Count > 80) Niveles.RemoveAt(0);
        }

        public void NuevoDia(DateTime t)
        {
            foreach (Nivel L in Niveles)
            {
                if (L.Oculto) continue;
                bool esPd = L.Tipo == "PDH" || L.Tipo == "PDL";
                bool viejo = !L.Tomado && (t - L.T).TotalDays > 2;
                if (esPd || viejo) { L.Oculto = true; L.Tomado = true; }
            }
            Agregar("PDH", diaH, 1, true, t);
            Agregar("PDL", diaL, -1, true, t);
            diaH = diaL = double.NaN;
        }

        static double Max(double a, double b) { return double.IsNaN(a) ? b : Math.Max(a, b); }
        static double Min(double a, double b) { return double.IsNaN(a) ? b : Math.Min(a, b); }

        // rango del día y de las sesiones; al cerrar una sesión agrega su máximo y su mínimo
        public void Sesiones(int m, double h, double l, DateTime t)
        {
            ConfigMotor c = motor.Cfg;
            diaH = Max(diaH, h); diaL = Min(diaL, l);
            bool a = Utiles.EnSesion(m, c.AsiaIni, c.AsiaFin);
            if (a) { aH = Max(aH, h); aL = Min(aL, l); }
            if (!a && enAsia) { Agregar("ASIA_H", aH, 1, true, t); Agregar("ASIA_L", aL, -1, true, t); aH = aL = double.NaN; }
            enAsia = a;
            bool lo = Utiles.EnSesion(m, c.LonIni, c.LonFin);
            if (lo) { loH = Max(loH, h); loL = Min(loL, l); }
            if (!lo && enLon) { Agregar("LON_H", loH, 1, true, t); Agregar("LON_L", loL, -1, true, t); loH = loL = double.NaN; }
            enLon = lo;
            bool pm = Utiles.EnSesion(m, c.PmIni, c.PmFin);
            if (pm) { pmH = Max(pmH, h); pmL = Min(pmL, l); }
            if (!pm && enPm) { Agregar("PM_H", pmH, 1, true, t); Agregar("PM_L", pmL, -1, true, t); pmH = pmL = double.NaN; }
            enPm = pm;
        }

        // un nivel se toma cuando el precio lo pasa
        public void Barrer(double h, double l)
        {
            foreach (Nivel L in Niveles)
                if (!L.Tomado && (L.Lado == 1 ? h > L.P : l < L.P)) L.Tomado = true;
        }

        // Swing relevante: si está a menos de la tolerancia de otro se vuelve EQH/EQL; si está a menos de
        // SepAtr × ATR, se queda el más extremo.
        public void AgregarSwing(double p, int lado, bool htf, DateTime t, double atr, string tipoBase)
        {
            ConfigMotor c = motor.Cfg;
            foreach (Nivel L in Niveles)
            {
                if (L.Tomado || L.Lado != lado || !(L.Tipo.StartsWith("SW") || L.Tipo.StartsWith("EQ"))) continue;
                double d = Math.Abs(L.P - p);
                if (d <= c.EqTolPuntos)
                {
                    L.Tipo = lado == 1 ? "EQH" : "EQL";
                    L.P = lado == 1 ? Math.Max(L.P, p) : Math.Min(L.P, p);
                    L.Htf = true;
                    return;
                }
                if (!double.IsNaN(atr) && d < c.SepAtr * atr)
                {
                    if (lado == 1 ? p > L.P : p < L.P) { L.P = p; L.T = t; }
                    return;
                }
            }
            Agregar(tipoBase, p, lado, htf, t);
        }
    }

    public class Motor
    {
        public readonly ConfigMotor Cfg;
        public readonly MotorTF[] Ltf = new MotorTF[6];      // índices 1..5
        public readonly Agregador A15 = new Agregador(15, 0), A60 = new Agregador(60, 0), A240 = new Agregador(240, 18 * 60);
        public readonly List<FvgHtf> Htf15 = new List<FvgHtf>(), Htf60 = new List<FvgHtf>(), Htf240 = new List<FvgHtf>();
        public readonly Liquidez Liq;
        public DateTime DayStart = DateTime.MinValue;
        public DateTime UltimoT = DateTime.MinValue;          // apertura del último minuto procesado
        public readonly List<Fvg> NuevosIfvg = new List<Fvg>();
        public readonly List<CisdEvento> NuevosCisd = new List<CisdEvento>();
        DateTime diaId = DateTime.MinValue;
        int idSeq;

        public Motor(ConfigMotor cfg)
        {
            Cfg = cfg;
            for (int n = 1; n <= 5; n++) Ltf[n] = new MotorTF(this, n);
            Liq = new Liquidez(this);
        }

        public int NuevoId() { return ++idSeq; }

        public void DescartarEventos() { NuevosIfvg.Clear(); NuevosCisd.Clear(); }

        // Una vela de 1 minuto cerrada: t = apertura y tc = cierre, en hora de Nueva York.
        public void ProcesarMinuto(DateTime t, DateTime tc, double o, double h, double l, double c)
        {
            // el día de trading empieza a las 18:00 NY: fecha del instante t + 6 h
            DateTime t6 = t.AddHours(6);
            if (t6.Date != diaId)
            {
                bool primero = diaId == DateTime.MinValue;
                diaId = t6.Date;
                DayStart = t6.Date.AddHours(-6);
                if (!primero) Liq.NuevoDia(t);
            }
            int minuto = t.Hour * 60 + t.Minute;
            Liq.Sesiones(minuto, h, l, t);
            for (int n = 1; n <= 5; n++)
                foreach (Vela v in Ltf[n].Agg.Alimentar(o, h, l, c, t, tc)) Ltf[n].Procesar(v);
            foreach (Vela v in A15.Alimentar(o, h, l, c, t, tc)) { ProcesarHtf(A15, Htf15, "15m", v); Pivote15(); }
            foreach (Vela v in A60.Alimentar(o, h, l, c, t, tc)) ProcesarHtf(A60, Htf60, "1h", v);
            foreach (Vela v in A240.Alimentar(o, h, l, c, t, tc)) ProcesarHtf(A240, Htf240, "4h", v);
            Liq.Barrer(h, l);
            UltimoT = t;
        }

        // FVG de fondo: vivo hasta que una vela de su TF cierra a través
        void ProcesarHtf(Agregador a, List<FvgHtf> lista, string tf, Vela c3)
        {
            foreach (FvgHtf f in lista)
                if (f.Vivo && ((f.Dir == 1 && c3.C < f.Bot) || (f.Dir == -1 && c3.C > f.Top))) f.Vivo = false;
            int n = a.Velas.Count;
            if (n < 3) return;
            Vela c1 = a.Velas[n - 3];
            int dir = c1.H < c3.L ? 1 : c1.L > c3.H ? -1 : 0;
            if (dir == 0) return;
            lista.Add(new FvgHtf { Id = NuevoId(), Tf = tf, Dir = dir, Top = dir == 1 ? c3.L : c1.L, Bot = dir == 1 ? c1.H : c3.H, TBirth = c1.T });
            if (lista.Count > 20) lista.RemoveAt(0);
        }

        // swings de 15m: pivotes con 3 velas a cada lado
        void Pivote15()
        {
            List<Vela> v = A15.Velas;
            int mid = v.Count - 1 - 3;
            if (mid < 3) return;
            Vela m = v[mid];
            if (Utiles.EsPivote(v, mid, 3, 1)) Liq.AgregarSwing(m.H, 1, true, m.T, A15.Atr, "SW15_H");
            if (Utiles.EsPivote(v, mid, 3, -1)) Liq.AgregarSwing(m.L, -1, true, m.T, A15.Atr, "SW15_L");
        }

        // ── selección para dibujar ────────────────────────────────────────────

        // IFVG confirmados de hoy: vivos, y muertos si se pide; con ocultarMin > 0, solo los invertidos hace menos de N minutos
        public List<Fvg> IfvgCandidatos(DateTime ahora, bool[] tfOn, bool muertos, int ocultarMin)
        {
            var r = new List<Fvg>();
            for (int n = 1; n <= 5; n++)
            {
                if (!tfOn[n]) continue;
                foreach (Fvg f in Ltf[n].Fvgs)
                {
                    if (f.Estado != 1 && f.Estado != 2) continue;
                    bool vigente = ocultarMin == 0 || (ahora - f.TcInv).TotalMinutes <= ocultarMin;
                    if (f.TInv >= DayStart && vigente && (f.Estado == 1 || muertos)) r.Add(f);
                }
            }
            return r;
        }

        // si un IFVG de TF mayor, en la misma dirección, solapa el precio dentro de la ventana, se ve el mayor
        public static bool TapadoIfvg(Fvg f, DateTime tRefF, List<Fvg> otros, Func<Fvg, DateTime> tRef, int dedupMin)
        {
            foreach (Fvg g in otros)
                if (g.Tf > f.Tf && g.Dir == f.Dir && g.Top >= f.Bot && g.Bot <= f.Top && Math.Abs((tRef(g) - tRefF).TotalMinutes) <= dedupMin)
                    return true;
            return false;
        }

        public List<CisdEvento> CisdCandidatos(bool[] tfOn)
        {
            var r = new List<CisdEvento>();
            for (int n = 1; n <= 5; n++)
            {
                if (!tfOn[n]) continue;
                if (Ltf[n].UltAlc != null && Ltf[n].UltAlc.T >= DayStart) r.Add(Ltf[n].UltAlc);
                if (Ltf[n].UltBaj != null && Ltf[n].UltBaj.T >= DayStart) r.Add(Ltf[n].UltBaj);
            }
            return r;
        }

        public static bool TapadoCisd(CisdEvento e, List<CisdEvento> otros, int dedupMin, double dedupPts)
        {
            foreach (CisdEvento g in otros)
                if (g.Tf > e.Tf && g.Dir == e.Dir && Math.Abs(g.Lvl - e.Lvl) <= dedupPts && Math.Abs((g.T - e.T).TotalMinutes) <= dedupMin)
                    return true;
            return false;
        }

        // cierre de la vela abierta de N minutos, a partir de la apertura del minuto en curso
        public static DateTime FinVelaAbierta(DateTime tUlt, int n)
        {
            int m = tUlt.Hour * 60 + tUlt.Minute;
            return tUlt.AddMinutes(n - m % n);
        }

        // Vista previa: lo que confirmaría la vela abierta de cada TF si cerrara al precio actual.
        // tUlt = apertura del minuto en curso. Misma deduplicación que los confirmados.
        public void Previa(double precio, DateTime tUlt, bool[] tfOn, int dedupMin, double dedupPts, List<Fvg> confirmados, List<CisdEvento> cisdConf,
                           List<Fvg> outIfvg, List<CisdEvento> outCisd)
        {
            outIfvg.Clear(); outCisd.Clear();
            var todosI = new List<Fvg>();
            var todosC = new List<CisdEvento>();
            for (int n = 1; n <= 5; n++)
            {
                if (!tfOn[n]) continue;
                MotorTF s = Ltf[n];
                DateTime tFin = FinVelaAbierta(tUlt, n);
                foreach (Fvg f in s.Fvgs)
                {
                    if (f.Estado != 0) continue;
                    bool dentro = Cfg.LimiteEnMinutos
                        ? (Cfg.MaxMinutosInv == 0 || (tFin - f.TcBirth).TotalMinutes <= Cfg.MaxMinutosInv)
                        : (Cfg.MaxVelasInv == 0 || s.Agg.SeqN + 1 - f.SeqBirth <= Cfg.MaxVelasInv);
                    bool inv = f.Dir == 1 ? precio < f.Bot : precio > f.Top;
                    if (dentro && inv) todosI.Add(f);
                }
                if (s.CBear.Armado && precio < s.CBear.Lvl) todosC.Add(new CisdEvento { T = tFin, Dir = -1, Tf = n, Lvl = s.CBear.Lvl, TStart = s.CBear.TStart });
                if (s.CBull.Armado && precio > s.CBull.Lvl) todosC.Add(new CisdEvento { T = tFin, Dir = 1, Tf = n, Lvl = s.CBull.Lvl, TStart = s.CBull.TStart });
            }
            foreach (Fvg f in todosI)
            {
                DateTime tf = FinVelaAbierta(tUlt, f.Tf);
                bool tapado = TapadoIfvg(f, tf, confirmados, g => g.TInv, dedupMin) || TapadoIfvg(f, tf, todosI, g => FinVelaAbierta(tUlt, g.Tf), dedupMin);
                if (!tapado) outIfvg.Add(f);
            }
            foreach (CisdEvento e in todosC)
                if (!TapadoCisd(e, cisdConf, dedupMin, dedupPts) && !TapadoCisd(e, todosC, dedupMin, dedupPts)) outCisd.Add(e);
        }

        // FVG de fondo a dibujar: los que contienen el precio y los N más cercanos por arriba y por abajo. Con opuestos,
        // además el bajista más cercano por arriba y el alcista más cercano por abajo: si no, en una caída los alcistas que
        // el precio acaba de cruzar (siguen vivos hasta que cierre una vela de su TF) ocupan los N de arriba y tapan a los bajistas.
        public List<FvgHtf> HtfSeleccion(double precio, bool on15, bool on60, bool on240, int nCercanos, bool opuestos)
        {
            var vivos = new List<FvgHtf>();
            if (on15) foreach (FvgHtf f in Htf15) if (f.Vivo) vivos.Add(f);
            if (on60) foreach (FvgHtf f in Htf60) if (f.Vivo) vivos.Add(f);
            if (on240) foreach (FvgHtf f in Htf240) if (f.Vivo) vivos.Add(f);
            var sel = new List<FvgHtf>();
            foreach (FvgHtf f in vivos) if (f.Bot <= precio && f.Top >= precio) sel.Add(f);
            for (int k = 0; k < nCercanos; k++)
            {
                FvgHtf arriba = null, abajo = null;
                foreach (FvgHtf f in vivos)
                {
                    if (sel.Contains(f)) continue;
                    if (f.Bot > precio && (arriba == null || f.Bot < arriba.Bot)) arriba = f;
                    if (f.Top < precio && (abajo == null || f.Top > abajo.Top)) abajo = f;
                }
                if (arriba != null) sel.Add(arriba);
                if (abajo != null) sel.Add(abajo);
            }
            if (opuestos)
            {
                FvgHtf baj = null, alc = null;
                foreach (FvgHtf f in vivos)
                {
                    if (f.Dir == -1 && f.Bot > precio && (baj == null || f.Bot < baj.Bot)) baj = f;
                    if (f.Dir == 1 && f.Top < precio && (alc == null || f.Top > alc.Top)) alc = f;
                }
                if (baj != null && !sel.Contains(baj)) sel.Add(baj);
                if (alc != null && !sel.Contains(alc)) sel.Add(alc);
            }
            return sel;
        }

        // Número de cada FVG dentro de su TF, de arriba hacia abajo (15m #1 es el de 15m más alto de los dibujados),
        // para rotular su techo y su piso con el mismo nombre.
        public static Dictionary<FvgHtf, int> NumerarHtf(List<FvgHtf> sel)
        {
            var num = new Dictionary<FvgHtf, int>();
            foreach (IGrouping<string, FvgHtf> g in sel.GroupBy(f => f.Tf))
            {
                int k = 0;
                foreach (FvgHtf f in g.OrderByDescending(f => f.Top).ThenByDescending(f => f.Bot)) num[f] = ++k;
            }
            return num;
        }

        // "15m 3▲ 1▼ · 1h 1▲ 0▼ · 4h 0▲ 0▼": FVG vivos de cada TF encendida, alcistas y bajistas
        public string HtfConteo(bool on15, bool on60, bool on240)
        {
            var partes = new List<string>();
            if (on15) partes.Add(Conteo("15m", Htf15));
            if (on60) partes.Add(Conteo("1h", Htf60));
            if (on240) partes.Add(Conteo("4h", Htf240));
            return string.Join(" · ", partes);
        }

        static string Conteo(string tf, List<FvgHtf> lista)
        {
            int alc = 0, baj = 0;
            foreach (FvgHtf f in lista)
                if (f.Vivo) { if (f.Dir == 1) alc++; else baj++; }
            return tf + " " + alc + "▲ " + baj + "▼";
        }
    }

    // ── GEX ─────────────────────────────────────────────────────────────────

    public class GexOpcion { public double Strike, TAnios, Oi; public bool EsCall; public double Iv = double.NaN, Gamma = double.NaN; }

    public class GexResultado
    {
        public double Spot, Total, CallWall, PutWall, Flip = double.NaN;
        public List<double> Extra = new List<double>();
        public int NOpciones;
        public DateTime Calculado;
    }

    // Niveles GEX con la cadena de opciones de CBOE (cdn.cboe.com/api/global/delayed_quotes/options/QQQ.json).
    // Misma lógica que gex/gex_nasdaq.py: GEX = gamma × OI × 100 × spot² × 1 %, calls +, puts −.
    public static class GexCalc
    {
        static readonly Regex Occ = new Regex(@"^([A-Z_]+?)(\d{6})([CP])(\d{8})$");
        static readonly Regex Objeto = new Regex(@"\{[^{}]*\}");
        const string Num = @"(-?\d+(?:\.\d+)?(?:[eE][-+]?\d+)?)";

        static double Campo(string obj, params string[] nombres)
        {
            foreach (string n in nombres)
            {
                Match m = Regex.Match(obj, "\"" + n + "\"\\s*:\\s*\"?" + Num);
                if (m.Success) return double.Parse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture);
            }
            return double.NaN;
        }

        static string Texto(string obj, params string[] nombres)
        {
            foreach (string n in nombres)
            {
                Match m = Regex.Match(obj, "\"" + n + "\"\\s*:\\s*\"([^\"]*)\"");
                if (m.Success && m.Groups[1].Value != "") return m.Groups[1].Value;
            }
            return null;
        }

        public static double GammaBs(double s, double k, double t, double iv, double r, double q)
        {
            if (s <= 0 || k <= 0 || t <= 0 || iv <= 0) return 0;
            double raiz = iv * Math.Sqrt(t);
            double d1 = (Math.Log(s / k) + (r - q + 0.5 * iv * iv) * t) / raiz;
            return Math.Exp(-q * t) * Math.Exp(-0.5 * d1 * d1) / (Math.Sqrt(2 * Math.PI) * s * raiz);
        }

        // Lee el JSON: precio del subyacente y opciones vigentes con open interest.
        // Los vencimientos son a las 16:00 de Nueva York; el plazo se mide en UTC para respetar el cambio de horario.
        public static double Leer(string json, DateTime ahoraUtc, TimeZoneInfo tzNy, double maxDias, List<GexOpcion> salida)
        {
            salida.Clear();
            double spot = double.NaN;
            foreach (string k in new[] { "current_price", "close", "last", "price", "prev_day_close" })
            {
                Match m = Regex.Match(json, "\"" + k + "\"\\s*:\\s*\"?" + Num);
                if (m.Success) { spot = double.Parse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture); if (spot > 0) break; }
            }
            if (!(spot > 0)) throw new FormatException("no encontré el precio del subyacente en el JSON (current_price / close / last)");
            foreach (Match mo in Objeto.Matches(json))
            {
                string obj = mo.Value;
                string sym = Texto(obj, "option", "symbol", "contract");
                if (sym == null) continue;
                Match occ = Occ.Match(sym);
                if (!occ.Success) continue;
                string fecha = occ.Groups[2].Value;
                var venc = new DateTime(2000 + int.Parse(fecha.Substring(0, 2)), int.Parse(fecha.Substring(2, 2)), int.Parse(fecha.Substring(4, 2)), 16, 0, 0);
                double t = (TimeZoneInfo.ConvertTimeToUtc(venc, tzNy) - ahoraUtc).TotalSeconds / (365.0 * 86400);
                if (t <= 0 || t * 365 > maxDias) continue;
                double oi = Campo(obj, "open_interest", "openInterest", "oi");
                if (double.IsNaN(oi) || oi <= 0) continue;
                double iv = Campo(obj, "iv", "implied_volatility", "impliedVolatility");
                if (!double.IsNaN(iv) && iv > 3) iv /= 100.0;             // algunos feeds la dan en porcentaje
                salida.Add(new GexOpcion
                {
                    Strike = int.Parse(occ.Groups[4].Value) / 1000.0,
                    EsCall = occ.Groups[3].Value == "C",
                    TAnios = t, Oi = oi, Iv = iv, Gamma = Campo(obj, "gamma")
                });
            }
            if (salida.Count == 0) throw new FormatException("no hay opciones vigentes con open interest en el rango de vencimientos pedido");
            return spot;
        }

        static double GexOp(GexOpcion op, double s, double r, double q, bool usarGammaFeed)
        {
            double g;
            if (usarGammaFeed && !double.IsNaN(op.Gamma)) g = op.Gamma;
            else if (!double.IsNaN(op.Iv) && op.Iv > 0) g = GammaBs(s, op.Strike, op.TAnios, op.Iv, r, q);
            else return 0;
            return (op.EsCall ? 1 : -1) * g * op.Oi * 100 * s * s * 0.01;
        }

        public static GexResultado Calcular(double spot, List<GexOpcion> ops, double r, double q, int nExtra)
        {
            var calls = new SortedDictionary<double, double>();
            var puts = new SortedDictionary<double, double>();
            foreach (GexOpcion op in ops)
            {
                double v = GexOp(op, spot, r, q, true);
                SortedDictionary<double, double> d = op.EsCall ? calls : puts;
                double acc; d.TryGetValue(op.Strike, out acc); d[op.Strike] = acc + v;
                if (!calls.ContainsKey(op.Strike)) calls[op.Strike] = 0;
                if (!puts.ContainsKey(op.Strike)) puts[op.Strike] = 0;
            }
            var res = new GexResultado { Spot = spot, NOpciones = ops.Count };
            double maxC = double.NegativeInfinity, minP = double.PositiveInfinity;
            foreach (KeyValuePair<double, double> kv in calls)
            {
                if (kv.Value > maxC) { maxC = kv.Value; res.CallWall = kv.Key; }
                double p = puts[kv.Key];
                if (p < minP) { minP = p; res.PutWall = kv.Key; }
                res.Total += kv.Value + p;
            }
            // perfil: GEX total a precios hipotéticos (±12 % en pasos de 0,25 %) con gamma de Black-Scholes
            double sAnt = 0, gAnt = 0;
            for (int i = -48; i <= 48; i++)
            {
                double s = spot * (1 + i * 0.0025), gt = 0;
                foreach (GexOpcion op in ops) gt += GexOp(op, s, r, q, false);
                if (i > -48 && (gAnt == 0 || (gAnt < 0) != (gt < 0)))
                {
                    double cruce = gAnt == 0 ? sAnt : sAnt + (s - sAnt) * (-gAnt) / (gt - gAnt);
                    if (double.IsNaN(res.Flip) || Math.Abs(cruce - spot) < Math.Abs(res.Flip - spot)) res.Flip = cruce;
                }
                sAnt = s; gAnt = gt;
            }
            // strikes extra: los de más GEX neto, si pesan al menos 10 % del mayor
            var netos = new List<KeyValuePair<double, double>>();
            double tope = 0;
            foreach (KeyValuePair<double, double> kv in calls)
            {
                double neto = Math.Abs(kv.Value + puts[kv.Key]);
                netos.Add(new KeyValuePair<double, double>(kv.Key, neto));
                tope = Math.Max(tope, neto);
            }
            foreach (KeyValuePair<double, double> kv in netos.OrderByDescending(x => x.Value))
            {
                if (res.Extra.Count >= nExtra) break;
                if (kv.Key == res.CallWall || kv.Key == res.PutWall || kv.Value < 0.1 * tope) continue;
                res.Extra.Add(kv.Key);
            }
            res.Extra.Sort();
            return res;
        }
    }
}
// ===== NUCLEO FIN =====

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private PrimerIFVG[] cachePrimerIFVG;
		public PrimerIFVG PrimerIFVG()
		{
			return PrimerIFVG(Input);
		}

		public PrimerIFVG PrimerIFVG(ISeries<double> input)
		{
			if (cachePrimerIFVG != null)
				for (int idx = 0; idx < cachePrimerIFVG.Length; idx++)
					if (cachePrimerIFVG[idx] != null &&  cachePrimerIFVG[idx].EqualsInput(input))
						return cachePrimerIFVG[idx];
			return CacheIndicator<PrimerIFVG>(new PrimerIFVG(), input, ref cachePrimerIFVG);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.PrimerIFVG PrimerIFVG()
		{
			return indicator.PrimerIFVG(Input);
		}

		public Indicators.PrimerIFVG PrimerIFVG(ISeries<double> input )
		{
			return indicator.PrimerIFVG(input);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.PrimerIFVG PrimerIFVG()
		{
			return indicator.PrimerIFVG(Input);
		}

		public Indicators.PrimerIFVG PrimerIFVG(ISeries<double> input )
		{
			return indicator.PrimerIFVG(input);
		}
	}
}

#endregion
