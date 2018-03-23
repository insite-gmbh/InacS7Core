﻿using Dacs7.Heper;
using System;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Dacs7.Communication
{
    /// <summary>
    /// </summary>
    public abstract class SocketBase
    {
        public delegate Task OnConnectionStateChangedHandler(string socketHandle, bool connected);
        public delegate Task<int> OnDataReceivedHandler(string socketHandle, Memory<byte> aBuffer);
        public delegate Task OnSocketShutdownHandler(string socketHandle);

        #region Fields
        protected ISocketConfiguration _configuration;
        private bool _shutdown;
        protected string _identity;
        #endregion

        #region Properties
        public bool IsConnected { get; protected set; }
        public bool Shutdown => _shutdown;
        public int ReceiveBufferSize { get { return _configuration.ReceiveBufferSize; } }


        public OnConnectionStateChangedHandler OnConnectionStateChanged;
        public OnDataReceivedHandler OnRawDataReceived;
        public OnSocketShutdownHandler OnSocketShutdown;

        public abstract string Identity { get; }
        #endregion

        public SocketBase(ISocketConfiguration configuration)
        {
            _configuration = configuration;
        }

        public abstract Task OpenAsync();

        public virtual Task CloseAsync()
        {
            _shutdown = true;
            return Task.CompletedTask;
        }

        public abstract Task<SocketError> SendAsync(Memory<byte> data);

        protected async Task HandleSocketDown()
        {
            await PublishConnectionStateChanged(false);
        }

        protected async Task PublishSocketShutdown(string identity = null)
        {
            await OnSocketShutdown?.Invoke(identity ?? Identity);
        }

        protected async Task PublishConnectionStateChanged(bool state, string identity = null)
        {
            if (IsConnected != state)
            {
                IsConnected = state;
                await OnConnectionStateChanged?.Invoke(identity ?? Identity, IsConnected);
            }
        }

        /// <summary>
        /// Process the incomimgn data
        /// </summary>
        /// <param name="receivedData"></param>
        /// <param name="identity"></param>
        /// <returns>the processed number of bytes</returns>
        protected async Task<int> ProcessData(Memory<byte> receivedData, string identity = null)
        {
            return await OnRawDataReceived?.Invoke(identity ?? Identity, receivedData);
        }
    }
}
