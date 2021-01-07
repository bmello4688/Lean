/*
 * QUANTCONNECT.COM - Democratizing Finance, Empowering Individuals.
 * Lean Algorithmic Trading Engine v2.0. Copyright 2014 QuantConnect Corporation.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
*/

using System;
using NUnit.Framework;
using QuantConnect.Interfaces;
using QuantConnect.Securities;
using QuantConnect.Securities.Positions;

namespace QuantConnect.Tests.Common.Securities
{
    /// <summary>
    /// Provides an implementation of <see cref="IBuyingPowerModel"/> that verifies consistency with
    /// the <see cref="SecurityPositionGroupBuyingPowerModel"/>
    /// </summary>
    public class BuyingPowerModelComparator : IBuyingPowerModel
    {
        public SecurityPortfolioManager Portfolio { get; }
        public IBuyingPowerModel SecurityModel { get; }
        public IPositionGroupBuyingPowerModel PositionGroupModel { get; }

        public BuyingPowerModelComparator(
            IBuyingPowerModel securityModel,
            IPositionGroupBuyingPowerModel positionGroupModel,
            SecurityPortfolioManager portfolio = null,
            ITimeKeeper timeKeeper = null
            )
        {
            Portfolio = portfolio;
            SecurityModel = securityModel;
            PositionGroupModel = positionGroupModel;

            if (portfolio == null)
            {
                var securities = new SecurityManager(timeKeeper ?? new TimeKeeper(DateTime.UtcNow));
                Portfolio = new SecurityPortfolioManager(securities, new SecurityTransactionManager(null, securities));
            }
        }

        public decimal GetLeverage(Security security)
        {
            return SecurityModel.GetLeverage(security);
        }

        public void SetLeverage(Security security, decimal leverage)
        {
            SecurityModel.SetLeverage(security, leverage);
        }

        public MaintenanceMargin GetMaintenanceMargin(MaintenanceMarginParameters parameters)
        {
            EnsureSecurityExists(parameters.Security);
            var expected = SecurityModel.GetMaintenanceMargin(parameters);
            var actual = PositionGroupModel.GetMaintenanceMargin(new PositionGroupMaintenanceMarginParameters(
                Portfolio, new PositionGroup(PositionGroupModel, new Position(parameters.Security)), true
            ));

            Assert.AreEqual(expected.Value, actual.Value,
                $"{PositionGroupModel.GetType().Name}:{nameof(GetMaintenanceMargin)}"
            );

            return expected;
        }

        public InitialMargin GetInitialMarginRequirement(InitialMarginParameters parameters)
        {
            EnsureSecurityExists(parameters.Security);
            var expected = SecurityModel.GetInitialMarginRequirement(parameters);
            var actual = PositionGroupModel.GetInitialMarginRequirement(new PositionGroupInitialMarginParameters(
                Portfolio, new PositionGroup(PositionGroupModel, new Position(parameters.Security))
            ));

            Assert.AreEqual(expected.Value, actual.Value,
                $"{PositionGroupModel.GetType().Name}:{nameof(GetInitialMarginRequirement)}"
            );

            return expected;
        }

        public InitialMargin GetInitialMarginRequiredForOrder(InitialMarginRequiredForOrderParameters parameters)
        {
            EnsureSecurityExists(parameters.Security);
            var expected = SecurityModel.GetInitialMarginRequiredForOrder(parameters);
            var actual = PositionGroupModel.GetInitialMarginRequiredForOrder(new PositionGroupInitialMarginForOrderParameters(
                Portfolio, new PositionGroup(PositionGroupModel, new Position(parameters.Security)), parameters.Order
            ));

            Assert.AreEqual(expected.Value, actual.Value,
                $"{PositionGroupModel.GetType().Name}:{nameof(GetInitialMarginRequiredForOrder)}"
            );

            return expected;
        }

        public HasSufficientBuyingPowerForOrderResult HasSufficientBuyingPowerForOrder(
            HasSufficientBuyingPowerForOrderParameters parameters
            )
        {
            EnsureSecurityExists(parameters.Security);
            return SecurityModel.HasSufficientBuyingPowerForOrder(parameters);
        }

        public GetMaximumOrderQuantityResult GetMaximumOrderQuantityForTargetBuyingPower(
            GetMaximumOrderQuantityForTargetBuyingPowerParameters parameters
            )
        {
            EnsureSecurityExists(parameters.Security);
            return SecurityModel.GetMaximumOrderQuantityForTargetBuyingPower(parameters);
        }

        public GetMaximumOrderQuantityResult GetMaximumOrderQuantityForDeltaBuyingPower(
            GetMaximumOrderQuantityForDeltaBuyingPowerParameters parameters
            )
        {
            EnsureSecurityExists(parameters.Security);
            return SecurityModel.GetMaximumOrderQuantityForDeltaBuyingPower(parameters);
        }

        public ReservedBuyingPowerForPosition GetReservedBuyingPowerForPosition(ReservedBuyingPowerForPositionParameters parameters)
        {
            EnsureSecurityExists(parameters.Security);
            return SecurityModel.GetReservedBuyingPowerForPosition(parameters);
        }

        public BuyingPower GetBuyingPower(BuyingPowerParameters parameters)
        {
            EnsureSecurityExists(parameters.Security);
            return SecurityModel.GetBuyingPower(parameters);
        }

        private void EnsureSecurityExists(Security security)
        {
            if (!Portfolio.Securities.ContainsKey(security.Symbol))
            {
                Portfolio.Securities[security.Symbol] = security;
            }
        }
    }
}
