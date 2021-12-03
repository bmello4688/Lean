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
using QuantConnect.Data.Market;
using QuantConnect.Data.UniverseSelection;
using QuantConnect.Indicators;
using QuantConnect.Securities;
using QuantConnect.Util;

namespace QuantConnect.Research
{
    public partial class QuantBook
    {
        public dynamic RunAlpha<TAlphaData, TBaseData>(ChartableAlphaModel<TAlphaData, TBaseData> alpha, Symbol symbol, int period, Resolution? resolution = null, Func<BaseData, IBaseData> selector = null)
            where TAlphaData : ChartableAlphaData<TBaseData>
            where TBaseData : class, IBaseData
        {
            return RunAlpha(alpha, new[] { symbol }, period, resolution, selector);
        }

        public dynamic RunAlpha<TAlphaData, TBaseData>(ChartableAlphaModel<TAlphaData, TBaseData> alpha, Symbol[] symbols, int period, Resolution? resolution = null, Func<BaseData, IBaseData> selector = null)
            where TAlphaData : ChartableAlphaData<TBaseData>
            where TBaseData : class, IBaseData
        {
            var history = History(symbols, period, resolution);
            return RunAlpha(alpha, history, selector);
        }

        /// <summary>
        /// Gets the historical data of an bar indicator and convert it into pandas.DataFrame
        /// </summary>
        /// <param name="indicator">Bar indicator</param>
        /// <param name="history">Historical data used to calculate the indicator</param>
        /// <param name="selector">Selects a value from the BaseData to send into the indicator, if null defaults to the Value property of BaseData (x => x.Value)</param>
        /// <returns>pandas.DataFrame containing the historical data of <param name="indicator"></returns>
        private dynamic RunAlpha<TAlphaData, TBaseData>(ChartableAlphaModel<TAlphaData, TBaseData> alpha, IEnumerable<Slice> history, Func<BaseData, IBaseData> selector = null)
            where TAlphaData : ChartableAlphaData<TBaseData>
            where TBaseData : class, IBaseData
        {
            selector ??= (x => x);
            SetAlpha(alpha);

            Dictionary<Symbol, Dictionary<string, List<IndicatorDataPoint>>> symbolsToIndicatorNameToDataPoints = new();
            IReadOnlyList<Symbol> activeSymbols = null;
            history.DoForEach(slice =>
            {
                var newAddedSymbols = activeSymbols is null ? slice.Keys : slice.Keys.Where(s => !activeSymbols.Contains(s));
                var newRemovedSymbols = activeSymbols?.Where(s => !slice.Keys.Contains(s)) ?? Enumerable.Empty<Symbol>();

                activeSymbols = slice.Keys;

                alpha.OnSecuritiesChanged(this, new SecurityChanges(newAddedSymbols.Select(s => AddSecurity(s)), newRemovedSymbols.Select(s => Securities[s])));
                //newRemovedSymbols.DoForEach(s => RemoveSecurity(s.Symbol));

                var insightsBySymbol = alpha.Update(this, slice).ToDictionary(i => i.Symbol);

                //record
                slice.Keys.DoForEach(symbol =>
                {
                    if (!symbolsToIndicatorNameToDataPoints.ContainsKey(symbol))
                        symbolsToIndicatorNameToDataPoints.Add(symbol, new());

                    var alphaData = alpha[symbol];

                    var data = ConvertToData(slice, symbol);

                    alphaData?.IndicatorsToUpdate.DoForEach(indicator => indicator.Update(selector(data)));

                    var alphaDataProperties = alphaData.GetType().GetProperties()
                                    .Where(x => x.PropertyType.IsGenericType
                                    || x.PropertyType.IsValueType)
                                    .ToDictionary(x => x.Name, y => y.GetValue(alphaData));

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

                        var baseData = (BaseData)slice[symbol];

                        decimal insightDirection = (decimal)insight.Direction;

                        var name = nameof(Insight);
                        if (!symbolsToIndicatorNameToDataPoints[symbol].ContainsKey(name))
                        {
                            symbolsToIndicatorNameToDataPoints[symbol].Add(name, new());
                            symbolsToIndicatorNameToDataPoints[symbol].Add(nameof(baseData.Price), new());
                        }

                        symbolsToIndicatorNameToDataPoints[symbol][name].Add(new IndicatorDataPoint(slice.Time, insightDirection));
                        symbolsToIndicatorNameToDataPoints[symbol][nameof(baseData.Price)].Add(new IndicatorDataPoint(slice.Time, baseData.Value));
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

        private PyDict ConvertToPyDict(Series series)
        {
            using (Py.GIL())
            {
                PyDict dict = new(); 

                var csdict = series.GetType().GetFields()
                .Where(p => p.IsPublic)
                .ToDictionary(p => p.Name, p => p.GetValue(series));

                //convert to string
                csdict[nameof(series.Color)] = series.Color.IsEmpty ? string.Empty : ColorTranslator.ToHtml(series.Color);
                //don't use values
                csdict.Remove(nameof(series.Values));

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
