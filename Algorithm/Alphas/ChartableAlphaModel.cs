using QuantConnect.Data;
using QuantConnect.Data.UniverseSelection;
using QuantConnect.Indicators;
using QuantConnect.Logging;
using QuantConnect.Securities;
using QuantConnect.Util;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace QuantConnect.Algorithm.Framework.Alphas
{
    /// <summary>
    /// Base Alpha Model for creating insights
    /// </summary>
    public abstract class ChartableAlphaModel<TSymbolData, TBaseData> : AlphaModel
        where TSymbolData : ChartableAlphaData<TBaseData>
        where TBaseData : class, IBaseData
    {
        private readonly List<Series> seriesChartOptions;
        private readonly Resolution _insightResolution;
        private readonly Dictionary<Symbol, TSymbolData> _symbolDataBySymbol = new();

        public TSymbolData this[Symbol symbol] => 
            _symbolDataBySymbol.ContainsKey(symbol) ? 
            _symbolDataBySymbol[symbol] : default;

        public ChartableAlphaModel(Resolution insightResolution, params Series[]seriesOptions)
        {
            _insightResolution = insightResolution;
            Name = $"{GetType().Name}({_insightResolution})";

            seriesChartOptions = new();
            if(seriesOptions is not null && seriesOptions.Length > 0)
                seriesChartOptions.AddRange(seriesOptions);
            seriesChartOptions.Add(new($"{InsightDirection.Up}_{nameof(Insight)}", SeriesType.Scatter, "$", Color.Green, ScatterMarkerSymbol.Triangle));
            seriesChartOptions.Add(new($"{InsightDirection.Down}_{nameof(Insight)}", SeriesType.Scatter, "$", Color.Red, ScatterMarkerSymbol.TriangleDown));
            seriesChartOptions.Add(new($"{InsightDirection.Flat}_{nameof(Insight)}", SeriesType.Scatter, "$", Color.Blue, ScatterMarkerSymbol.Square));
        }

        /// <summary>
        /// Updates this alpha model with the latest data from the algorithm.
        /// This is called each time the algorithm receives data for subscribed securities
        /// </summary>
        /// <param name="algorithm">The algorithm instance</param>
        /// <param name="data">The new data available</param>
        /// <returns>The new insights generated</returns>
        public override IEnumerable<Insight> Update(QCAlgorithm algorithm, Slice data)
        {
            var insights = new List<Insight>();
            foreach (var symbolData in _symbolDataBySymbol.Values)
            {
                if (symbolData.AreAllIndicatorsReady)
                {
                    try
                    {
                        int predictionBarCount = symbolData.GetPredictionBarCount();
                        if (predictionBarCount > 0)
                        {
                            Insight insight = GetInsight(symbolData, _insightResolution, predictionBarCount);
                            if (insight != null)
                                insights.Add(insight);
                        }
                        else
                        {
                            Log.Error($"{Name} has a prediction bar count of {predictionBarCount}. Needs to be greater than 0.");
                        }
                    }
                    catch(Exception ex)
                    {
                        Log.Error($"GetInsight Error: \n\t{ex}");
                    }
                }

                symbolData.Update();
            }

            return insights;
        }

        /// <summary>
        /// Event fired each time the we add/remove securities from the data feed
        /// </summary>
        /// <param name="algorithm">The algorithm instance that experienced the change in securities</param>
        /// <param name="changes">The security additions and removals from the algorithm</param>
        public override void OnSecuritiesChanged(QCAlgorithm algorithm, SecurityChanges changes)
        {
            foreach (var added in changes.AddedSecurities)
            {
                if (!_symbolDataBySymbol.TryGetValue(added.Symbol, out TSymbolData symbolData))
                {
                    _symbolDataBySymbol[added.Symbol] = CreateSymbolDataEntry(added);
                    _symbolDataBySymbol[added.Symbol].RegisterIndicators(algorithm, _insightResolution);
                }
                else
                {
                    // a security that was already initialized was re-added, reset the indicators
                    symbolData.ResetIndicators();
                }
            }
        }

        public IEnumerable<Series> GetSeriesPlotOptions()
        {
            return seriesChartOptions;
        }

        protected abstract TSymbolData CreateSymbolDataEntry(Security added);

        protected abstract Insight GetInsight(TSymbolData symbolData, Resolution insightResolution, int predictionBarCount);
    }

    public abstract class ChartableAlphaData<TBaseData>
        where TBaseData : class, IBaseData
    {
        private readonly List<IndicatorBase<TBaseData>> _indicatorsToRegisterForUpdates = new();

        public Security Security { get; private set; }
        public Symbol Symbol => Security.Symbol;

        public IEnumerable<IndicatorBase<TBaseData>> IndicatorsToUpdate => _indicatorsToRegisterForUpdates;

        internal bool AreAllIndicatorsReady => IndicatorsToUpdate?.All(i => i.IsReady) ?? false;

        public Func<int> GetPredictionBarCount { get; private set; }

        public ChartableAlphaData(Security added, Func<int> getPredictionBarCount, params IndicatorBase<TBaseData>[] indicators)
        {
            Security = added;
            _indicatorsToRegisterForUpdates.AddRange(indicators);
            GetPredictionBarCount = getPredictionBarCount ?? throw new ArgumentNullException(nameof(getPredictionBarCount));
        }

        internal void RegisterIndicators(QCAlgorithm algorithm, Resolution indicatorsResolution)
        {
            IndicatorsToUpdate.DoForEach(indicator =>
            {
                algorithm.RegisterIndicatorForUpdates(indicator, Symbol, indicatorsResolution);
            });
        }

        internal void ResetIndicators()
        {
            IndicatorsToUpdate.DoForEach(i => i.Reset());
        }

        internal void Update()
        {
            OnUpdate();
        }

        protected virtual void OnUpdate()
        {

        }
    }
}
