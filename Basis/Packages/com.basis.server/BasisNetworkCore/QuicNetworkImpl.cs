using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Security;
using System.Runtime.InteropServices;
using Dummy.Quic;
using Microsoft.Quic;
using UnityEngine;

#nullable enable

namespace Basis.Network.Core
{
    public class QuicNetPeer : QuicConnection, NetPeer
    {
        internal readonly MsQuicBuffers pendingConnectionData = new();
        internal readonly QuicNetManager manager;
        internal QuicNetPeer(QuicNetManager manager, NetDataWriter connectionData) : base() {
            this.manager = manager;
            // TODO: consider making a NetDataWriter.AsMemory() method?
            pendingConnectionData.Initialize(connectionData.Data.AsMemory(0, connectionData.Length));
        }

        public int Id => throw new System.NotImplementedException();

        public IPAddress Address => throw new System.NotImplementedException();

        public int RemoteId => throw new System.NotImplementedException();

        public int RoundTripTime => throw new System.NotImplementedException();

        public float TimeSinceLastPacket => throw new System.NotImplementedException();

        public long RemoteTimeDelta => throw new System.NotImplementedException();

        public void Disconnect()
        {
            throw new System.NotImplementedException();
        }

        public void Disconnect(byte[] b)
        {
            throw new System.NotImplementedException();
        }

        public void DisconnectForce()
        {
            throw new System.NotImplementedException();
        }

        public int GetPacketsCountInQueue(byte channel, DeliveryMethod deliveryMethod)
        {
            throw new System.NotImplementedException();
        }

        public void Send(byte[] data, byte channelNumber, DeliveryMethod deliveryMethod)
        {
            throw new System.NotImplementedException();
        }

        public void Send(NetDataWriter data, byte channelNumber, DeliveryMethod deliveryMethod)
        {
            throw new System.NotImplementedException();
        }

        private sealed class ConnectionDataClientStream : QuicStream
        {
            readonly QuicNetPeer peer;
            internal unsafe ConnectionDataClientStream(QuicNetPeer peer): base(peer, QUIC_STREAM_OPEN_FLAGS.NONE, GCHandleType.Normal) {
                this.peer = peer;
                var buffers = peer.pendingConnectionData;
                MsQuicApi.Api.StreamSend(_handle, buffers.Buffers, (uint)buffers.Count, QUIC_SEND_FLAGS.START | QUIC_SEND_FLAGS.FIN, null);
            }
            protected override int HandleStreamEvent(ref QUIC_STREAM_EVENT streamEvent)
            {
                switch (streamEvent.Type)
                {
                    case QUIC_STREAM_EVENT_TYPE.SEND_COMPLETE:
                        peer.pendingConnectionData.Dispose();
                        // Debug.Log($"[peer][{peer.HandleInt}] Initial Connection Data sent");
                        break;
                    case QUIC_STREAM_EVENT_TYPE.RECEIVE:
                        // TODO: store disconnect reason somewhere.
                        if ((streamEvent.RECEIVE.Flags & QUIC_RECEIVE_FLAGS.FIN) != QUIC_RECEIVE_FLAGS.NONE) {
                            // disconnect reason received
                        }
                        break;
                }

                return MsQuic.QUIC_STATUS_SUCCESS;
            }
        }

        private void SendConnectionData() {
            
        }

        protected override int HandleConnectionEvent(ref QUIC_CONNECTION_EVENT connectionEvent)
        {
            switch (connectionEvent.Type)
            {
                case QUIC_CONNECTION_EVENT_TYPE.CONNECTED:
                    Debug.Log($"[conn][{HandleInt:X11}] Connected");
                    base.HandleEventConnected(ref connectionEvent.CONNECTED);
                    SendConnectionData();
                    break;
                case QUIC_CONNECTION_EVENT_TYPE.SHUTDOWN_INITIATED_BY_TRANSPORT:
                    int status = connectionEvent.SHUTDOWN_INITIATED_BY_TRANSPORT.Status;
                    if (status == MsQuic.QUIC_STATUS_CONNECTION_IDLE)
                    {
                        Debug.Log($"[conn][{HandleInt:X11}] Successfully shutdown on idle");
                    }
                    else
                    {
                        Debug.Log($"[conn][{HandleInt:X11}] Shutdown by transport, 0x{status:x}");
                    }
                    break;
                case QUIC_CONNECTION_EVENT_TYPE.SHUTDOWN_INITIATED_BY_PEER:
                    ulong error = connectionEvent.SHUTDOWN_INITIATED_BY_PEER.ErrorCode;
                    Debug.Log($"[conn][{HandleInt:X11}] Shutdown by peer, 0x{error:x}");
                    break;
                case QUIC_CONNECTION_EVENT_TYPE.SHUTDOWN_COMPLETE:
                    Debug.Log($"[conn][{HandleInt:X11}] All done.");
                    base.HandleEventShutdownComplete(ref connectionEvent.SHUTDOWN_COMPLETE);
                    //shutdown.SetResult(null);
                    break;
                case QUIC_CONNECTION_EVENT_TYPE.PEER_CERTIFICATE_RECEIVED:
                    base.HandleEventPeerCertificateReceived(ref connectionEvent.PEER_CERTIFICATE_RECEIVED);
                    break;
                default:
                    break;
            }
            return MsQuic.QUIC_STATUS_SUCCESS;
        }
    }

    public class QuicNetManager : NetManager
    {
        public NetStatistics Statistics => throw new System.NotImplementedException();

        public int ConnectedPeersCount => throw new System.NotImplementedException();

        private readonly MsQuicConfigurationSafeHandle clientConfig = CreateClientConfig();

        public NetPeer Connect(string hostname, int port, NetDataWriter connectionData)
        {
            var peer = new QuicNetPeer(connectionData);
            peer.Start(clientConfig, MsQuic.QUIC_ADDRESS_FAMILY_UNSPEC, null, (ushort)port);
            // TODO: keep internal ref to peer?

            return peer;
        }

        static readonly List<SslApplicationProtocol> Alpn = new() { new("BasisVR") };

        public void Start(IPAddress IPv4Address, IPAddress IPv6Address, int SetPort)
        {
            // TODO: listen? split into a separate listen step?
        }

        public void Stop()
        {
            throw new System.NotImplementedException();
        }

        private static MsQuicConfigurationSafeHandle CreateClientConfig() {
            var settings = default(QUIC_SETTINGS);
            settings.IdleTimeoutMs = 1000;
            settings.IsSet.IdleTimeoutMs = 1;

            settings.CongestionControlAlgorithm = (ushort)QUIC_CONGESTION_CONTROL_ALGORITHM.BBR;
            settings.IsSet.CongestionControlAlgorithm = 1;

            var credentialFlags = QUIC_CREDENTIAL_FLAGS.CLIENT;

            #if UNITY_ANDROID || BASIS_QUIC_MANUAL_CERTIFICATE_VALIDATION
                // NOTE: msquic doesn't have support for android's certificate store/validation,
                //  so we need to do it ourselves on Android, at minimum.
                flags |= QUIC_CREDENTIAL_FLAGS.NO_CERTIFICATE_VALIDATION;
                flags |= QUIC_CREDENTIAL_FLAGS.INDICATE_CERTIFICATE_RECEIVED;
            #endif

            return MsQuicConfiguration.CreateInternal(settings, credentialFlags, null, null, Alpn, QUIC_ALLOWED_CIPHER_SUITE_FLAGS.NONE);
        }
    }
}