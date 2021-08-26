using Deedle;
using QuantConnect.Data;
using QuantConnect.Data.Market;
using System;
using System.Collections.Generic;
using System.Linq;
using Frame = Deedle.Frame;
using QuantConnect.Indicators;

namespace QuantConnect.Research
{
    public static class DataFrameExtensions
    {
        private const string DateTimeColumn = "Time";

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
            Dictionary<DateTime, Dictionary<string, object>> timetoSeries = new Dictionary<DateTime, Dictionary<string, object>>();
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
    }
}
