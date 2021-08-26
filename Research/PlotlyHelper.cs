using Deedle;
using QuantConnect.Data.Market;
using System;
using System.Collections.Generic;
using System.Linq;
using Plotly.NET;
using PChart = Plotly.NET.Chart;
using Microsoft.FSharp.Core;

namespace QuantConnect.Research
{
    public static class PlotlyHelper
    {
        private static readonly string[] IgnoredProperties = new string[] { "OPEN", "HIGH", "LOW", "CLOSE", "VOLUME" };

        public static GenericChart.GenericChart Plot(object dataFrame, string preTitleText = "", params Series[] seriesArray)
        {
            if (dataFrame is Frame<DateTime, string> frame)
            {
                return Plot(frame, preTitleText, seriesArray);
            }
            else
                return null;
        }

        public static GenericChart.GenericChart Plot(Frame<DateTime, string> dataFrame, string preTitleText = "", params Series[] seriesArray)
        {
            var charts = new List<GenericChart.GenericChart>();

            var ohlc = AddOhlcChart(dataFrame);

            if (ohlc != null)
                charts.Add(ohlc);

            var volume = AddVolumeChart(dataFrame);

            if (volume != null)
                charts.Add(volume);

            if (ohlc != null && seriesArray != null)
            {
                foreach (var series in seriesArray)
                {
                    string color = series.Color == System.Drawing.Color.Empty ? null : $"rgb({series.Color.R}, {series.Color.G}, {series.Color.B})";
                    StyleParam.Symbol? symbol = ConvertScatterMarkerSymbolToPlotly(series.ScatterMarkerSymbol);
                    var indicator = AddIndicatorToChart(dataFrame, series.Name, series.SeriesType, series.Unit == "$", color, symbol);

                    if (indicator != null)
                    {
                        charts.Add(indicator);
                    }
                }
            }

            var chart = GenericChart.combine(charts).Style($"{preTitleText}{GetTitle(charts)}");

            return chart;
        }

        public static IEnumerable<GenericChart.GenericChart> AddToPlot(IEnumerable<GenericChart.GenericChart> plots, GenericChart.GenericChart newChartComponent, string specificGraphIdentifierByTitle = null)
        {
            if (plots?.Any() ?? true)
                throw new ArgumentException("plots are empty");

            var traceAdded = false;
            var titles = new List<string>();

            var enumerator = plots.GetEnumerator();

            while (enumerator.MoveNext())
            {
                var plot = enumerator.Current;
                var layout = GetPlotLayout(plot);
                var title = layout.TryGetTypedValue<string>("title");
                titles.Add(title.Value);

                if (title.Value.Contains(specificGraphIdentifierByTitle, StringComparison.InvariantCultureIgnoreCase) ||
                    plots.Count() == 1)
                {
                    plot = AddToPlot(plot, newChartComponent);
                    traceAdded = true;
                    break;
                }
            }

            if (!traceAdded)
                throw new ArgumentException($"specific_graph_identifier_by_title={specificGraphIdentifierByTitle} was not in any graph title. Options are {titles}");

            return plots;
        }

        public static GenericChart.GenericChart AddToPlot(GenericChart.GenericChart plot, GenericChart.GenericChart newChartComponent)
        {
            var layout = GetPlotLayout(plot);
            return GenericChart.combine(new GenericChart.GenericChart[] { plot, newChartComponent }).WithLayout(layout);
        }

        private static StyleParam.Symbol? ConvertScatterMarkerSymbolToPlotly(ScatterMarkerSymbol scatterMarkerSymbol)
        {
            switch (scatterMarkerSymbol)
            {
                case ScatterMarkerSymbol.None:
                    return null;
                case ScatterMarkerSymbol.Circle:
                    return StyleParam.Symbol.Circle;
                case ScatterMarkerSymbol.Square:
                    return StyleParam.Symbol.Square;
                case ScatterMarkerSymbol.Diamond:
                    return StyleParam.Symbol.Diamond;
                case ScatterMarkerSymbol.Triangle:
                    return StyleParam.Symbol.TriangleUp;
                case ScatterMarkerSymbol.TriangleDown:
                    return StyleParam.Symbol.TriangleDown;
                default:
                    return StyleParam.Symbol.Circle;
            }
        }

