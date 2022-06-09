using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Python.Runtime;
using QuantConnect.Algorithm.Framework.Alphas;
using QuantConnect.Data;
using QuantConnect.Data.Consolidators;
using QuantConnect.Data.Market;
using QuantConnect.Data.UniverseSelection;
using QuantConnect.Indicators;
using QuantConnect.Logging;
using QuantConnect.Securities;
using QuantConnect.Util;

namespace QuantConnect.Research
{
    public partial class QuantBook
    {
        public dynamic RunAlpha(IChartableAlphaModel alpha, int period, Resolution? resolution = null)
        {
            var history = History(period, resolution);
            return RunAlpha(alpha, history);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="alpha"></param>
        /// <param name="start"></param>
        /// <param name="end">if null then present</param>
        /// <param name="resolution"></param>
        /// <returns></returns>
        public dynamic RunAlpha(IChartableAlphaModel alpha, DateTime start, DateTime? end = null, Resolution? resolution = null)
        {
            if(end is null)
            {
                end = DateTime.UtcNow;
            }

            var history = History(Securities.Keys, start, end.Value, resolution);
            return RunAlpha(alpha, history);
        }

        /// <summary>
        /// Gets the historical data of an bar indicator and convert it into pandas.DataFrame
        /// </summary>
        /// <param name="indicator">Bar indicator</param>
        /// <param name="history">Historical data used to calculate the indicator</param>
        /// <param name="selector">Selects a value from the BaseData to send into the indicator, if null defaults to the Value property of BaseData (x => x.Value)</param>
        /// <returns>pandas.DataFrame containing the historical data of <param name="indicator"></returns>
        private dynamic RunAlpha(IChartableAlphaModel alpha, IEnumerable<Slice> history)
        {
            SetAlpha(alpha);

            static IBaseData selectorToUse(IndicatorBase indicator, IBaseData data) => indicator is IndicatorBase<IndicatorDataPoint> ?
                                                            new IndicatorDataPoint(data.Symbol, data.EndTime, data.Value) :
                                                            data;

            Dictionary<Symbol, Dictionary<string, List<IndicatorDataPoint>>> symbolsToIndicatorNameToDataPoints = new();
            Dictionary<Symbol, ISet<IDataConsolidator>> symbolToConsolidators = new();
            IReadOnlyList<Symbol> activeSymbols = null;
            history.DoForEach(slice =>
            {
                var newAddedSymbols = activeSymbols is null ? slice.Keys : slice.Keys.Where(s => !activeSymbols.Contains(s));
                var newRemovedSymbols = activeSymbols?.Where(s => !slice.Keys.Contains(s)) ?? Enumerable.Empty<Symbol>();

                var added = newAddedSymbols.Select(s => AddSecurity(s));
                var removed = newRemovedSymbols.Select(s => Securities[s]);

                alpha.OnSecuritiesChanged(this, SecurityChanges.Create(added.Where(s => !s.IsInternalFeed()).ToList(),
                    added.Where(s => !s.IsInternalFeed()).ToList(),
                    removed.Where(s => !s.IsInternalFeed()).ToList(),
                    removed.Where(s => s.IsInternalFeed()).ToList()));
                //newRemovedSymbols.DoForEach(s => RemoveSecurity(s.Symbol));

                activeSymbols = slice.Keys;

                
                var insightsBySymbol = alpha.Update(this, slice).ToDictionary(i => i.Symbol);

                //record
                slice.Keys.DoForEach(symbol =>
                {


                    if (!symbolToConsolidators.ContainsKey(symbol))
                    {
                        symbolToConsolidators.Add(symbol, GetRegisteredConsolidators(symbol));
                    }

                    var consolidators = symbolToConsolidators[symbol];

                    consolidators.DoForEach(consolidator =>
                   {
                       var data = slice.Get(consolidator.InputType);

                       if (data.ContainsKey(symbol))
                       {
                           var lastBar = (IBaseData)data[symbol];
                           consolidator.Update(lastBar);
                           consolidator.Scan(lastBar.EndTime);
                       }
                   });

                    //var data = ConvertToData(slice, symbol);

                    var alphaData = alpha[symbol];

                    if (alphaData is null)
                    {
                        Log($"No alpha data for {symbol}");
                        return;
                    }

                    if (!symbolsToIndicatorNameToDataPoints.ContainsKey(symbol))
                        symbolsToIndicatorNameToDataPoints.Add(symbol, new());

                    //alphaData.IndicatorsToUpdate.DoForEach(indicator => indicator.Update(selectorToUse(indicator, data)));

                    var alphaDataProperties = alphaData.GetChartableIndicators();

                    alphaDataProperties.DoForEach(alphaDataProperty =>
                    {
                        var name = alphaDataProperty.Key;
                        var value = alphaDataProperty.Value;

                        if (value is IndicatorBase indicator)
                        {
                            if (!symbolsToIndicatorNameToDataPoints[symbol].ContainsKey(name))
                                symbolsToIndicatorNameToDataPoints[symbol].Add(name, new());

                            symbolsToIndicatorNameToDataPoints[symbol][name].Add(indicator.Current);
                        }
                        else if (value is ValueType || value is Delegate)
                        {
                            try
                            {
                                if (value is Delegate func)
                                {
                                    value = func.DynamicInvoke();
                                }

                                if (value is ValueType)
                                {
                                    if (!symbolsToIndicatorNameToDataPoints[symbol].ContainsKey(name))
                                        symbolsToIndicatorNameToDataPoints[symbol].Add(name, new());

                                    decimal? decimalValue = ConvertToDecimal(value);

                                    if(decimalValue.HasValue)
                                        symbolsToIndicatorNameToDataPoints[symbol][name].Add(new IndicatorDataPoint(slice.Time, decimalValue.Value));
                                }
                            }
                            catch { }
                        }
                    });

                    //add insight
                    if (insightsBySymbol.ContainsKey(symbol))
                    {
                        var insight = insightsBySymbol[symbol];
                        InitializeInsightFields(insight);

                        decimal insightDirection = (decimal)insight.Direction;

                        var name = nameof(Insight);
                        if (!symbolsToIndicatorNameToDataPoints[symbol].ContainsKey(name))
                        {
                            symbolsToIndicatorNameToDataPoints[symbol].Add(name, new());
                        }

                        symbolsToIndicatorNameToDataPoints[symbol][name].Add(new IndicatorDataPoint(slice.Time, insightDirection));
                    }
                });
            });

            var frames = symbolsToIndicatorNameToDataPoints.Select(
                symbolToIndicatorNameToDataPoints =>
            {
                var symbol = symbolToIndicatorNameToDataPoints.Key;
                var properties = symbolToIndicatorNameToDataPoints.Value;

                dynamic frame;
                if (_isPythonNotebook)
                {
                    frame = PandasConverter.GetIndicatorDataFrameWithSymbol(symbol, properties);
                }
                else
                {
                    frame = properties.ToDataFrame(symbol);
                }

                return frame;
            });

            //concat
            if (_isPythonNotebook)
            {

                var dataFrame = PandasConverter.Concat(frames.Select(x => (PyObject)x));

                using (Py.GIL())
                {
                    //series to dict
                    var series = alpha.GetSeriesPlotOptions().Select(series => ConvertToPyDict(series)).ToPyList();

                    return new PyTuple(new[] { dataFrame, series });
                }
            }
            else
            {
                var dataFrame = frames.Select(x => (Deedle.Frame<(SecurityIdentifier, DateTime), string>)x).Merge();

                return (dataFrame, alpha.GetSeriesPlotOptions().ToArray());
            }


        }

        private ISet<IDataConsolidator> GetRegisteredConsolidators(Symbol symbol, TickType? tickType = null)
        {
            SubscriptionDataConfig subscription;
            try
            {
                // deterministic ordering is required here
                var subscriptions = SubscriptionManager.SubscriptionDataConfigService
                    .GetSubscriptionDataConfigs(symbol)
                    .OrderBy(x => x.TickType)
                    .ToList();

                // find our subscription
                subscription = subscriptions.FirstOrDefault(x => tickType == null || tickType == x.TickType);
                if (subscription == null)
                {
                    // if we can't locate the exact subscription by tick type just grab the first one we find
                    subscription = subscriptions.First();
                }
            }
            catch (InvalidOperationException)
            {
                // this will happen if we did not find the subscription, let's give the user a decent error message
                throw new Exception($"Please register to receive data for symbol \'{symbol}\' using the AddSecurity() function.");
            }

            return subscription.Consolidators;
        }

        private PyDict ConvertToPyDict(IndicatorSeries series)
        {
            using (Py.GIL())
            {
                PyDict dict = new(); 

                var csdict = series.GetType().GetProperties()
                .Where(p => p.CanRead)
                .ToDictionary(p => p.Name, p => p.GetValue(series));

                //convert to string
                csdict[nameof(series.Color)] = series.Color.IsEmpty ? string.Empty : ColorTranslator.ToHtml(series.Color);

                csdict.DoForEach(kvp => dict.SetItem(kvp.Key.ToLowerInvariant(), kvp.Value.ToPython()));

                return dict;
            }
        }

        private static decimal? ConvertToDecimal(object value)
        {
            if (value is decimal decimalValue)
                return decimalValue;

            string expression = Convert.ToString(value, CultureInfo.InvariantCulture);

            if (decimal.TryParse(expression
                          , NumberStyles.Any
                          , NumberFormatInfo.InvariantInfo
                          , out decimal number))
            {
                return number;
            }
            else if (bool.TryParse(expression, out bool result))
            {
                return result ? 1m : 0m;
            }
            else
            {
                //not supported
                return null;
            }
        }

        private static BaseData ConvertToData(Slice slice, Symbol symbol)
        {
            dynamic value;
            if (!slice.TryGetValue(symbol, out value))
            {
                return default;
            }

            var data = (BaseData)(value is System.Collections.IList list ? list[list.Count - 1] : value);

            return data;
        }
    }
}
