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
 *
*/

using NUnit.Framework;
using QuantConnect.Data;
using QuantConnect.Data.Market;
using QuantConnect.Lean.Engine.DataFeeds;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using QuantConnect.Lean.Engine.DataFeeds.Enumerators;

namespace QuantConnect.Tests.Engine.DataFeeds
{
    [TestFixture, Parallelizable(ParallelScope.Fixtures)]
    public class BaseDataExchangeTests
    {
        // This is a default timeout for all tests to wait if something went wrong
        const int DefaultTimeout = 30000;
        private CancellationTokenSource _cancellationTokenSource;

        [SetUp]
        public void SetUp()
        {
            _cancellationTokenSource = new CancellationTokenSource();
        }

        [TearDown]
        public void TearDown()
        {
            _cancellationTokenSource.Cancel();
        }

        [Test]
        public void EndsQueueConsumption()
        {
            var enqueable = new EnqueueableEnumerator<BaseData>();
            var exchange = new BaseDataExchange("test");

            var cancellationToken = new CancellationTokenSource();
            Task.Run(() =>
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    Thread.Sleep(10);
                    enqueable.Enqueue(new Tick {Symbol = Symbols.SPY, Time = DateTime.UtcNow});
                }
            });

            BaseData last = null;
            var lastUpdated = new AutoResetEvent(false);
            exchange.AddEnumerator(Symbols.SPY, enqueable, handleData: spy =>
            {
                last = spy;
                lastUpdated.Set();
            });

            var finishedRunning = new AutoResetEvent(false);
            Task.Run(() => { exchange.Start(cancellationToken.Token); finishedRunning.Set(); } );

            Assert.IsTrue(lastUpdated.WaitOne(DefaultTimeout));

            cancellationToken.Cancel();

            Assert.IsTrue(finishedRunning.WaitOne(DefaultTimeout));

            var endTime = DateTime.UtcNow;
            Assert.IsNotNull(last);
            Assert.IsTrue(last.Time <= endTime);
            enqueable.Dispose();
        }

        [Test]
        public void DefaultErrorHandlerDoesNotStopQueueConsumption()
        {
            var enqueable = new EnqueueableEnumerator<BaseData>();
            var exchange = new BaseDataExchange("test");

            var cancellationToken = new CancellationTokenSource();
            Task.Run(() =>
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    Thread.Sleep(10);
                    enqueable.Enqueue(new Tick { Symbol = Symbols.SPY, Time = DateTime.UtcNow });
                }
            });

            var first = true;
            BaseData last = null;
            var lastUpdated = new AutoResetEvent(false);
            exchange.AddEnumerator(Symbols.SPY, enqueable, handleData: spy =>
            {
                if (first)
                {
                    first = false;
                    throw new Exception("This exception should be swalloed by the exchange!");
                }
                last = spy;
                lastUpdated.Set();
            });

            Task.Run(() => exchange.Start(cancellationToken.Token));

            Assert.IsTrue(lastUpdated.WaitOne(DefaultTimeout));

            cancellationToken.Cancel();
            enqueable.Dispose();
        }

        [Test]
        public void SetErrorHandlerExitsOnTrueReturn()
        {
            var enqueable = new EnqueueableEnumerator<BaseData>();
            var exchange = new BaseDataExchange("test");

            var cancellationToken = new CancellationTokenSource();
            Task.Factory.StartNew(() =>
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    Thread.Sleep(10);
                    enqueable.Enqueue(new Tick { Symbol = Symbols.SPY, Time = DateTime.UtcNow });
                }
            });

            var first = true;
            var errorCaught = new AutoResetEvent(true);
            BaseData last = null;

            exchange.SetErrorHandler(error =>
            {
                errorCaught.Set();
                return true;
            });

            exchange.AddEnumerator(Symbols.SPY, enqueable, handleData: spy =>
            {
                if (first)
                {
                    first = false;
                    throw new Exception();
                }
                last = spy;
            });

            Task.Run(() => exchange.Start(cancellationToken.Token));

            Assert.IsTrue(errorCaught.WaitOne(DefaultTimeout));

            Assert.IsNull(last);

            enqueable.Dispose();
            cancellationToken.Cancel();
        }

        [Test]
        public void RespectsShouldMoveNext()
        {
            var exchange = new BaseDataExchange("test");
            exchange.SetErrorHandler(exception => true);
            exchange.AddEnumerator(Symbol.Empty, new List<BaseData> {new Tick()}.GetEnumerator(), () => false);

            var isFaultedEvent = new ManualResetEvent(false);
            var isCompletedEvent = new ManualResetEvent(false);
            Task.Run(() => exchange.Start(new CancellationTokenSource(50).Token)).ContinueWith(task =>
            {
                if (task.IsFaulted) isFaultedEvent.Set();
                isCompletedEvent.Set();
            });

            isCompletedEvent.WaitOne();
            Assert.IsFalse(isFaultedEvent.WaitOne(0));
        }

        [Test]
        public void FiresOnEnumeratorFinishedEvents()
        {
            var exchange = new BaseDataExchange("test");
            IEnumerator<BaseData> enumerator = new List<BaseData>().GetEnumerator();

            var isCompletedEvent = new ManualResetEvent(false);
            exchange.AddEnumerator(Symbol.Empty, enumerator, () => true, handler => isCompletedEvent.Set());
            Task.Run(() => exchange.Start(new CancellationTokenSource(50).Token));

            isCompletedEvent.WaitOne();
        }

        [Test]
        public void RemovesBySymbol()
        {
            var exchange = new BaseDataExchange("test");
            var enumerator = new List<BaseData> {new Tick {Symbol = Symbols.SPY}}.GetEnumerator();
            exchange.AddEnumerator(Symbols.SPY, enumerator);
            var removed = exchange.RemoveEnumerator(Symbols.AAPL);
            Assert.IsNull(removed);
            removed = exchange.RemoveEnumerator(Symbols.SPY);
            Assert.AreEqual(Symbols.SPY, removed.Symbol);
        }
    }
}