        private static GenericChart.GenericChart Style(this GenericChart.GenericChart chart, string title)
        {
            return chart.WithLayout(Layout.init<string, string, string, string, string, string, string>
            (
                Title: title,
                Titlefont: null,
                Font: null,
                Showlegend: null,
                Autosize: true,
                Width: null,
                Height: null,
                Legend: Legend.init
                (
                    BGColor: null,
                    BorderColor: null,
                    Borderwidth: null,
                    Orientation: null,
                    TraceOrder: null,
                    TraceGroupGap: null,
                    ItemSizing: null,
                    ItemWidth: null,
                    ItemClick: null,
                    ItemDoubleClick: null,
                    X: 1.05, //add space to not overlap y2 axis
                    Y: 1,
                    XAnchor: null,
                    YAnchor: null,
                    VerticalAlign: null,
                    Title: null
                ),
                Annotations: null,
                Margin: null,
                Paper_bgcolor: null,
                Plot_bgcolor: null,
                Hovermode: null,
                Dragmode: StyleParam.Dragmode.Zoom,
                Separators: null,
                Barmode: null,
                Bargap: null,
                Radialaxis: null,
                Angularaxis: null,
                Scene: null,
                Direction: null,
                Orientation: null,
                Shapes: null,
                Hidesources: null,
                Smith: null,
                Geo: null
            ))
                .WithX_AxisStyle("Date")
                .WithY_AxisStyle("Price", Side: StyleParam.Side.Left, Id: 1)
                .WithY_AxisStyle("Unit", Overlaying: StyleParam.AxisAnchorId.NewY(1), Side: StyleParam.Side.Right, Id: 2);
        }

        private static Layout GetPlotLayout(GenericChart.GenericChart plot)
        {
            if (plot is GenericChart.GenericChart.Chart chart)
                return chart.Item2;
            else if (plot is GenericChart.GenericChart.MultiChart mchart)
                return mchart.Item2;
            else
                return null;
        }

        private static string GetTitle(List<GenericChart.GenericChart> charts)
        {
            string title = "";

            if (charts.Any(t =>
            {
                if (t.IsChart)
                {
                    var chart = (GenericChart.GenericChart.Chart)t;

                    return chart.Item1.type == "candlestick";
                }
                else
                    return false;
            })
                )
                title += "OHLC";

            if (charts.Any(t =>
            {
                if (t.IsChart)
                {
                    var chart = (GenericChart.GenericChart.Chart)t;

                    return chart.Item1.TryGetTypedValue<string>("name").Value == "Volume";
                }
                else
                    return false;
            })
                )
                title += "V";

            return title;
        }

        private class OhlcData
        {
            public IEnumerable<DateTime> X { get; }

            public IEnumerable<decimal> Open { get; }

            public IEnumerable<decimal> High { get; }

            public IEnumerable<decimal> Low { get; }

            public IEnumerable<decimal> Close { get; }

            public OhlcData(IEnumerable<DateTime> x, IEnumerable<decimal> open, IEnumerable<decimal> high, IEnumerable<decimal> low, IEnumerable<decimal> close)
            {
                X = x;
                Open = open;
                High = high;
                Low = low;
                Close = close;
            }
        }

        private static OhlcData GetOhlcData(Frame<DateTime, string> dataFrame)
        {
            IBar bar;
            if (!dataFrame.ColumnKeys.Contains(nameof(bar.Open)) ||
                    !dataFrame.ColumnKeys.Contains(nameof(bar.High)) ||
                    !dataFrame.ColumnKeys.Contains(nameof(bar.Low)) ||
                    !dataFrame.ColumnKeys.Contains(nameof(bar.Close))
                    )
            {
                return null;
            }

            var data = new OhlcData
            (
                x: dataFrame.RowIndex.KeySequence,
                open: GetDecimalValues(dataFrame.Columns[nameof(bar.Open)].Values),
                high: GetDecimalValues(dataFrame.Columns[nameof(bar.High)].Values),
                low: GetDecimalValues(dataFrame.Columns[nameof(bar.Low)].Values),
                close: GetDecimalValues(dataFrame.Columns[nameof(bar.Close)].Values)
            );

            return data;
        }

        private static GenericChart.GenericChart AddOhlcChart(Frame<DateTime, string> dataFrame)
        {

            if (dataFrame is null)
            {
                throw new ArgumentNullException(nameof(dataFrame));
            }

            var data = GetOhlcData(dataFrame);

            if (data is null)
                return null;

            var rangeslider = RangeSlider.init<DateTime, decimal>(
                BgColor: null,
                BorderColor: null,
                BorderWidth: null,
                AutoRange: null,
                Range: null,
                Thickness: null,
                Visible: false,
                YAxisRangeMode: null,
                YAxisRange: null);

            var chart = PChart.Candlestick
            (
                x: data.X,
                open: data.Open,
                high: data.High,
                low: data.Low,
                close: data.Close
            )
            .WithTraceName("OHLC")
            .WithLegend(true)
            .WithX_AxisRangeSlider(rangeslider)
            .WithAxisAnchor(Y: 1);

            return chart;
        }

        private static GenericChart.GenericChart AddVolumeChart(Frame<DateTime, string> dataFrame)
        {
            TradeBar bar;
            if (dataFrame is null)
            {
                throw new ArgumentNullException(nameof(dataFrame));
            }
            else if (!dataFrame.ColumnKeys.Contains(nameof(bar.Volume)))
            {
                return null;
            }

            var marker = new Marker();
            marker.SetValue("color", "rgb(7, 89, 148)");

            var barTrace = PChart.Column<DateTime, decimal, string>
            (
                keys: dataFrame.RowIndex.KeySequence,

                values: GetDecimalValues(dataFrame.Columns[nameof(bar.Volume)].Values),

                Opacity: 0.5d,

                Name: "Volume",

                Marker: marker

            ).WithAxisAnchor(Y: 2);

            return barTrace;
        }

