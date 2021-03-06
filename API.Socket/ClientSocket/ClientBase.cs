﻿using API.Socket.Exception;
using API.Socket.InternalStructure;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace API.Socket.ClientSocket
{
    public abstract class ClientBase
    {
        private StateObject _stateObject;
        private IPEndPoint _remoteEP;
        private SocketAsyncEventArgs _ioEvent;
        private string ip;
        private int port;
        private readonly object _closePeerObj;
        #region abstract
        protected abstract void OnDisconnected();
        protected abstract void OnConnected(StateObject state);
        protected abstract void OnRecieved(StateObject state);
        #endregion abstract
        protected ClientBase()
        {
            _closePeerObj = new object();
            _stateObject = new StateObject();
            _ioEvent = new SocketAsyncEventArgs();

            _ioEvent.Completed += new EventHandler<SocketAsyncEventArgs>(Receive_Completed);
            _ioEvent.SetBuffer(new byte[StateObject.BufferSize], 0, StateObject.BufferSize);
            _ioEvent.UserToken = _stateObject;
        }
        public void Close()
        {
            ClosePeer();
            _stateObject.Dispose();
        }
        public void Connect(string ip, int port, int timeout = 5000)
        {
            try
            {
                if (IsConnect())
                    return;
                this.ip = ip;
                this.port = port;
                _remoteEP = new IPEndPoint(IPAddress.Parse(ip), port);
                if (_stateObject.Socket == null)
                {
                    System.Net.Sockets.Socket handler = new System.Net.Sockets.Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    IAsyncResult asyncResult = handler.BeginConnect(_remoteEP, null, null);
                    if (asyncResult.AsyncWaitHandle.WaitOne(timeout, true))
                    {
                        handler.EndConnect(asyncResult);
                        _stateObject.Socket = handler;
                        var option = new TcpKeepAlive
                        {
                            OnOff = 1,
                            KeepAliveTime = 5000,
                            KeepAliveInterval = 1000
                        };
                        _stateObject.Socket.IOControl(IOControlCode.KeepAliveValues, option.GetBytes(), null);
                        BeginReceive(_ioEvent);
                        OnConnected(_stateObject);
                    }
                    else
                    {
                        _stateObject.Init();
                        throw new SocketException(10060);
                    }
                }
                else
                {
                    _stateObject.Init();
                }
            }
            catch (ArgumentNullException arg)
            {
                throw new Exception.Exception(arg.Message);
            }
            catch (SocketException se)
            {
                throw new Exception.Exception(se.Message);
            }
            catch (System.Exception e)
            {
                throw new Exception.Exception(e.Message);
            }
        }
        private void BeginReceive(SocketAsyncEventArgs e)
        {
            var state = e.UserToken as StateObject;
            if (state.Socket != null)
            {
                bool pending = state.Socket.ReceiveAsync(_ioEvent);
                if (!pending)
                    ProcessReceive(e);
            }
        }
        private void ProcessReceive(SocketAsyncEventArgs e)
        {
            var state = e.UserToken as StateObject;
            try
            {
                if (e.BytesTransferred > 0 && e.SocketError == SocketError.Success)
                {
                    state.ReceiveBuffer.Push(e.Buffer.Take(e.BytesTransferred).ToArray());
                    OnRecieved(state);
                }
                else
                {
                    ClosePeer();
                    return;
                }
            }
            catch (System.Exception)
            {
                state.ReceiveBuffer.Clear();
            }
            BeginReceive(e);
        }
        private void Receive_Completed(object sender, SocketAsyncEventArgs e)
        {
            if (e.LastOperation == SocketAsyncOperation.Receive)
                ProcessReceive(e);
        }
        protected void ClosePeer()
        {
            try
            {
                if (Monitor.TryEnter(_closePeerObj))
                {
                    try
                    {
                        if (_ioEvent != null && _stateObject.Socket != null)
                        {
                            _ioEvent.SocketError = SocketError.Shutdown;
                            _stateObject.Init();
                            OnDisconnected();
                        }
                    }
                    finally
                    {
                        Monitor.Exit(_closePeerObj);
                    }
                }
            }
            catch (Exception.Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
        }
        public bool IsConnect()
        {
            if (_stateObject == null) return false;
            if (_stateObject.Socket == null) return false;
            return !(_stateObject.Socket.Poll(1000, SelectMode.SelectRead) && _stateObject.Socket.Available == 0);
        }
        public void Send(IPacket packet)
        {
            try
            {
                if (_stateObject.Socket == null)
                    throw new Exception.Exception(ErrorCode.SocketDisConnect, "");
                if (packet == null)
                    throw new ArgumentNullException("packet");
                _stateObject.Send(packet);
            }
            catch (Exception.Exception ex)
            {
                Console.WriteLine(ex.Message);
                ClosePeer();
            }
            catch (System.Exception ex)
            {
                Console.WriteLine(ex.Message);
                ClosePeer();
            }
        }
    }
}
