using QuantConnect.Algorithm.Framework.Alphas;
using QuantConnect.Util;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace QuantConnect
{
    public class IndicatorSeries
    {
        /// <summary>
        /// Name of the Series:
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Should be plotted on price chart
        /// </summary>
        public bool IsPriceRelated { get; }

        /// <summary>
        /// Index/position of the series on the chart.
        /// </summary>
        public int Index { get; set; }

        /// <summary>
        /// Y-Axis Title for the chart series.
        /// </summary>
        public string NonPriceChartYAxisTitle { get; } = "Unit";

        /// <summary>
        /// Chart type for the series:
        /// </summary>
        public SeriesType SeriesType { get; }

        /// <summary>
        /// Color the series
        /// </summary>
        [JsonConverter(typeof(ColorJsonConverter))]
        public Color Color { get; }

        /// <summary>
        /// Shape or symbol for the marker in a scatter plot
        /// </summary>
        public ScatterMarkerSymbol ScatterMarkerSymbol { get; set; }

        public bool IsACategory { get; }

        public decimal CategoryValue { get; }

        public IndicatorSeries(string name, SeriesType type, bool isPriceRelated, Color color, ScatterMarkerSymbol symbol = ScatterMarkerSymbol.None)
        {
            Name = name;
            SeriesType = type;
            IsPriceRelated = isPriceRelated;
            Index = 0;
            Color = color;
            ScatterMarkerSymbol = symbol;
        }

        public IndicatorSeries(Enum category, string name, SeriesType type, Color color, ScatterMarkerSymbol symbol = ScatterMarkerSymbol.None) 
            : this($"{category.ToStringInvariant()}_{name}", type, true, color, symbol)
        {
            IsACategory = true;
            CategoryValue = Enum.Parse(category.GetType(), category.ToStringInvariant()).ConvertInvariant<decimal>();
        }
    }
}
