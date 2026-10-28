// Imitación mínima de NinjaTrader 8 y WPF: solo las firmas que usa PrimerIFVG.cs, sin comportamiento.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace System.Windows
{
    public class DependencyObject { }
    public class FrameworkElement : DependencyObject { public DependencyObject Parent { get; set; } }
    public class Window : FrameworkElement { public static Window GetWindow(DependencyObject d) { return null; } }
    public struct Thickness { public Thickness(double l, double t, double r, double b) { } }
    public enum TextAlignment { Left, Right, Center, Justify }
    public struct Point { public Point(double x, double y) { X = x; Y = y; } public double X { get; set; } public double Y { get; set; } }
    public delegate void RoutedEventHandler(object sender, RoutedEventArgs e);
    public class RoutedEventArgs : EventArgs { }
}

namespace System.Windows.Media
{
    public struct Color
    {
        public byte A, R, G, B;
        public static Color FromRgb(byte r, byte g, byte b) { return new Color { A = 255, R = r, G = g, B = b }; }
        public static Color FromArgb(byte a, byte r, byte g, byte b) { return new Color { A = a, R = r, G = g, B = b }; }
    }
    public static class Colors { public static Color Gray { get { return Color.FromRgb(128, 128, 128); } } }
    public abstract class Brush { public void Freeze() { } }
    public class SolidColorBrush : Brush { public SolidColorBrush(Color c) { Color = c; } public Color Color { get; set; } }
    public static class Brushes
    {
        public static SolidColorBrush Black, Gray, White, Orange, Transparent;
    }
}

namespace System.Windows.Input
{
    public class Cursor { }
    public static class Cursors { public static Cursor Pen, SizeAll, SizeNS, No, Arrow, Hand; }
}

namespace System.Windows.Threading
{
    public class DispatcherOperation { }
    public class Dispatcher { public DispatcherOperation InvokeAsync(Action a) { return null; } }
    public class DispatcherTimer
    {
        public TimeSpan Interval { get; set; }
        public event EventHandler Tick;
        public void Start() { }
        public void Stop() { }
    }
}

namespace System.Windows.Controls
{
    using System.Windows.Media;
    public class Control : FrameworkElement
    {
        public object Content { get; set; }
        public Thickness Margin { get; set; }
        public Thickness Padding { get; set; }
        public Brush Foreground { get; set; }
        public Brush Background { get; set; }
        public double FontSize { get; set; }
        public double Opacity { get; set; }
    }
    public class Button : Control { public event RoutedEventHandler Click; }
    public class TabItem : Control { }
    public class SelectionChangedEventArgs : RoutedEventArgs { public IList AddedItems { get; set; } }
    public delegate void SelectionChangedEventHandler(object sender, SelectionChangedEventArgs e);
    public class TabControl : Control
    {
        public IList Items { get; set; }
        public object SelectedItem { get; set; }
        public event SelectionChangedEventHandler SelectionChanged;
    }
}

namespace SharpDX
{
    public struct Vector2 { public Vector2(float x, float y) { } }
    public struct RectangleF { public RectangleF(float x, float y, float w, float h) { } }
}

namespace SharpDX.DirectWrite
{
    public class Factory { }
    public struct TextMetrics { public float Width, Height; }
    public class TextFormat : IDisposable { public float FontSize { get { return 10; } } public void Dispose() { } }
    public class TextLayout : IDisposable
    {
        public TextLayout(Factory f, string text, TextFormat format, float maxWidth, float maxHeight) { }
        public TextMetrics Metrics { get { return new TextMetrics(); } }
        public void Dispose() { }
    }
}

namespace SharpDX.Direct2D1
{
    public class Brush : IDisposable { public float Opacity { get; set; } public void Dispose() { } }
    public enum AntialiasMode { PerPrimitive, Aliased }
    public struct RoundedRectangle { public SharpDX.RectangleF Rect; public float RadiusX, RadiusY; }
    public class RenderTarget
    {
        public AntialiasMode AntialiasMode { get; set; }
        public void DrawLine(SharpDX.Vector2 p0, SharpDX.Vector2 p1, Brush brush, float strokeWidth) { }
        public void FillRoundedRectangle(RoundedRectangle roundedRect, Brush brush) { }
        public void FillRectangle(SharpDX.RectangleF rect, Brush brush) { }
        public void DrawTextLayout(SharpDX.Vector2 origin, SharpDX.DirectWrite.TextLayout textLayout, Brush defaultForegroundBrush) { }
    }
}

