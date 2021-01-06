using System;
using System.Collections.Generic;
using System.Linq;
using QuantConnect.Interfaces;
using QuantConnect.Securities.Future;

namespace QuantConnect.Securities.FutureOption
{
    public class FuturesOptionsUnderlyingMapper
    {
        private readonly IFutureChainProvider _chainProvider;
        private readonly Dictionary<string, Func<DateTime, DateTime?, DateTime?>> _underlyingFuturesOptionsRules;

        public FuturesOptionsUnderlyingMapper(IFutureChainProvider futureChainProvider)
        {
            _chainProvider = futureChainProvider;
            _underlyingFuturesOptionsRules = new Dictionary<string, Func<DateTime, DateTime?, DateTime?>>
            {
                { "ZB", (d, ld) => ContractMonthSerialLookupRule(Symbol.Create("ZB", SecurityType.Future, Market.CBOT), d, ld.Value) },
                { "ZC", (d, ld) => ContractMonthSerialLookupRule(Symbol.Create("ZC", SecurityType.Future, Market.CBOT), d, ld.Value) },
                { "ZS", (d, ld) => ContractMonthSerialLookupRule(Symbol.Create("ZS", SecurityType.Future, Market.CBOT), d, ld.Value) },
                { "ZT", (d, ld) => ContractMonthSerialLookupRule(Symbol.Create("ZT", SecurityType.Future, Market.CBOT), d, ld.Value) },
                { "ZW", (d, ld) => ContractMonthSerialLookupRule(Symbol.Create("ZW", SecurityType.Future, Market.CBOT), d, ld.Value) },

                { "HG", (d, _) => ContractMonthYearStartThreeMonthsThenEvenOddMonthsSkipRule(d, true) },
                { "SI", (d, _) => ContractMonthYearStartThreeMonthsThenEvenOddMonthsSkipRule(d, true) },
                { "GC", (d, _) => ContractMonthEvenOddMonth(d, false) }
            };
        }

        public Symbol GetUnderlyingFutureFromFutureOption(string futureOptionTicker, string market, DateTime futureOptionExpiration, DateTime? date = null)
        {
            var futureTicker = FuturesOptionsSymbolMappings.MapFromOption(futureOptionTicker);
            var canonicalFuture = Symbol.Create(futureTicker, SecurityType.Future, market);
            var contractMonth = FuturesOptionsExpiryFunctions.GetFutureContractMonth(canonicalFuture, futureOptionExpiration);

            if (_underlyingFuturesOptionsRules.ContainsKey(futureTicker))
            {
                var newFutureContractMonth = _underlyingFuturesOptionsRules[futureTicker](contractMonth, date);
                if (newFutureContractMonth == null)
                {
                    return null;
                }

                contractMonth = newFutureContractMonth.Value;
            }

            var futureExpiry = FuturesExpiryFunctions.FuturesExpiryFunction(canonicalFuture)(contractMonth);
            return Symbol.CreateFuture(futureTicker, market, futureExpiry);
        }

        protected DateTime? ContractMonthSerialLookupRule(Symbol canonicalSymbol, DateTime futureOptionContractMonth, DateTime lookupDate)
        {
            var futureChain = _chainProvider.GetFutureContractList(canonicalSymbol, lookupDate);

            foreach (var future in futureChain.OrderBy(s => s.ID.Date))
            {
                var futureContractMonth = future.ID.Date.Date
                    .AddMonths(FuturesExpiryUtilityFunctions.GetDeltaBetweenContractMonthAndContractExpiry(future.ID.Symbol, future.ID.Date))
                    .AddDays(-future.ID.Date.Day + 1);

                if (futureContractMonth < futureOptionContractMonth)
                {
                    continue;
                }

                return futureContractMonth;
            }

            return null;
        }

        protected DateTime ContractMonthEvenOddMonth(DateTime futureOptionContractMonth, bool oddMonths)
        {
            var monthEven = futureOptionContractMonth.Month % 2 == 0;
            if (oddMonths && monthEven)
            {
                return futureOptionContractMonth.AddMonths(1);
            }
            if (!oddMonths && !monthEven)
            {
                return futureOptionContractMonth.AddMonths(1);
            }

            return futureOptionContractMonth;
        }

        protected DateTime ContractMonthYearStartThreeMonthsThenEvenOddMonthsSkipRule(DateTime futureOptionContractMonth, bool oddMonths)
        {
            if (futureOptionContractMonth.Month <= 3)
            {
                return new DateTime(futureOptionContractMonth.Year, 3, 1);
            }

            return ContractMonthEvenOddMonth(futureOptionContractMonth, oddMonths);
        }
    }
}
