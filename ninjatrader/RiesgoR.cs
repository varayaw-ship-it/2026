// ══════════════════════════════════════════════════════════════════════════════
//  RIESGO R1–R5 · herramienta de dibujo para NinjaTrader 8 · v0.1 (2026-10-28)
//  Como la herramienta Risk Reward de NinjaTrader, pero con los objetivos R1 a R5,
//  zonas con degradado y etiquetas con precio, puntos y dinero.
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

        class Pastilla { public float Y; public string Texto; public Brush Color; }

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
                MostrarPrecio = true;
                MostrarPuntos = true;
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

        // "R3 · 29725,25 · +37,5 pts · +75,00 $" (cada parte se puede apagar)
        string TextoNivel(string nombre, double precio, double distancia, string signo)
        {
            var partes = new List<string> { nombre };
            if (MostrarPrecio) partes.Add(FormatoPrecio(precio));
            if (MostrarPuntos) partes.Add(signo + Puntos(distancia));
            if (MostrarDinero && ValorPunto > 0) partes.Add(signo + Dinero(distancia * ValorPunto * Contratos));
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

                DibujarPastillas(chartPanel, g);
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

        // Una pastilla de color por línea, a la derecha de la caja (o adentro si no cabe). Si la caja es baja y las
        // pastillas no caben una por R, se muestran cada tantos R (siempre la última); si se pisan, se corren hacia abajo.
        void DibujarPastillas(ChartPanel chartPanel, Geometria g)
        {
            int n = g.Niveles.Length - 1;
            using (SharpDX.DirectWrite.TextFormat formato = Fuente.ToDirectWriteTextFormat())
            using (SharpDX.Direct2D1.Brush blanco = Brushes.White.ToDxBrush(RenderTarget))
            {
                float alto;
                using (var muestra = new SharpDX.DirectWrite.TextLayout(NinjaTrader.Core.Globals.DirectWriteFactory, "R5", formato, 200, 100))
                    alto = muestra.Metrics.Height + 4;

                var lista = new List<Pastilla>
                {
                    new Pastilla { Y = g.YStop, Texto = TextoNivel("Stop", g.Stop, g.Riesgo, "−"), Color = ColorStop },
                    new Pastilla
                    {
                        Y = g.YEntrada, Color = ColorEntrada,
                        Texto = (g.Dir == 1 ? "Largo" : "Corto") + (MostrarPrecio ? " · " + FormatoPrecio(g.Entrada) : "") + " · " + Contratos + " " + Simbolo
                    }
                };
                float pxPorR = Math.Abs(g.YEntrada - g.YStop);
                int paso = pxPorR <= 0 ? n : Math.Max(1, (int)Math.Ceiling((alto + 2) / pxPorR));
                for (int k = 1; k <= n; k++)
                    if (k % paso == 0 || k == n)
                        lista.Add(new Pastilla { Y = g.YNiveles[k], Texto = TextoNivel("R" + k, g.Niveles[k], k * g.Riesgo, "+"), Color = ColorObjetivo });

                float bordeDerecho = chartPanel.X + chartPanel.W;
                float ocupadoHasta = float.MinValue;
                foreach (Pastilla p in lista.OrderBy(x => x.Y))
                {
                    using (var layout = new SharpDX.DirectWrite.TextLayout(NinjaTrader.Core.Globals.DirectWriteFactory, p.Texto, formato, 1000, 100))
                    using (SharpDX.Direct2D1.Brush fondo = p.Color.ToDxBrush(RenderTarget))
                    {
                        float w = layout.Metrics.Width + 12;
                        float x = g.Der + 8 + w <= bordeDerecho ? g.Der + 8 : g.Der - w - 6;
                        float y = Math.Max(p.Y - alto / 2, ocupadoHasta + 2);
                        fondo.Opacity = 0.92f;
                        RenderTarget.FillRoundedRectangle(new SharpDX.Direct2D1.RoundedRectangle
                        {
                            Rect = new SharpDX.RectangleF(x, y, w, alto),
                            RadiusX = 4,
                            RadiusY = 4
                        }, fondo);
                        RenderTarget.DrawTextLayout(new SharpDX.Vector2(x + 6, y + 2), layout, blanco);
                        ocupadoHasta = y + alto;
                    }
                }
            }
        }

        #region Propiedades

        [Range(1, 5)]
        [Display(Name = "Hasta qué R (1 a 5)", Order = 1, GroupName = "Riesgo R")]
        public int NivelesR { get; set; }

        [Range(1, 1000)]
        [Display(Name = "Contratos", Order = 2, GroupName = "Riesgo R")]
        public int Contratos { get; set; }

        [Display(Name = "Mostrar el precio", Order = 3, GroupName = "Riesgo R")]
        public bool MostrarPrecio { get; set; }

        [Display(Name = "Mostrar los puntos", Order = 4, GroupName = "Riesgo R")]
        public bool MostrarPuntos { get; set; }

        [Display(Name = "Mostrar el dinero", Order = 5, GroupName = "Riesgo R")]
        public bool MostrarDinero { get; set; }

        [Range(0, 100)]
        [Display(Name = "Opacidad de las zonas (%)", Order = 6, GroupName = "Riesgo R")]
        public int OpacidadZona { get; set; }

        [Display(Name = "Letra", Order = 7, GroupName = "Riesgo R")]
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