namespace NinjaTrader.Cbi { }

namespace NinjaTrader.Core
{
    public class GeneralOptions { public TimeZoneInfo TimeZoneInfo { get; set; } }
    public static class Globals
    {
        public static DateTime Now { get { return DateTime.Now; } }
        public static string InstallDir { get { return ""; } }
        public static GeneralOptions GeneralOptions { get { return null; } }
        public static SharpDX.DirectWrite.Factory DirectWriteFactory { get { return null; } }
    }
}

namespace NinjaTrader.Data
{
    public enum BarsPeriodType { Tick, Volume, Range, Second, Minute, Day, Week, Month, Year }
    public class BarsPeriod { public BarsPeriodType BarsPeriodType { get; set; } public int Value { get; set; } }
    public class Bars
    {
        public int GetBar(DateTime time) { return 0; }
        public double GetClose(int index) { return 0; }
    }
}

namespace NinjaTrader.Gui
{
    using System.Windows.Media;
    public enum DashStyleHelper { Dash, DashDot, DashDotDot, Dot, Solid }
    public class Stroke { public Stroke(Brush brush, DashStyleHelper dashStyleHelper, float width) { } }
    public static class DxExtensions
    {
        public static SharpDX.Direct2D1.Brush ToDxBrush(this Brush brush, SharpDX.Direct2D1.RenderTarget renderTarget) { return null; }
    }
    public static class Serialize
    {
        public static string BrushToString(Brush b) { return ""; }
        public static Brush StringToBrush(string s) { return null; }
    }
}

namespace NinjaTrader.Gui.Tools
{
    public class SimpleFont
    {
        public SimpleFont(string familySerialize, double size) { }
        public SharpDX.DirectWrite.TextFormat ToDirectWriteTextFormat() { return null; }
    }
}

namespace NinjaTrader.Gui.Chart
{
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Media;
    using System.Windows.Threading;
    public enum ScaleJustification { Left, Right, Overlay }
    public class ChartControlProperties { public Brush ChartText { get; set; } public Brush ChartBackground { get; set; } }
    public class ChartScale { public int GetYByValue(double value) { return 0; } public int PanelIndex { get; set; } }
    public class ChartBars { public int FromIndex { get; set; } public int ToIndex { get; set; } }
    public class ChartPanel { public int X { get; set; } public int Y { get; set; } public int W { get; set; } public int H { get; set; } }
    public class ChartControl : FrameworkElement
    {
        public Dispatcher Dispatcher { get; set; }
        public ChartControlProperties Properties { get; set; }
        public int GetXByBarIndex(ChartBars chartBars, int barIndex) { return 0; }
        public System.Collections.Generic.List<ChartPanel> ChartPanels { get; set; }
        public SharpDX.Direct2D1.Brush SelectionBrush { get; set; }
    }
    public class ChartTab : Control { public ChartControl ChartControl { get; set; } }
    public class Chart : Window
    {
        public TabControl MainTabControl { get; set; }
        public ObservableCollection<object> MainMenu { get; set; }
    }
}

namespace NinjaTrader.NinjaScript
{
    using System.Windows.Media;
    using NinjaTrader.Data;
    using NinjaTrader.Gui.Chart;

    public enum State { SetDefaults, Configure, Active, DataLoaded, Historical, Transition, Realtime, Terminated, Finalized }
    public enum Calculate { OnBarClose, OnEachTick, OnPriceChange }
    public enum Priority { High, Low, Medium }
    public interface ISeries<T> { T this[int barsAgo] { get; } }

