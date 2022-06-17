using Python.Runtime;
using QuantConnect.Data;
using QuantConnect.Data.Market;
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
    /// Provides an implementation of <see cref="IChartableAlphaModel"/> that combines multiple alpha
    /// models into a single alpha model and properly sets each insights 'SourceModel' property.
    /// </summary>
    public class ConditionalCompositeChartableAlphaModel : CompositeChartableAlphaModel
    {
        public ConditionalCompositeChartableAlphaModel(params IChartableAlphaModel[] alphaModels)
            : base(alphaModels)
        {
        }

        protected override IEnumerable<List<Insight>> FilterInsights(Dictionary<Symbol, List<Insight>> symbolToInsights, int alphaCount)
        {
            return symbolToInsights.Values
                .Where(insightsForSymbol => insightsForSymbol.Count == alphaCount &&
                        insightsForSymbol.Skip(1)
                        .All(insight => insight.Direction == insightsForSymbol.First().Direction));
        }
    }

    /// <summary>
    /// Provides an implementation of <see cref="IChartableAlphaModel"/> that combines multiple alpha
    /// models into a single alpha model and properly sets each insights 'SourceModel' property.
    /// </summary>
    public class CompositeChartableAlphaModel : AlphaModel, IChartableAlphaModel
    {
        private readonly List<IChartableAlphaModel> _alphaModels = new();
        private Dictionary<Symbol, CompositeAlphaData> _symbolsToAlphas = new();

        public ChartableAlphaData this[Symbol symbol]
        {
            get
            {
                if (_symbolsToAlphas.TryGetValue(symbol, out var storedAlpha))
                {
                    return storedAlpha;
                }
                else
                {
                    return null;
                }
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CompositeAlphaModel"/> class
        /// </summary>
        /// <param name="alphaModels">The individual alpha models defining this composite model</param>
        public CompositeChartableAlphaModel(params IChartableAlphaModel[] alphaModels)
        {
            if (alphaModels.IsNullOrEmpty())
            {
                throw new ArgumentException("Must specify at least 1 alpha model for the CompositeAlphaModel");
            }

            _alphaModels.AddRange(alphaModels);
        }

        /// <summary>
        /// Updates this alpha model with the latest data from the algorithm.
        /// This is called each time the algorithm receives data for subscribed securities.
        /// This method patches this call through the each of the wrapped models.
        /// </summary>
        /// <param name="algorithm">The algorithm instance</param>
        /// <param name="data">The new data available</param>
        /// <returns>The new insights generated</returns>
        public override IEnumerable<Insight> Update(QCAlgorithm algorithm, Slice data)
        {
            Dictionary<Symbol, List<Insight>> symbolToInsights = new();

            _alphaModels.DoForEach(model =>
            {
                var name = model.GetModelName();

                model.Update(algorithm, data).DoForEach(insight =>
                {
                    if (string.IsNullOrEmpty(insight.SourceModel))
                    {
                        // set the source model name if not already set
                        insight.SourceModel = name;
                    }

                    if (insight != null)
                    {
                        if (!symbolToInsights.ContainsKey(insight.Symbol))
                            symbolToInsights.Add(insight.Symbol, new());

                        symbolToInsights[insight.Symbol].Add(insight);
                    }
                });
            });

            var alphaCount = _alphaModels.Count;

            return FilterInsights(symbolToInsights, alphaCount)
                .Select(i => Combine(i));
        }

        protected Insight Combine(List<Insight> insights)
        {
            var firstInsight = insights.FirstOrDefault();
            if (insights.Count <= 1)
                return firstInsight;
            else
            {
                var direction = InsightDirection.Flat;

                int directionCounter = insights.Sum(insight => (int)insight.Direction);

                if (directionCounter > 0)
                    direction = InsightDirection.Up;
                else if (directionCounter < 0)
                    direction = InsightDirection.Down;

                return new Insight(firstInsight.Symbol,
                    firstInsight.Period,
                    firstInsight.Type,
                    direction,
                    firstInsight.Magnitude,
                    firstInsight.Confidence,
                    firstInsight.SourceModel,
                    firstInsight.Weight);
            }
        }

        protected virtual IEnumerable<List<Insight>> FilterInsights(Dictionary<Symbol, List<Insight>> symbolToInsights, int alphaCount)
        {
            //no filter
            return symbolToInsights.Values;
        }

        /// <summary>
        /// Event fired each time the we add/remove securities from the data feed.
        /// This method patches this call through the each of the wrapped models.
        /// </summary>
        /// <param name="algorithm">The algorithm instance that experienced the change in securities</param>
        /// <param name="changes">The security additions and removals from the algorithm</param>
        public override void OnSecuritiesChanged(QCAlgorithm algorithm, SecurityChanges changes)
        {
            foreach (var model in _alphaModels)
            {
                model.OnSecuritiesChanged(algorithm, changes);
            }

            changes.AddedSecurities.DoForEach(security =>
           {
               var alphas = _alphaModels.Select(alpha => alpha[security.Symbol])
                                    .Where(a => a is not null);

               if (alphas is not null)
               {
                   CompositeAlphaData storedAlpha = new(alphas);
                   _symbolsToAlphas.Add(security.Symbol, storedAlpha);
               }
           });

            changes.RemovedSecurities.DoForEach(security =>
            {
                _symbolsToAlphas.Remove(security.Symbol);
            });
        }

        /// <summary>
        /// Adds a new <see cref="AlphaModel"/>
        /// </summary>
        /// <param name="alphaModel">The alpha model to add</param>
        public void AddAlpha(IChartableAlphaModel alphaModel)
        {
            _alphaModels.Add(alphaModel);
        }

        public IEnumerable<IndicatorSeries> GetSeriesPlotOptions()
        {
            return _alphaModels.SelectMany(a => a.GetSeriesPlotOptions())
                    .DistinctBy(s => s.Name);
        }

        private class CompositeAlphaData : ChartableAlphaData
        {
            private IEnumerable<ChartableAlphaData> _alphas;

            public CompositeAlphaData(IEnumerable<ChartableAlphaData> alphas)
                : base(alphas.First().Security)
            {
                _alphas = alphas;
                _alphas.DoForEach(alpha =>
               _indicatorsToRegisterForUpdates.AddRange(alpha.IndicatorsToUpdate));
            }

            protected override void InitializeIndicators()
            {
            }

            public override int GetPredictionBarCount()
            {
                return _alphas.Min(alpha => alpha.GetPredictionBarCount());
            }

            public override Dictionary<string, object> GetChartableIndicators()
            {
                return _alphas.SelectMany(alpha => alpha.GetChartableIndicators())
                    .DistinctBy(x => x.Key)
                    .ToDictionary();
            }
        }
    }
    public interface IChartableAlphaModel : IAlphaModel
    {
        public ChartableAlphaData this[Symbol symbol] { get; }

        /// <summary>
        /// Gets the series plot information
        /// </summary>
        /// <returns></returns>
        IEnumerable<IndicatorSeries> GetSeriesPlotOptions();
    }

    /// <summary>
    /// Base Alpha Model for creating insights
    /// </summary>
    public abstract class ChartableAlphaModel<TAlphaData> : AlphaModel, IChartableAlphaModel
        where TAlphaData : ChartableAlphaData
    {
        private readonly List<IndicatorSeries> seriesChartOptions;
        private readonly Resolution _insightResolution;
        private readonly Dictionary<Symbol, TAlphaData> _symbolDataBySymbol = new();

        public ChartableAlphaData this[Symbol symbol] =>
            _symbolDataBySymbol.ContainsKey(symbol) ?
            _symbolDataBySymbol[symbol] : default;

        public ChartableAlphaModel(Resolution insightResolution, params IndicatorSeries[] seriesOptions)
        {
            _insightResolution = insightResolution;
            Name = $"{GetType().Name}({_insightResolution})";

            seriesChartOptions = new();
            if (seriesOptions is not null && seriesOptions.Length > 0)
                seriesChartOptions.AddRange(seriesOptions);
            seriesChartOptions.Add(new(InsightDirection.Up, nameof(Insight), SeriesType.Scatter, Color.Green, ScatterMarkerSymbol.Triangle));
            seriesChartOptions.Add(new(InsightDirection.Down, nameof(Insight), SeriesType.Scatter, Color.Red, ScatterMarkerSymbol.TriangleDown));
            seriesChartOptions.Add(new(InsightDirection.Flat, nameof(Insight), SeriesType.Scatter, Color.Blue, ScatterMarkerSymbol.Square));
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
                        TimeSpan predictionTimespan = _insightResolution.ToTimeSpan() * predictionBarCount;
                        if (predictionBarCount > 0)
                        {
                            Insight insight = GetInsight(symbolData, predictionTimespan);
                            if (insight != null)
                                insights.Add(insight);
                        }
                        else
                        {
                            Log.Error($"{Name} has a prediction bar count of {predictionBarCount}. Needs to be greater than 0.");
                        }
                    }
                    catch (Exception ex)
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
                if (added.IsTradable)
                {
                    if (!_symbolDataBySymbol.TryGetValue(added.Symbol, out TAlphaData symbolData))
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
        }

        public IEnumerable<IndicatorSeries> GetSeriesPlotOptions()
        {
            return seriesChartOptions;
        }

        protected abstract TAlphaData CreateSymbolDataEntry(Security added);

        protected abstract Insight GetInsight(TAlphaData symbolData, TimeSpan predictionTimespan);
    }

    public abstract class ChartableAlphaData
    {
        protected readonly List<IndicatorBase> _indicatorsToRegisterForUpdates = new();
        private Dictionary<string, object> _alphaProperties;

        public Security Security { get; private set; }
        public Symbol Symbol => Security.Symbol;

        public IEnumerable<IndicatorBase> IndicatorsToUpdate => _indicatorsToRegisterForUpdates;

        internal bool AreAllIndicatorsReady => IndicatorsToUpdate?.All(i => i.IsReady) ?? false;

        public IndicatorBase<IndicatorDataPoint> Price { get; private set; }

        public ChartableAlphaData(Security added)
        {
            Security = added;

            bool isReady = false;
            Price = new FunctionalIndicator<IndicatorDataPoint>(nameof(Price),
                b =>
                {
                    isReady = true;
                    return b.Value;
                },
                b => isReady);

            SetupIndicators();

        }

        public virtual Dictionary<string, object> GetChartableIndicators()
        {
            return GetType().GetProperties()
                   .Where(x => x.PropertyType.IsAssignableTo(typeof(IndicatorBase))
                   || x.PropertyType.IsValueType)
                   .ToDictionary(x => x.Name, y => y.GetValue(this));
        }

        private void SetupIndicators()
        {
            InitializeIndicators();

            var reflectedIndicators = GetType().GetProperties()
                                    .Where(x => x.PropertyType.IsAssignableTo(typeof(IndicatorBase)))
                                    .Select(y => y.GetValue(this) as IndicatorBase)
                                    .Where(z => z is not null).ToArray();

            _indicatorsToRegisterForUpdates.AddRange(reflectedIndicators);

            if(_indicatorsToRegisterForUpdates.Count < 2)
            {
                throw new NotSupportedException("No Indicators registered. Initialize your indicators in InitializeIndicators().");
            }
        }

        internal void RegisterIndicators(QCAlgorithm algorithm, Resolution indicatorsResolution)
        {
            IndicatorsToUpdate.DoForEach(indicator =>
            {
                //match consolidators
                if (indicator is IndicatorBase<IndicatorDataPoint> derivedIndicator)
                {
                    algorithm.RegisterIndicatorForUpdates(derivedIndicator, Symbol, indicatorsResolution);
                }
                else if (indicator is IndicatorBase<IBaseDataBar> derivedIndicator1)
                {
                    algorithm.RegisterIndicatorForUpdates(derivedIndicator1, Symbol, indicatorsResolution);
                }
                else if (indicator is IndicatorBase<TradeBar> derivedIndicator2)
                {
                    algorithm.RegisterIndicatorForUpdates(derivedIndicator2, Symbol, indicatorsResolution);
                }
                else if (indicator is IndicatorBase<QuoteBar> derivedIndicator3)
                {
                    algorithm.RegisterIndicatorForUpdates(derivedIndicator3, Symbol, indicatorsResolution);
                }
                else if (indicator is IndicatorBase<Tick> derivedIndicator4)
                {
                    algorithm.RegisterIndicatorForUpdates(derivedIndicator4, Symbol, indicatorsResolution);
                }
                else if (indicator is IndicatorBase<DynamicData> derivedIndicator5)
                {
                    algorithm.RegisterIndicatorForUpdates(derivedIndicator5, Symbol, indicatorsResolution);
                }
                else if (indicator is IndicatorBase<BaseData> derivedIndicator6)
                {
                    algorithm.RegisterIndicatorForUpdates(derivedIndicator6, Symbol, indicatorsResolution);
                }
                else if (indicator is IndicatorBase<OpenInterest> derivedIndicator7)
                {
                    algorithm.RegisterIndicatorForUpdates(derivedIndicator7, Symbol, indicatorsResolution);
                }

            });
        }

        internal void ResetIndicators()
        {
            IndicatorsToUpdate.DoForEach(i => i.Reset());
        }

        internal void Update()
        {
            if (Price.IsReady)
            {
                OnUpdate();
            }
        }

        public virtual int GetPredictionBarCount()
        {
            return 1;
        }

        protected virtual void InitializeIndicators()
        {
            throw new NotImplementedException("Need to override InitializeIndicators in derived class.");
        }

        protected virtual void OnUpdate()
        {

        }
    }
}
