﻿using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Diagnostics.Windows.Configs;
using BenchmarkDotNet.Engines;
using Dacs7.ReadWrite;
using System.Threading.Tasks;

namespace Dacs7.Benchmarks
{
    [SimpleJob(RunStrategy.Monitoring, launchCount: 0, warmupCount: 0, targetCount: 100)]
    [RankColumn]
    [MemoryDiagnoser]
    [InliningDiagnoser]
    public class ReadBenchmark
    {

        private Dacs7Client _client;
        private ReadItem _item;

        [Params("192.168.1.60:102,0,1")]
        public string Address;

        [Params("DB250.0,b,100", "DB250.0,b,1000", "DB250.1000,x1", "DB250.1100,w,10")]
        public string Tag { get; set; }


        [GlobalSetup]
        public async Task Setup()
        {
            _client = new Dacs7Client(Address, PlcConnectionType.Pg, 5000)
            {
                MaxAmQCalled = 5,
                MaxAmQCalling = 5
            };
            await _client.ConnectAsync();
            _item = ReadItem.CreateFromTag(Tag);
        }

        [GlobalCleanup]
        public void GlobalCleanup()
        {
            _client?.DisconnectAsync();
        }



        [Benchmark]
        public async Task ReadAsync()
        {
            var results = await _client.ReadAsync(_item);
        }

    }
}
