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

using System.Collections.Generic;

namespace QuantConnect.Securities.Positions
{
    /// <summary>
    /// Resolves position groups from a collection of positions.
    /// </summary>
    public interface IPositionGroupResolver
    {
        /// <summary>
        /// Resolves the position groups that exist within the specified collection of positions.
        /// </summary>
        /// <param name="positions">The collection of positions</param>
        /// <returns>An enumerable of position groups</returns>
        IEnumerable<IPositionGroup> Resolve(PositionCollection positions);
    }
}
