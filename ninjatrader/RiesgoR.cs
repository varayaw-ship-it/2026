// ══════════════════════════════════════════════════════════════════════════════
//  RIESGO R1–R5 · herramienta de dibujo para NinjaTrader 8 · v0.2 (2026-10-28)
//  Como la herramienta Risk Reward de NinjaTrader, pero con los objetivos R1 a R5,
//  zonas con degradado y textos mínimos escritos dentro de la caja: cuánto se
//  pierde en el stop y cuánto se gana en cada R. Si hay una posición abierta en
//  el instrumento, usa sus contratos; si no, los de los ajustes.
//
//  USO
//  · En el gráfico: menú de herramientas de dibujo (el lápiz) → "Riesgo R1–R5".
//  · Primer clic: la entrada. Segundo clic: el stop. Si el stop queda debajo de
//    la entrada es un largo; si queda arriba, un corto.
//  · Después se pueden arrastrar las dos anclas (entrada y stop); arrastrando la
//    caja se mueve todo. Doble clic sobre la caja abre sus propiedades.
//
//  INSTALAR
//  · NinjaScript Editor → clic derecho en DrawingTools → New Drawing Tool, con el
//    nombre RiesgoR → Generate. Reemplazar todo el contenido por este archivo y F5.
// ══════════════════════════════════════════════════════════════════════════════

#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
#endregion

namespace NinjaTrader.NinjaScript.DrawingTools
{
    public class RiesgoR : DrawingTool
    {
        const int Sensibilidad = 15;                // píxeles para tomar un ancla con el mouse
        const float AnchoMinimo = 60;               // ancho mínimo de la caja en píxeles
        ChartAnchor editando;

        class Geometria
        {
            public int Dir;                         // 1 largo, -1 corto
            public double Entrada, Stop, Riesgo;
            public double[] Niveles;                // Niveles[k] = precio de Rk (k = 1..N)
            public float Izq, Der, Arriba, Abajo, YEntrada, YStop;
            public float[] YNiveles;
        }

        [Display(Order = 1)]
        public ChartAnchor EntradaAnchor { get; set; }

        [Display(Order = 2)]
        public ChartAnchor StopAnchor { get; set; }

        public override IEnumerable<ChartAnchor> Anchors { get { return new[] { EntradaAnchor, StopAnchor }; } }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "Riesgo R1–R5";
                DrawingState = DrawingState.Building;
                EntradaAnchor = new ChartAnchor { IsBrowsable = true, IsEditing = true, DrawingTool = this, DisplayName = "Entrada" };
                StopAnchor = new ChartAnchor { IsBrowsable = true, IsEditing = true, DrawingTool = this, DisplayName = "Stop" };

