﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Dacs7
{
    public partial class Dacs7Client
    {

        /// <summary>
        /// Reads data from the plc.
        /// </summary>
        /// <param name="values">a list of tags with the following syntax Area.Offset,DataType[,length]</param>
        /// <returns>returns a enumerable with the read values</returns>
        public Task<IEnumerable<object>> ReadAsync(params string[] values) => ReadAsync(values as IEnumerable<string>);


        /// <summary>
        /// Reads data from the plc.
        /// </summary>
        /// <param name="values">a list of tags with the following syntax Area.Offset,DataType[,length]</param>
        /// <returns>returns a enumerable with the read values</returns>
        public async Task<IEnumerable<object>> ReadAsync(IEnumerable<string> values)
        {
            var items = CreateNodeIdCollection(values);
            var result = await _protocolHandler.ReadAsync(items);
            var enumerator = items.GetEnumerator();
            return result.Select(value =>
            {
                enumerator.MoveNext();
                return ConvertMemoryToData(enumerator.Current, value.Data);
            }).ToList();
        }

        /// <summary>
        /// This read opearation can be used for big data, because the partitioning is built-in.
        /// </summary>
        /// <param name="dbNumber">number of the datablock</param>
        /// <param name="offset">offset in bytes in the datablock</param>
        /// <param name="length">the length to read</param>
        /// <returns>returns the data if read succeeded</returns>
        public Task<Memory<byte>> ReadAsync(int dbNumber, int offset, int length)
        {
            return InternalReadData($"db{dbNumber}", offset, length);
        }

        /// <summary>
        /// This read opearation can be used for big data, because the partitioning is built-in.
        /// </summary>
        /// <param name="area">the <see cref="PlcArea"/> to read from (DB is not supported)</param>
        /// <param name="offset">offset in bytes in the datablock</param>
        /// <param name="length">the length to read</param>
        /// <returns>returns the data if read succeeded</returns>
        public Task<Memory<byte>> ReadAsync(PlcArea area, int offset, int length)
        {
            return InternalReadData(FromArea(area), offset, length);
        }





        private async Task<Memory<byte>> InternalReadData(string area, int offset, int length)
        {
            Memory<byte> result = Memory<byte>.Empty;

            var bytesToRead = length;
            var processed = 0;
            while (bytesToRead > 0)
            {
                var slice = Math.Min(_s7Context.ReadItemMaxLength, bytesToRead);
                if (bytesToRead == length && slice != length)
                {
                    result = new byte[length];
                }

                Memory<byte> partResult = (Memory<byte>)(await ReadAsync(CalculateByteArrayTag(area, offset + processed, slice))).FirstOrDefault();

                if (result.IsEmpty)
                {
                    result = partResult;
                }
                else
                {
                    partResult.CopyTo(result.Slice(processed));
                }

                processed += slice;
                bytesToRead -= slice;
            }

            return result;
        }

    }
}