    public abstract class NinjaScriptBase
    {
        public State State { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public Calculate Calculate { get; set; }
        public double TickSize { get; set; }
        public int BarsInProgress { get; set; }
        public int[] CurrentBars { get; set; }
        public ISeries<DateTime>[] Times { get; set; }
        public ISeries<double>[] Opens { get; set; }
        public ISeries<double>[] Highs { get; set; }
        public ISeries<double>[] Lows { get; set; }
        public ISeries<double>[] Closes { get; set; }
        public Bars[] BarsArray { get; set; }
        public BarsPeriod BarsPeriod { get; set; }
        public bool IsFirstTickOfBar { get; set; }
        public ISeries<double> Input { get; set; }
        protected virtual void OnStateChange() { }
        protected virtual void OnBarUpdate() { }
        public void AddDataSeries(BarsPeriodType periodType, int period) { }
        public void Alert(string id, Priority priority, string message, string soundLocation, int rearmSeconds, Brush backBrush, Brush foreBrush) { }
        public void Print(object value) { }
        public void TriggerCustomEvent(Action<object> customEvent, int barsIndex, object state) { }
        public void RemoveDrawObject(string tag) { }
        public bool EqualsInput(ISeries<double> input) { return false; }
    }
}

namespace NinjaTrader.Gui.NinjaScript
{
    using NinjaTrader.Gui.Chart;
    using NinjaTrader.NinjaScript;
    public abstract class IndicatorRenderBase : NinjaScriptBase
    {
        public ChartControl ChartControl { get; set; }
        public bool IsOverlay { get; set; }
        public bool DisplayInDataBox { get; set; }
        public bool DrawOnPricePanel { get; set; }
        public bool PaintPriceMarkers { get; set; }
        public bool IsSuspendedWhileInactive { get; set; }
        public ScaleJustification ScaleJustification { get; set; }
        public void ForceRefresh() { }
        public ChartPanel ChartPanel { get; set; }
        public ChartBars ChartBars { get; set; }
        public bool IsInHitTest { get; set; }
        public SharpDX.Direct2D1.RenderTarget RenderTarget { get; set; }
        protected virtual void OnRender(ChartControl chartControl, ChartScale chartScale) { }
        protected T CacheIndicator<T>(T indicator, ISeries<double> input, ref T[] cache) where T : NinjaTrader.NinjaScript.Indicators.Indicator { return indicator; }
    }
    public abstract class StrategyRenderBase : NinjaScriptBase { protected NinjaTrader.NinjaScript.Indicators.Indicator indicator; }
}

namespace NinjaTrader.NinjaScript.Indicators
{
    public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase { }
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
    public abstract class MarketAnalyzerColumnBase : NinjaScriptBase { protected NinjaTrader.NinjaScript.Indicators.Indicator indicator; }
}

namespace NinjaTrader.NinjaScript.DrawingTools
{
    using System.Windows;
    using System.Windows.Media;
    using NinjaTrader.Gui;
    using NinjaTrader.Gui.Tools;

