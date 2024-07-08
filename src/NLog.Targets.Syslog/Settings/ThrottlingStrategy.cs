// Licensed under the BSD license
// See the LICENSE file in the project root for more information

using System;

namespace NLog.Targets.Syslog.Settings
{
    /// <summary>The throttling strategy to be used</summary>
    public enum ThrottlingStrategy
    {
        /// <summary>No throttling strategy</summary>
        None,

        [Obsolete]
        DiscardOnFixedTimeout,

        [Obsolete]
        DiscardOnPercentageTimeout,

        /// <summary>Discard log entries</summary>
        Discard,

        [Obsolete]
        DeferForFixedTime,

        [Obsolete]
        DeferForPercentageTime,

        [Obsolete]
        Block
    }
}