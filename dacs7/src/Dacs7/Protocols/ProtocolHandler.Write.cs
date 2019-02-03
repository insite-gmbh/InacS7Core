﻿using Dacs7.Helper;
using Dacs7.Protocols.SiemensPlc;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Dacs7.Protocols
{

    internal class WriteResult
    {
        public Exception Exception { get; set; }

    }


    internal partial class ProtocolHandler
    {
        private ConcurrentDictionary<ushort, CallbackHandler<IEnumerable<S7DataItemWriteResult>>> _writeHandler = new ConcurrentDictionary<ushort, CallbackHandler<IEnumerable<S7DataItemWriteResult>>>();

        public async Task<IEnumerable<ItemResponseRetValue>> WriteAsync(IEnumerable<WriteItem> vars)
        {
            if (ConnectionState != ConnectionState.Opened)
                ExceptionThrowHelper.ThrowNotConnectedException();


            var result = vars.ToDictionary(x => x, x => ItemResponseRetValue.Success);
            foreach (var normalized in CreateWritePackages(_s7Context, vars))
            {
                if(!await WritePackage(result, normalized)) return new List<ItemResponseRetValue>();
            }
            return result.Values;
        }


        private async Task<bool> WritePackage(Dictionary<WriteItem, ItemResponseRetValue> result, WritePackage normalized)
        {
            var id = GetNextReferenceId();
            CallbackHandler<IEnumerable<S7DataItemWriteResult>> cbh;
            var sendData = _transport.Build(S7WriteJobDatagram.TranslateToMemory(S7WriteJobDatagram.BuildWrite(_s7Context, id, normalized.Items)));
            try
            {
                IEnumerable<S7DataItemWriteResult> writeResults = null;
                using (await SemaphoreGuard.Async(_concurrentJobs))
                {
                    cbh = new CallbackHandler<IEnumerable<S7DataItemWriteResult>>(id);
                    _writeHandler.TryAdd(cbh.Id, cbh);
                    try
                    {
                        if (await _transport.Client.SendAsync(sendData) != SocketError.Success)
                            return false;
                        writeResults = await cbh.Event.WaitAsync(_s7Context.Timeout);
                    }
                    finally
                    {
                        _writeHandler.TryRemove(cbh.Id, out _);
                    }
                }

                HandlerErrorResult(id, cbh, writeResults);

                BildResults(result, normalized, writeResults);
            }
            catch (TaskCanceledException)
            {
                ExceptionThrowHelper.ThrowTimeoutException();
            }
            return true;
        }

        private void HandlerErrorResult(ushort id, CallbackHandler<IEnumerable<S7DataItemWriteResult>> cbh, IEnumerable<S7DataItemWriteResult> writeResults)
        {
            if (writeResults == null)
            {
                if (_closeCalled)
                {
                    ExceptionThrowHelper.ThrowNotConnectedException(cbh.Exception);
                }
                else
                {
                    if (cbh.Exception != null)
                    {
                        ExceptionThrowHelper.ThrowException(cbh.Exception);
                    }
                    ExceptionThrowHelper.ThrowWriteTimeoutException(id);

                }
            }
        }

        private static void BildResults(Dictionary<WriteItem, ItemResponseRetValue> result, WritePackage normalized, IEnumerable<S7DataItemWriteResult> writeResults)
        {
            var items = normalized.Items.GetEnumerator();
            foreach (var item in writeResults)
            {
                if (items.MoveNext())
                {
                    if (items.Current.IsPart)
                    {
                        if (result.TryGetValue(items.Current.Parent, out var retCode) && retCode == ItemResponseRetValue.Success)
                        {
                            result[items.Current.Parent] = (ItemResponseRetValue)item.ReturnCode;
                        }
                    }
                    else
                    {
                        result[items.Current] = (ItemResponseRetValue)item.ReturnCode;
                    }
                }
            }
        }

        private Task ReceivedWriteJobAck(Memory<byte> buffer)
        {
            var data = S7WriteJobAckDatagram.TranslateFromMemory(buffer);

            if (_writeHandler.TryGetValue(data.Header.Header.ProtocolDataUnitReference, out var cbh))
            {
                if(data.Header.Error.ErrorClass != 0)
                {
                    _logger.LogError("Error while writing data for reference {0}. ErrorClass: {1}  ErrorCode:{2}", data.Header.Header.ProtocolDataUnitReference, data.Header.Error.ErrorClass, data.Header.Error.ErrorCode);
                    cbh.Exception = new Dacs7Exception(data.Header.Error.ErrorClass, data.Header.Error.ErrorCode);
                }
                if (data.Data == null)
                {
                    _logger.LogWarning("No data from write ack received for reference {0}", data.Header.Header.ProtocolDataUnitReference);
                }

                cbh.Event.Set(data.Data);
            }
            else
            {
                _logger.LogWarning("No write handler found for received write ack reference {0}", data.Header.Header.ProtocolDataUnitReference);
            }

            return Task.CompletedTask;
        }

        private IEnumerable<WritePackage> CreateWritePackages(SiemensPlcProtocolContext s7Context, IEnumerable<WriteItem> vars)
        {
            var result = new List<WritePackage>();
            foreach (var item in vars.ToList().OrderByDescending(x => x.NumberOfItems))
            {
                var currentPackage = result.FirstOrDefault(package => package.TryAdd(item));
                if (currentPackage == null)
                {
                    if (item.NumberOfItems > s7Context.WriteItemMaxLength)
                    {
                        ushort bytesToWrite = item.NumberOfItems;
                        ushort processed = 0;
                        while (bytesToWrite > 0)
                        {
                            var slice = Math.Min(_s7Context.WriteItemMaxLength, bytesToWrite);
                            var child = WriteItem.CreateChild(item, (ushort)(item.Offset + processed), slice);
                            if (slice < _s7Context.WriteItemMaxLength)
                            {
                                currentPackage = result.FirstOrDefault(package => package.TryAdd(child));
                            }

                            if (currentPackage == null)
                            {
                                currentPackage = new WritePackage(s7Context.PduSize);
                                if (currentPackage.TryAdd(child))
                                {
                                    if (currentPackage.Full)
                                    {
                                        yield return currentPackage.Return();
                                        if (currentPackage.Handled)
                                        {
                                            currentPackage = null;
                                        }
                                    }
                                    else
                                    {
                                        result.Add(currentPackage);
                                    }
                                }
                                else
                                {
                                    ExceptionThrowHelper.ThrowCouldNotAddPackageException(nameof(WritePackage));
                                }
                            }
                            processed += slice;
                            bytesToWrite -= slice;
                        }
                    }
                    else
                    {
                        currentPackage = new WritePackage(s7Context.PduSize);
                        result.Add(currentPackage);
                        if (!currentPackage.TryAdd(item))
                        {
                            ExceptionThrowHelper.ThrowCouldNotAddPackageException(nameof(WritePackage));
                        }
                    }
                }

                if (currentPackage != null)
                {
                    if (currentPackage.Full)
                    {
                        yield return currentPackage.Return();
                    }

                    if (currentPackage.Handled)
                    {
                        result.Remove(currentPackage);
                    }
                }
            }
            foreach (var package in result)
            {
                yield return package.Return();
            }
        }

    }
}