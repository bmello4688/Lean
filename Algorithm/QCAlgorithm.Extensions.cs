using QuantConnect;
using QuantConnect.Algorithm;
using QuantConnect.Algorithm.Framework.Alphas;
using QuantConnect.Data;
using QuantConnect.Data.Market;
using QuantConnect.Indicators;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QuantConnect.Algorithm
{
    public static class QCAlgorithmExtensions
    {
        public static IndicatorBase<TBaseData> RegisterIndicatorForUpdates<TBaseData>(this QCAlgorithm algorithm, IndicatorBase<TBaseData> indicator, Symbol symbol, Resolution? resolution = null, Func<IBaseData, decimal> selector = null)
            where TBaseData : class, IBaseData
        {
            var name = algorithm.CreateIndicatorName(symbol, indicator.Name, resolution);

            var wrappedIndicator = new RegisteredIndicator<TBaseData>(name, indicator);

            algorithm.RegisterIndicator(symbol, wrappedIndicator, resolution, s => s as TBaseData);

            if (algorithm.EnableAutomaticIndicatorWarmUp)
            {
                algorithm.WarmUpIndicator(symbol, wrappedIndicator, resolution);
            }

            return wrappedIndicator;
        }

        private class RegisteredIndicator<T> : IndicatorBase<T>
            where T: IBaseData
        {
            private IndicatorBase<T> _indicator;

            public RegisteredIndicator(string name, IndicatorBase<T> indicator)
                :base(name)
            {
                _indicator = indicator;
            }

            public override bool IsReady => _indicator.IsReady;

            protected override decimal ComputeNextValue(T input)
            {
                _indicator.Update(input);

                return _indicator.Current.Value;
            }
        }
    }
}
