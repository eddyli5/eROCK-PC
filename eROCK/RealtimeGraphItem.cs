﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Righthand.RealtimeGraph;

namespace eROCK
{
    class RealtimeGraphItem : IGraphItem
    {
        public int Time { get; set; }
        public double Value { get; set; }
    }
}
