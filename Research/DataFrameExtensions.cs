using Deedle;
using QuantConnect.Data;
using QuantConnect.Data.Market;
using System;
using System.Collections.Generic;
using System.Linq;
using Frame = Deedle.Frame;
using QuantConnect.Indicators;
using Microsoft.FSharp.Core;
using Plotly.NET;
using Chart2D = Plotly.NET.Chart2D.Chart;
using Plotly.NET.TraceObjects;
using Plotly.NET.LayoutObjects;
using QuantConnect.Util;

namespace QuantConnect.Research
{
    public static class DataFrameExtensions
    {
        private const string DateTimeColumn = "Time";
        private static readonly string[] IgnoredProperties = new string[] { "OPEN", "HIGH", "LOW", "CLOSE", "VOLUME" };

        public static Frame<DateTime, string> ToDataFrame<T>(this IEnumerable<Slice> slices, Func<Slice, DataDictionary<T>> getDataDictionary)
            where T : IBar
        {
            if (getDataDictionary is null)
                return null;

            Frame<DateTime, string> dataFrame = null;
            foreach (var slice in slices)
            {
                var df = ToDataFrame(slice, getDataDictionary);

                if (dataFrame == null)
                    dataFrame = df;
                else
                {
                    //concat
                    dataFrame = dataFrame.Merge(df);
                }
            }

            dataFrame = dataFrame.IndexRows<DateTime>(DateTimeColumn).SortRowsByKey();

            return dataFrame;
        }

        public static Frame<DateTime, string> ToDataFrame<T>(this Slice slice, Func<Slice, DataDictionary<T>> getDataDictionary)
        {
            if (getDataDictionary is null)
                return null;

            DataDictionary<T> dataDictionary = getDataDictionary(slice);

            var enumerator = dataDictionary.GetEnumerator();

            List<KeyValuePair<DateTime, Series<string, object>>> rows = new List<KeyValuePair<DateTime, Series<string, object>>>();
            while (enumerator.MoveNext())
            {
                var symbolToData = enumerator.Current;

                var builder = new SeriesBuilder<string>();
                builder.Add(DateTimeColumn, slice.Time);
                builder.Add(nameof(Symbol), symbolToData.Key.Value);

                if (symbolToData.Value is IBar bar)
                {
                    builder.Add(nameof(bar.Open), bar.Open);
                    builder.Add(nameof(bar.High), bar.High);
                    builder.Add(nameof(bar.Low), bar.Low);
                    builder.Add(nameof(bar.Close), bar.Close);
                }

                if (symbolToData.Value is TradeBar tradeBar)
                {
                    builder.Add(nameof(tradeBar.Volume), tradeBar.Volume);
                }



                rows.Add(new KeyValuePair<DateTime, Series<string, object>>(slice.Time, builder.Series));
            }

            return Frame.FromRows(rows);
        }

        public static Frame<DateTime, string> ToDataFrame(this Dictionary<string, List<IndicatorDataPoint>> data)
        {
            Dictionary<DateTime, Dictionary<string, object>> timetoSeries = new();
            foreach (var kvp in data)
            {
                foreach (var item in kvp.Value)
                {
                    var index = item.EndTime;

                    Dictionary<string, object> builder;
                    if (!timetoSeries.ContainsKey(index))
                    {
                        builder = new Dictionary<string, object>();
                        timetoSeries.Add(index, builder);
                    }
                    else
                        builder = timetoSeries[index];

                    if (!builder.ContainsKey(kvp.Key))
                        builder.Add(kvp.Key, item.Value);
                    else
                        throw new InvalidOperationException($"Duplicate time {index}");
                }
            }

            return Frame.FromRows(timetoSeries.ToList().Select(kvp => KeyValuePair.Create(kvp.Key, kvp.Value.ToSeries())));
        }

        public static Frame<(SecurityIdentifier, DateTime), string> ToDataFrame(this Dictionary<string, List<IndicatorDataPoint>> data, Symbol symbol)
        {
            Dictionary<(SecurityIdentifier, DateTime), Dictionary<string, object>> timetoSeries = new();
            foreach (var kvp in data)
            {
                foreach (var item in kvp.Value)
                {
                    var index = (symbol.ID, item.EndTime);

                    Dictionary<string, object> builder;
                    if (!timetoSeries.ContainsKey(index))
                    {
                        builder = new Dictionary<string, object>();
                        timetoSeries.Add(index, builder);
                    }
                    else
                        builder = timetoSeries[index];

                    if (!builder.ContainsKey(kvp.Key))
                        builder.Add(kvp.Key, item.Value);
                    else
                        throw new InvalidOperationException($"Duplicate time {index}");
                }
            }

            return Frame.FromRows(timetoSeries.ToList().Select(kvp => KeyValuePair.Create(kvp.Key, kvp.Value.ToSeries())));
        }

