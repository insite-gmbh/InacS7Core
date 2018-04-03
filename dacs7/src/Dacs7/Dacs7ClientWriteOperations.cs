﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Dacs7
{
    public partial class Dacs7Client
    {

        /// <summary>
        /// Writes data from the plc.
        /// </summary>
        /// <param name="values">a list of tags with the following syntax Area.Offset,DataType[,length]</param>
        /// <returns>returns a enumerable with the write result, 0xFF = Success</returns>
        public Task<IEnumerable<byte>> WriteAsync(params KeyValuePair<string, object>[] values) => WriteAsync(values as IEnumerable<KeyValuePair<string, object>>);

        /// <summary>
        /// Writes data from the plc.
        /// </summary>
        /// <param name="values">a list of tags with the following syntax Area.Offset,DataType[,length]</param>
        /// <returns>returns a enumerable with the write result, 0xFF = Success</returns>
        public Task<IEnumerable<byte>> WriteAsync(IEnumerable<KeyValuePair<string, object>> values)
        {
            var items = CreateWriteNodeIdCollection(values);
            return _protocolHandler.WriteAsync(items);
        }


        /// <summary>
        /// This write opearation can be used for big data, because the partitioning is built-in.
        /// </summary>
        /// <param name="dbNumber">number of the datablock</param>
        /// <param name="offset">offset in bytes in the datablock</param>
        /// <param name="data">data to write</param>
        /// <returns>returns 0xFF if succeeded</returns>
        public Task<byte> WriteAsync(int dbNumber, int offset, Memory<byte> data)
        {
            return InternalWriteData($"db{dbNumber}", offset, data);
        }

        /// <summary>
        /// This write opearation can be used for big data, because the partitioning is built-in.
        /// </summary>
        /// <param name="area"><see cref="PlcArea"/> to write to</param>
        /// <param name="offset">offset in bytes in the datablock</param>
        /// <param name="data">data to write</param>
        /// <returns>returns 0xFF if succeeded</returns>
        public Task<byte> WriteAsync(PlcArea area, int offset, Memory<byte> data)
        {
            return InternalWriteData(FromArea(area), offset, data);
        }





        private async Task<byte> InternalWriteData(string area, int offset, Memory<byte> data)
        {
            Memory<byte> result = Memory<byte>.Empty;

            var bytesToWrite = data.Length;
            var processed = 0;
            while (bytesToWrite > 0)
            {
                var slice = Math.Min(_s7Context.WriteItemMaxLength, bytesToWrite);
                var erroCode = (await WriteAsync(new KeyValuePair<string, object>(CalculateByteArrayTag(area, offset + processed, slice), data.Slice(processed, slice)))).FirstOrDefault();

                if (erroCode != 0xFF)
                    return erroCode;

                processed += slice;
                bytesToWrite -= slice;
            }

            return 0xFF;
        }
    }
}
