﻿/*
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

using System.Threading;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using QuantConnect.Algorithm;
using Python.Runtime;
using QuantConnect.Data.Market;
using QuantConnect.Data;
using QuantConnect.Indicators;
using QuantConnect.Tests.Indicators;
using System;
using System.Linq;
using QuantConnect.Tests.Engine.DataFeeds;
using QuantConnect.Util;

namespace QuantConnect.Tests.Algorithm
{
    [TestFixture]
    public class AlgorithmPlottingTests
    {
        private Symbol _spy;
        private QCAlgorithm _algorithm;
        private IEnumerable<Type> _indicatorTestsTypes;

        [SetUp]
        public void Setup()
        {
            _algorithm = new QCAlgorithm();
            _algorithm.SubscriptionManager.SetDataManager(new DataManagerStub(_algorithm));
            _spy = _algorithm.AddEquity("SPY").Symbol;

            _indicatorTestsTypes =
                from type in GetType().Assembly.GetTypes()
                where type.IsPublic && !type.IsAbstract
                where
                   typeof(CommonIndicatorTests<TradeBar>).IsAssignableFrom(type) ||
                   typeof(CommonIndicatorTests<IBaseDataBar>).IsAssignableFrom(type) ||
                   typeof(CommonIndicatorTests<IndicatorDataPoint>).IsAssignableFrom(type)
                select type;
        }

        [Test]
        public void TestGetChartUpdatesWhileAdding()
        {
            var algorithm = new QCAlgorithm();

            var task1 = Task.Factory.StartNew(() =>
            {
                for (var i = 0; i < 1000; i++)
                {
                    algorithm.AddChart(new Chart($"Test_{i}"));
                    Thread.Sleep(1);
                }
            });

            var task2 = Task.Factory.StartNew(() =>
            {
                for (var i = 0; i < 1000; i++)
                {
                    algorithm.GetChartUpdates(true);
                    Thread.Sleep(1);
                }
            });

            Task.WaitAll(task1, task2);
        }

        [Test]
        public void PlotPythonIndicatorInSeparateChart()
        {
            PyObject indicator;

            foreach (var type in _indicatorTestsTypes)
            {
                var indicatorTest = Activator.CreateInstance(type);
                if (indicatorTest is CommonIndicatorTests<IndicatorDataPoint>)
                {
                    indicator = (indicatorTest as CommonIndicatorTests<IndicatorDataPoint>).GetIndicatorAsPyObject();
                }
                else if (indicatorTest is CommonIndicatorTests<IBaseDataBar>)
                {
                    indicator = (indicatorTest as CommonIndicatorTests<IBaseDataBar>).GetIndicatorAsPyObject();
                }
                else if (indicatorTest is CommonIndicatorTests<TradeBar>)
                {
                    indicator = (indicatorTest as CommonIndicatorTests<TradeBar>).GetIndicatorAsPyObject();
                }
                else
                {
                    throw new NotSupportedException($"RegistersIndicatorProperlyPython(): Unsupported indicator data type: {indicatorTest.GetType()}");
                }

                Assert.DoesNotThrow(() => _algorithm.Plot($"TestIndicatorPlot-{type.Name}", indicator));
                var charts = _algorithm.GetChartUpdates();
                Assert.IsTrue(charts.Select(x => x.Name == $"TestIndicatorPlot-{type.Name}").Any());
            }
        }

        [Test]
        public void PlotPythonCustomIndicatorProperly()
        {
            using (Py.GIL())
            {
                var module = PyModule.FromString(
                    Guid.NewGuid().ToString(),
                    @"
from AlgorithmImports import *
class PythonCustomIndicator(PythonIndicator):
    def __init__(self):
        self.Value = 0
    def Update(self, input):
        self.Value = input.Value
        return True"
                );

                dynamic customIndicator = module.GetAttr("PythonCustomIndicator").Invoke();
                customIndicator.Name = "custom";
                var input = new IndicatorDataPoint();
                input.Value = 10;
                customIndicator.Update(input);
                customIndicator.Current.Value = customIndicator.Value;
                Assert.DoesNotThrow(() => _algorithm.Plot("PlotTest", customIndicator));
                var charts = _algorithm.GetChartUpdates();
                Assert.IsTrue(charts.Where(x => x.Name == "PlotTest").Any());
                Assert.AreEqual(10, charts.First().Series["custom"].Values.First().y);
            }
        }

        [Test]
        public void PlotCustomIndicatorAsDefault()
        {
            using (Py.GIL())
            {
                var module = PyModule.FromString(
                    Guid.NewGuid().ToString(),
                    @"
from AlgorithmImports import *

class CustomIndicator:
    def __init__(self):
        self.Value = 10
    def Update(self, input):
        self.Value = input.Value
        return True"
                );

                var customIndicator = module.GetAttr("CustomIndicator").Invoke();
                Assert.DoesNotThrow(() => _algorithm.Plot("PlotTest", customIndicator));
                var charts = _algorithm.GetChartUpdates();
                Assert.IsFalse(charts.Where(x => x.Name == "PlotTest").Any());
                Assert.IsTrue(charts.Where(x => x.Name == "Strategy Equity").Any());
                Assert.AreEqual(10, charts.First().Series["PlotTest"].Values.First().y);
            }
        }
    }
}