        private static GenericChart.GenericChart AddIndicatorToChart(Frame<DateTime, string> dataFrame, string name, SeriesType type, bool isPriceRelated, string color = null, StyleParam.Symbol? symbol = null)
        {
            var columnNames = dataFrame.Columns.Keys.Select(x => x.ToUpperInvariant());

            if (!columnNames.Contains(name.ToUpperInvariant()))
                throw new ArgumentException($"{name} is not a column name in your dataframe.");

            //drop rows that are empty
            dataFrame = dataFrame.DropSparseRows();

            var ohlcData = GetOhlcData(dataFrame);

            if (ohlcData is null)
                throw new ArgumentException($"No OHLC data found in dataframe.");

            var labels = new FSharpOption<IEnumerable<decimal>>(GetDecimalValues(dataFrame.Columns[name].Values));

            GenericChart.GenericChart trace;
            switch (type)
            {
                case SeriesType.Line:
                    trace = PChart.Line
                    (
                        Name: name,

                        x: ohlcData.X,

                        y: ohlcData.Close,

                        Labels: labels,

                        MarkerSymbol: symbol,

                        Color: color
                    );
                    break;
                case SeriesType.Scatter:
                case SeriesType.Candle: //candles already exist
                case SeriesType.Flag:
                    trace = PChart.Point
                    (
                        Name: name,

                        x: ohlcData.X,

                        y: ohlcData.Close,

                        Labels: labels,

                        Opacity: 0.5d,

                        MarkerSymbol: symbol,

                        Color: color
                    );
                    break;
                case SeriesType.Bar:
                    trace = PChart.Bar
                    (
                        Name: name,

                        keys: ohlcData.X,

                        values: labels.Value,

                        Opacity: 0.5d,

                        Labels: labels
                    );
                    break;
                case SeriesType.StackedArea:
                    trace = PChart.StackedArea
                    (
                        Name: name,

                        x: ohlcData.X,

                        y: labels.Value,

                        Opacity: 0.5d,

                        Labels: labels
                    );
                    break;
                //case SeriesType.Pie:
                //   break;
                //case SeriesType.Treemap:
                //trace = XChart.Treemap
                //(
                //    Name: name,

                //    x: ohlcData.X,

                //    y: labels.Value,

                //    Opacity: 0.5d,

                //    Labels: labels
                //);
                //break;
                default:
                    throw new NotSupportedException($"{type} is not a supported chart.");
            }

            if (isPriceRelated)
                trace.WithAxisAnchor(Y: 1);
            else
                trace.WithAxisAnchor(Y: 2);

            return trace;
        }

        private static List<GenericChart.GenericChart> AddIndicatorsToChart(Frame<DateTime, string> dataFrame, GenericChart.GenericChart candlestick)
        {
            if (dataFrame is null)
            {
                throw new ArgumentNullException(nameof(dataFrame));
            }
            else if (candlestick is null)
            {
                throw new ArgumentNullException(nameof(candlestick));
            }

            List<GenericChart.GenericChart> traces = new List<GenericChart.GenericChart>();

            if (candlestick is GenericChart.GenericChart.Chart chart)
            {
                var closes = chart.Item1.TryGetTypedValue<IEnumerable<decimal>>("close").Value;

                var min_threshold = closes.Min();
                var max_threshold = closes.Max();

                min_threshold = min_threshold * 0.8m;
                max_threshold = max_threshold * 1.2m;

                foreach (var nameToValues in dataFrame.Columns.Observations)
                {
                    //skip candlestick data
                    if (IgnoredProperties.Contains(nameToValues.Key.ToUpperInvariant()))
                        continue;

                    var firstValue = nameToValues.Value.Values.First();

                    if (firstValue is decimal value)
                    {
                        bool isPriceRelated = false;
                        if (value > min_threshold && value < max_threshold)
                            isPriceRelated = true;

                        GenericChart.GenericChart trace;
                        if (isPriceRelated)
                        {
                            //price axis
                            trace = PChart.Line
                            (
                                Name: nameToValues.Key,

                                x: dataFrame.RowIndex.KeySequence,

                                y: closes,

                                Labels: new FSharpOption<IEnumerable<decimal>>(GetDecimalValues(nameToValues.Value.Values))
                            ).WithAxisAnchor(Y: 1);
                        }
                        else
                            continue;
                        //{
                        //    trace = XChart.Point
                        //    (
                        //        Name: nameToValues.Key,

                        //        x: dataFrame.RowIndex.KeySequence,

                        //        y: closes,

                        //        Labels: new FSharpOption<IEnumerable<decimal>>(GetDecimalValues(nameToValues.Value.Values))
                        //    ).WithAxisAnchor(Y: 2);
                        //}

                        traces.Add(trace);
                    }

                }
            }

            return traces;
        }

        private static IEnumerable<decimal> GetDecimalValues(object traceProperty)
        {
            if (traceProperty is IEnumerable<object> values)
                return values.Select(x => (decimal)x);
            else
                return null;
        }
    }
}