    public enum TextPosition { BottomLeft, BottomRight, Center, TopLeft, TopRight }
    public enum DrawingState { Building, Normal, Editing, Moving }
    public class MasterInstrument
    {
        public string Name { get; set; }
        public double TickSize { get; set; }
        public double PointValue { get; set; }
        public double RoundToTickSize(double price) { return price; }
    }
    public class InstrumentImitado { public MasterInstrument MasterInstrument { get; set; } }
    public class Adjunto { public InstrumentImitado Instrument { get; set; } }
    public class ChartAnchor
    {
        public bool IsBrowsable { get; set; }
        public bool IsEditing { get; set; }
        public DrawingTool DrawingTool { get; set; }
        public string DisplayName { get; set; }
        public double Price { get; set; }
        public DateTime Time { get; set; }
        public void CopyDataValues(ChartAnchor destino) { }
        public Point GetPoint(NinjaTrader.Gui.Chart.ChartControl c, NinjaTrader.Gui.Chart.ChartPanel p, NinjaTrader.Gui.Chart.ChartScale s) { return new Point(); }
        public void MoveAnchor(ChartAnchor inicial, ChartAnchor dataPoint, NinjaTrader.Gui.Chart.ChartControl c, NinjaTrader.Gui.Chart.ChartPanel p, NinjaTrader.Gui.Chart.ChartScale s, DrawingTool d) { }
    }
    public abstract class DrawingTool : NinjaScriptBase, IDisposable
    {
        public bool IsLocked { get; set; }
        public bool IsAutoScale { get; set; }
        public bool IsSelected { get; set; }
        public bool IsInHitTest { get; set; }
        public DrawingState DrawingState { get; set; }
        public ChartAnchor InitialMouseDownAnchor { get; set; }
        public Adjunto AttachedTo { get; set; }
        public SharpDX.Direct2D1.RenderTarget RenderTarget { get; set; }
        public virtual System.Collections.Generic.IEnumerable<ChartAnchor> Anchors { get { return new ChartAnchor[0]; } }
        public virtual System.Windows.Input.Cursor GetCursor(NinjaTrader.Gui.Chart.ChartControl c, NinjaTrader.Gui.Chart.ChartPanel p, NinjaTrader.Gui.Chart.ChartScale s, Point point) { return null; }
        public virtual Point[] GetSelectionPoints(NinjaTrader.Gui.Chart.ChartControl c, NinjaTrader.Gui.Chart.ChartScale s) { return null; }
        public virtual bool IsVisibleOnChart(NinjaTrader.Gui.Chart.ChartControl c, NinjaTrader.Gui.Chart.ChartScale s, DateTime first, DateTime last) { return true; }
        public virtual void OnMouseDown(NinjaTrader.Gui.Chart.ChartControl c, NinjaTrader.Gui.Chart.ChartPanel p, NinjaTrader.Gui.Chart.ChartScale s, ChartAnchor dataPoint) { }
        public virtual void OnMouseMove(NinjaTrader.Gui.Chart.ChartControl c, NinjaTrader.Gui.Chart.ChartPanel p, NinjaTrader.Gui.Chart.ChartScale s, ChartAnchor dataPoint) { }
        public virtual void OnMouseUp(NinjaTrader.Gui.Chart.ChartControl c, NinjaTrader.Gui.Chart.ChartPanel p, NinjaTrader.Gui.Chart.ChartScale s, ChartAnchor dataPoint) { }
        public virtual void OnRender(NinjaTrader.Gui.Chart.ChartControl c, NinjaTrader.Gui.Chart.ChartScale s) { }
        protected ChartAnchor GetClosestAnchor(NinjaTrader.Gui.Chart.ChartControl c, NinjaTrader.Gui.Chart.ChartPanel p, NinjaTrader.Gui.Chart.ChartScale s, double sensibilidad, Point point) { return null; }
        public void Dispose() { }
    }
    public abstract class ShapeBase : DrawingTool { public Stroke OutlineStroke { get; set; } }
    public class Rectangle : ShapeBase { }
    public class Line : DrawingTool { }
    public class Ray : Line { }
    public class Text : DrawingTool { }
    public class TextFixed : Text { }

    public static class Draw
    {
        public static Rectangle Rectangle(NinjaScriptBase owner, string tag, bool isAutoScale, DateTime startTime, double startY, DateTime endTime, double endY, Brush brush, Brush areaBrush, int areaOpacity) { return new Rectangle(); }
        public static Line Line(NinjaScriptBase owner, string tag, bool isAutoScale, DateTime startTime, double startY, DateTime endTime, double endY, Brush brush, DashStyleHelper dashStyle, int width) { return new Line(); }
        public static Ray Ray(NinjaScriptBase owner, string tag, bool isAutoScale, int startBarsAgo, double startY, int endBarsAgo, double endY, Brush brush, DashStyleHelper dashStyle, int width) { return new Ray(); }
        public static Ray Ray(NinjaScriptBase owner, string tag, DateTime startTime, double startY, DateTime endTime, double endY, Brush brush, DashStyleHelper dashStyle, int width) { return new Ray(); }
        public static Text Text(NinjaScriptBase owner, string tag, bool isAutoScale, string text, DateTime time, double y, int yPixelOffset, Brush textBrush, SimpleFont font, TextAlignment alignment, Brush outlineBrush, Brush areaBrush, int areaOpacity) { return new Text(); }
        public static TextFixed TextFixed(NinjaScriptBase owner, string tag, string text, TextPosition textPosition, Brush textBrush, SimpleFont font, Brush outlineBrush, Brush areaBrush, int areaOpacity) { return new TextFixed(); }
    }
}