                NivelesR = 5;
                Contratos = 1;
                UsarPosicion = true;
                MostrarPrecio = false;
                MostrarPuntos = false;
                MostrarDinero = true;
                OpacidadZona = 18;
                Fuente = new SimpleFont("Segoe UI", 11);
                ColorObjetivo = Congelar(Color.FromRgb(0x2E, 0x7D, 0x32));
                ColorStop = Congelar(Color.FromRgb(0xD3, 0x2F, 0x2F));
                ColorEntrada = Congelar(Color.FromRgb(0x54, 0x6E, 0x7A));
            }
            else if (State == State.Terminated)
                Dispose();
        }

        static SolidColorBrush Congelar(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

        // ── precios ─────────────────────────────────────────────────────────

        double Redondear(double precio)
        {
            if (AttachedTo == null || AttachedTo.Instrument == null) return precio;
            return AttachedTo.Instrument.MasterInstrument.RoundToTickSize(precio);
        }

        double ValorPunto { get { return AttachedTo != null && AttachedTo.Instrument != null ? AttachedTo.Instrument.MasterInstrument.PointValue : 0; } }

        string Simbolo { get { return AttachedTo != null && AttachedTo.Instrument != null ? AttachedTo.Instrument.MasterInstrument.Name : ""; } }

        // precio con los decimales del tick (0,25 → 2 decimales), sin separador de miles, como el eje del gráfico
        string FormatoPrecio(double precio)
        {
            int dec = 2;
            if (AttachedTo != null && AttachedTo.Instrument != null)
            {
                string t = AttachedTo.Instrument.MasterInstrument.TickSize.ToString("0.##########", CultureInfo.InvariantCulture);
                dec = t.Contains(".") ? t.Length - t.IndexOf('.') - 1 : 0;
            }
            return precio.ToString("F" + dec, CultureInfo.CurrentCulture);
        }

        static string Puntos(double d) { return d.ToString("0.##", CultureInfo.CurrentCulture) + " pts"; }

        static string Dinero(double v) { return v.ToString("N2", CultureInfo.CurrentCulture) + " $"; }

        // Contratos de la posición abierta en el instrumento del gráfico, en cualquier cuenta conectada
        // (si hay varias con posición, la mayor). 0 si no hay posición.
        int ContratosEnPosicion()
        {
            if (AttachedTo == null || AttachedTo.Instrument == null) return 0;
            string nombre = AttachedTo.Instrument.FullName;
            int contratos = 0;
            try
            {
                lock (Account.All)
                    foreach (Account cuenta in Account.All)
                        foreach (Position pos in cuenta.Positions)
                            if (pos.MarketPosition != MarketPosition.Flat && pos.Instrument != null && pos.Instrument.FullName == nombre)
                                contratos = Math.Max(contratos, pos.Quantity);
            }
            catch (InvalidOperationException) { }       // la lista cambió mientras se leía: se usa lo que se alcanzó a leer
            return contratos;
        }

        // "R3 · +75,00 $" (el precio y los puntos se pueden agregar en los ajustes)
        string TextoNivel(string nombre, double precio, double distancia, string signo, int contratos)
        {
            var partes = new List<string> { nombre };
            if (MostrarPrecio) partes.Add(FormatoPrecio(precio));
            if (MostrarPuntos) partes.Add(signo + Puntos(distancia));
            if (MostrarDinero && ValorPunto > 0) partes.Add(signo + Dinero(distancia * ValorPunto * contratos));
            return string.Join(" · ", partes);
        }

        // ── geometría ───────────────────────────────────────────────────────

        Geometria Calcular(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale)
        {
            if (EntradaAnchor == null || StopAnchor == null || EntradaAnchor.IsEditing) return null;
            var g = new Geometria();
            g.Entrada = Redondear(EntradaAnchor.Price);
            g.Stop = Redondear(StopAnchor.Price);
            g.Dir = g.Stop <= g.Entrada ? 1 : -1;
            g.Riesgo = Math.Abs(g.Entrada - g.Stop);
            int n = Math.Max(1, Math.Min(5, NivelesR));
            g.Niveles = new double[n + 1];
            g.YNiveles = new float[n + 1];
            g.Niveles[0] = g.Entrada;
            for (int k = 1; k <= n; k++) g.Niveles[k] = g.Entrada + g.Dir * k * g.Riesgo;

            Point pE = EntradaAnchor.GetPoint(chartControl, chartPanel, chartScale);
            Point pS = StopAnchor.GetPoint(chartControl, chartPanel, chartScale);
            g.Izq = (float)Math.Min(pE.X, pS.X);
            g.Der = (float)Math.Max(pE.X, pS.X);
            if (g.Der - g.Izq < AnchoMinimo) g.Der = g.Izq + AnchoMinimo;
            g.YEntrada = chartScale.GetYByValue(g.Entrada);
            g.YStop = chartScale.GetYByValue(g.Stop);
            for (int k = 0; k <= n; k++) g.YNiveles[k] = chartScale.GetYByValue(g.Niveles[k]);
            g.Arriba = Math.Min(g.YStop, Math.Min(g.YEntrada, g.YNiveles[n]));
            g.Abajo = Math.Max(g.YStop, Math.Max(g.YEntrada, g.YNiveles[n]));
            return g;
        }

        static bool Dentro(Geometria g, Point p) { return g != null && p.X >= g.Izq && p.X <= g.Der && p.Y >= g.Arriba && p.Y <= g.Abajo; }

        // ── mouse ───────────────────────────────────────────────────────────

        public override void OnMouseDown(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale, ChartAnchor dataPoint)
        {
            switch (DrawingState)
            {
                case DrawingState.Building:
                    if (EntradaAnchor.IsEditing)
                    {
                        dataPoint.CopyDataValues(EntradaAnchor);
                        dataPoint.CopyDataValues(StopAnchor);
                        EntradaAnchor.Price = Redondear(EntradaAnchor.Price);
                        StopAnchor.Price = EntradaAnchor.Price;
                        EntradaAnchor.IsEditing = false;
                    }
                    else if (StopAnchor.IsEditing)
                    {
                        dataPoint.CopyDataValues(StopAnchor);
                        StopAnchor.Price = Redondear(StopAnchor.Price);
                        StopAnchor.IsEditing = false;
                    }
                    if (!EntradaAnchor.IsEditing && !StopAnchor.IsEditing)
                    {
                        DrawingState = DrawingState.Normal;
                        IsSelected = false;
                    }
                    break;
                case DrawingState.Normal:
                    Point punto = dataPoint.GetPoint(chartControl, chartPanel, chartScale);
                    editando = GetClosestAnchor(chartControl, chartPanel, chartScale, Sensibilidad, punto);
                    if (editando != null)
                    {
                        editando.IsEditing = true;
                        DrawingState = DrawingState.Editing;
                    }
                    else if (GetCursor(chartControl, chartPanel, chartScale, punto) != null)
                        DrawingState = DrawingState.Moving;
                    else
                        IsSelected = false;
                    break;
            }
        }

        public override void OnMouseMove(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale, ChartAnchor dataPoint)
        {
            if (IsLocked && DrawingState != DrawingState.Building) return;
            if (DrawingState == DrawingState.Building)
            {
                if (!EntradaAnchor.IsEditing && StopAnchor.IsEditing)
                {
                    dataPoint.CopyDataValues(StopAnchor);
                    StopAnchor.Price = Redondear(StopAnchor.Price);
                }
            }
            else if (DrawingState == DrawingState.Editing && editando != null)
            {
                dataPoint.CopyDataValues(editando);
                editando.Price = Redondear(editando.Price);
            }
            else if (DrawingState == DrawingState.Moving)
                foreach (ChartAnchor ancla in Anchors)
                    ancla.MoveAnchor(InitialMouseDownAnchor, dataPoint, chartControl, chartPanel, chartScale, this);
        }

        public override void OnMouseUp(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale, ChartAnchor dataPoint)
        {
            if (DrawingState == DrawingState.Moving || DrawingState == DrawingState.Editing)
            {
                DrawingState = DrawingState.Normal;
                foreach (ChartAnchor ancla in Anchors) ancla.Price = Redondear(ancla.Price);
            }
            if (editando != null) editando.IsEditing = false;
            editando = null;
        }

        public override Cursor GetCursor(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale, Point point)
        {
            switch (DrawingState)
            {
                case DrawingState.Building: return Cursors.Pen;
                case DrawingState.Moving: return IsLocked ? Cursors.No : Cursors.SizeAll;
                case DrawingState.Editing: return IsLocked ? Cursors.No : Cursors.SizeNS;
                default:
                    if (GetClosestAnchor(chartControl, chartPanel, chartScale, Sensibilidad, point) != null)
                        return IsLocked ? Cursors.Arrow : Cursors.SizeNS;
                    return Dentro(Calcular(chartControl, chartPanel, chartScale), point) ? (IsLocked ? Cursors.Arrow : Cursors.SizeAll) : null;
            }
        }

        public override Point[] GetSelectionPoints(ChartControl chartControl, ChartScale chartScale)
        {
            ChartPanel chartPanel = chartControl.ChartPanels[chartScale.PanelIndex];
            return new[] { EntradaAnchor.GetPoint(chartControl, chartPanel, chartScale), StopAnchor.GetPoint(chartControl, chartPanel, chartScale) };
        }

        // la caja puede estar en el margen derecho (futuro): siempre se dibuja
        public override bool IsVisibleOnChart(ChartControl chartControl, ChartScale chartScale, DateTime firstTimeOnChart, DateTime lastTimeOnChart)
        {
            return true;
        }

        // ── dibujo ──────────────────────────────────────────────────────────

        public override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            ChartPanel chartPanel = chartControl.ChartPanels[chartScale.PanelIndex];
            Geometria g = Calcular(chartControl, chartPanel, chartScale);
            if (g == null) return;

            SharpDX.Direct2D1.AntialiasMode modoAnterior = RenderTarget.AntialiasMode;
            RenderTarget.AntialiasMode = SharpDX.Direct2D1.AntialiasMode.PerPrimitive;
            try
            {
                float ancho = g.Der - g.Izq;
                if (IsInHitTest)
                {
                    RenderTarget.FillRectangle(new SharpDX.RectangleF(g.Izq, g.Arriba, ancho, g.Abajo - g.Arriba), chartControl.SelectionBrush);
                    return;
                }
                int n = g.Niveles.Length - 1;
                float op = Math.Max(0, Math.Min(100, OpacidadZona)) / 100f;

                // zona de riesgo
                Rellenar(ColorStop, op * 0.8f, g.Izq, Math.Min(g.YEntrada, g.YStop), ancho, Math.Abs(g.YEntrada - g.YStop));
                // zonas de beneficio: más intensas a medida que sube el R
                for (int k = 1; k <= n; k++)
                {
                    float y1 = g.YNiveles[k - 1], y2 = g.YNiveles[k];
                    Rellenar(ColorObjetivo, op * (0.35f + 0.65f * k / n), g.Izq, Math.Min(y1, y2), ancho, Math.Abs(y2 - y1));
                }

                // líneas: stop y entrada gruesas, R1..R(n-1) finas, la última gruesa
                Linea(ColorStop, 1f, 2f, g.Izq, g.Der, g.YStop);
                for (int k = 1; k <= n; k++) Linea(ColorObjetivo, k == n ? 1f : 0.75f, k == n ? 2f : 1f, g.Izq, g.Der, g.YNiveles[k]);
                Linea(ColorEntrada, 1f, 2f, g.Izq, g.Der, g.YEntrada);

                DibujarTextos(g);
            }
            finally
            {
                RenderTarget.AntialiasMode = modoAnterior;
            }
        }

        void Rellenar(Brush color, float opacidad, float x, float y, float w, float h)
        {
            if (h <= 0) return;
            using (SharpDX.Direct2D1.Brush b = color.ToDxBrush(RenderTarget))
            {
                b.Opacity = opacidad;
                RenderTarget.FillRectangle(new SharpDX.RectangleF(x, y, w, h), b);
            }
        }

        void Linea(Brush color, float opacidad, float grosor, float x1, float x2, float y)
        {
            using (SharpDX.Direct2D1.Brush b = color.ToDxBrush(RenderTarget))
            {
                b.Opacity = opacidad;
                RenderTarget.DrawLine(new SharpDX.Vector2(x1, y), new SharpDX.Vector2(x2, y), b, grosor);
            }
        }

        // Textos dentro de la caja, pegados a su línea y del lado de la entrada: el stop y los R a la derecha, la entrada
        // a la izquierda. Si la caja es baja y no caben uno por R, se escriben cada tantos R (siempre el último).
        void DibujarTextos(Geometria g)
        {
            int n = g.Niveles.Length - 1;
            int contratos = Contratos;
            bool enPosicion = false;
            if (UsarPosicion)
            {
                int abiertos = ContratosEnPosicion();
                if (abiertos > 0) { contratos = abiertos; enPosicion = true; }
            }
            using (SharpDX.DirectWrite.TextFormat formato = Fuente.ToDirectWriteTextFormat())
            {
                float alto;
                using (var muestra = new SharpDX.DirectWrite.TextLayout(NinjaTrader.Core.Globals.DirectWriteFactory, "R5", formato, 200, 100))
                    alto = muestra.Metrics.Height;

                bool riesgoAbajo = g.YStop >= g.YEntrada;           // en pantalla, el stop está debajo de la entrada (largo)
                Rotular(formato, TextoNivel("Stop", g.Stop, g.Riesgo, "−", contratos), ColorStop, g, g.YStop, !riesgoAbajo, true, alto);
                string entrada = (g.Dir == 1 ? "Largo" : "Corto") + " · " + contratos + " " + Simbolo
                                 + (enPosicion ? (contratos == 1 ? " abierto" : " abiertos") : "")
                                 + (MostrarPrecio ? " · " + FormatoPrecio(g.Entrada) : "");
                Rotular(formato, entrada, ColorEntrada, g, g.YEntrada, riesgoAbajo, false, alto);

                float pxPorR = Math.Abs(g.YEntrada - g.YStop);
                int paso = pxPorR <= 0 ? n : Math.Max(1, (int)Math.Ceiling((alto + 2) / pxPorR));
                for (int k = 1; k <= n; k++)
                    if (k % paso == 0 || k == n)
                        Rotular(formato, TextoNivel("R" + k, g.Niveles[k], k * g.Riesgo, "+", contratos), ColorObjetivo, g, g.YNiveles[k], riesgoAbajo, true, alto);
            }
        }

        // escribe el texto junto a la línea y, debajo o encima de ella, a la derecha o a la izquierda de la caja
        void Rotular(SharpDX.DirectWrite.TextFormat formato, string texto, Brush color, Geometria g, float yLinea, bool debajo, bool aLaDerecha, float alto)
        {
            using (var layout = new SharpDX.DirectWrite.TextLayout(NinjaTrader.Core.Globals.DirectWriteFactory, texto, formato, 1000, 100))
            using (SharpDX.Direct2D1.Brush pincel = color.ToDxBrush(RenderTarget))
            {
                float x = aLaDerecha ? g.Der - layout.Metrics.Width - 6 : g.Izq + 6;
                float y = debajo ? yLinea + 2 : yLinea - alto - 2;
                RenderTarget.DrawTextLayout(new SharpDX.Vector2(x, y), layout, pincel);
            }
        }

        #region Propiedades

        [Range(1, 5)]
        [Display(Name = "Hasta qué R (1 a 5)", Order = 1, GroupName = "Riesgo R")]
        public int NivelesR { get; set; }

        [Display(Name = "Usar los contratos de la posición abierta", Order = 2, GroupName = "Riesgo R")]
        public bool UsarPosicion { get; set; }

        [Range(1, 1000)]
        [Display(Name = "Contratos (si no hay posición abierta)", Order = 3, GroupName = "Riesgo R")]
        public int Contratos { get; set; }

        [Display(Name = "Mostrar el precio", Order = 4, GroupName = "Riesgo R")]
        public bool MostrarPrecio { get; set; }

        [Display(Name = "Mostrar los puntos", Order = 5, GroupName = "Riesgo R")]
        public bool MostrarPuntos { get; set; }

        [Display(Name = "Mostrar el dinero", Order = 6, GroupName = "Riesgo R")]
        public bool MostrarDinero { get; set; }

        [Range(0, 100)]
        [Display(Name = "Opacidad de las zonas (%)", Order = 7, GroupName = "Riesgo R")]
        public int OpacidadZona { get; set; }

        [Display(Name = "Letra", Order = 8, GroupName = "Riesgo R")]
        public SimpleFont Fuente { get; set; }

        [XmlIgnore]
        [Display(Name = "Color de los objetivos", Order = 1, GroupName = "Colores")]
        public Brush ColorObjetivo { get; set; }
        [Browsable(false)]
        public string ColorObjetivoSerializable { get { return Serialize.BrushToString(ColorObjetivo); } set { ColorObjetivo = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [Display(Name = "Color del stop", Order = 2, GroupName = "Colores")]
        public Brush ColorStop { get; set; }
        [Browsable(false)]
        public string ColorStopSerializable { get { return Serialize.BrushToString(ColorStop); } set { ColorStop = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [Display(Name = "Color de la entrada", Order = 3, GroupName = "Colores")]
        public Brush ColorEntrada { get; set; }
        [Browsable(false)]
        public string ColorEntradaSerializable { get { return Serialize.BrushToString(ColorEntrada); } set { ColorEntrada = Serialize.StringToBrush(value); } }

        #endregion
    }
}