        public static Frame<(SecurityIdentifier, DateTime), string> Merge(this IEnumerable<Frame<(SecurityIdentifier, DateTime), string>> frames)
        {
            Frame<(SecurityIdentifier, DateTime), string> mergedFrame = null;
            frames.DoForEach(frame =>
            {
                if (mergedFrame is null)
                    mergedFrame = frame;
                else
                    mergedFrame = mergedFrame.Merge(frame);
            });
            return mergedFrame;
        }

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
                    StyleParam.MarkerSymbol symbol = ConvertScatterMarkerSymbolToPlotly(series.ScatterMarkerSymbol);
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

        private static StyleParam.MarkerSymbol? ConvertScatterMarkerSymbolToPlotly(ScatterMarkerSymbol scatterMarkerSymbol)
        {
            switch (scatterMarkerSymbol)
            {
                case ScatterMarkerSymbol.None:
                    return null;
                case ScatterMarkerSymbol.Circle:
                    return StyleParam.MarkerSymbol.Circle;
                case ScatterMarkerSymbol.Square:
                    return StyleParam.MarkerSymbol.Square;
                case ScatterMarkerSymbol.Diamond:
                    return StyleParam.MarkerSymbol.Diamond;
                case ScatterMarkerSymbol.Triangle:
                    return StyleParam.MarkerSymbol.TriangleUp;
                case ScatterMarkerSymbol.TriangleDown:
                    return StyleParam.MarkerSymbol.TriangleDown;
                default:
                    return StyleParam.MarkerSymbol.Circle;
            }
        }

        private static GenericChart.GenericChart Style(this GenericChart.GenericChart chart, string title)
        {
            return chart.WithLayout(Layout.init<string>
            (
                Title: Title.init(title),
                Font: null,
                ShowLegend: null,
                AutoSize: true,
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
                PaperBGColor: null,
                PlotBGColor: null,
                HoverMode: null,
                DragMode: StyleParam.DragMode.Zoom
            ))
                .WithXAxisStyle(Title.init("Date"))
                .WithYAxisStyle(Title.init("Price"), Side: StyleParam.Side.Left, Id: StyleParam.SubPlotId.NewYAxis(1))
                .WithYAxisStyle(Title.init("Unit"), Overlaying: StyleParam.LinearAxisId.NewY(1), Side: StyleParam.Side.Right, Id: StyleParam.SubPlotId.NewYAxis(2));
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

            var rangeslider = RangeSlider.init(
                BgColor: null,
                BorderColor: null,
                BorderWidth: null,
                AutoRange: null,
                Range: null,
                Thickness: null,
                Visible: false,
                YAxisRangeMode: null,
                YAxisRange: null);

            var chart = Chart2D.Candlestick
            (
                x: data.X,
                open: data.Open,
                high: data.High,
                low: data.Low,
                close: data.Close
            )
            .WithTraceName("OHLC")
            .WithLegend(true)
            .WithXAxisRangeSlider(rangeslider)
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

            var barTrace = Chart2D.Column<decimal, DateTime, int, int, string>
            (
                Keys: new FSharpOption<IEnumerable<DateTime>>(dataFrame.RowIndex.KeySequence),

                values: GetDecimalValues(dataFrame.Columns[nameof(bar.Volume)].Values),

                Opacity: 0.5d,

                Name: "Volume",

                Marker: marker

            ).WithAxisAnchor(Y: 2);

            return barTrace;
        }

        private static GenericChart.GenericChart AddIndicatorToChart(Frame<DateTime, string> dataFrame, string name, SeriesType type, bool isPriceRelated, string color = null, StyleParam.MarkerSymbol? symbol = null)
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
                    trace = Chart2D.Line
                    (
                        Name: name,

                        x: ohlcData.X,

                        y: ohlcData.Close,

                        Labels: labels,

                        MarkerSymbol: symbol,

                        Color: new FSharpOption<Color>(Color.fromString(color))
                    );
                    break;
                case SeriesType.Scatter:
                case SeriesType.Candle: //candles already exist
                case SeriesType.Flag:
                    trace = Chart2D.Point
                    (
                        Name: name,

                        x: ohlcData.X,

                        y: ohlcData.Close,

                        Labels: labels,

                        Opacity: 0.5d,

                        MarkerSymbol: symbol,

                        Color: new FSharpOption<Color>(Color.fromString(color))
                    );
                    break;
                case SeriesType.Bar:
                    trace = Chart2D.Column<decimal, DateTime, int, int, string>
                    (
                        Name: name,

                        Keys: new FSharpOption<IEnumerable<DateTime>>(ohlcData.X),

                        values: labels.Value,

                        Opacity: 0.5d
                    );
                    break;
                case SeriesType.StackedArea:
                    trace = Chart2D.StackedArea
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
                            trace = Chart2D.Line
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
