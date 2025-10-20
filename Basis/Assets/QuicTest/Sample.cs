using System.Net.Security;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Dummy.Quic;
using Microsoft.Quic;

using System;
using System.Collections.Generic;
using UnityEngine;

#nullable enable

namespace Dummy.Quic.Sample
{
    public class Sample
    {
        // static void Main(string[] args)
        // {
        //     // switch (args[0])
        //     // {
        //     //     case "client":
        //     //         await RunClient(args.Length >= 2 && args[1] == "insecure");
        //     //         break;
        //     //     case "server":
        //     //         break;
        //     // }
        //     // returning from main terminates the program, so let's wait for shutdown.
        //     RunClient("pi.kitl.ing"u8, insecure: false).Wait();
        // }

        static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(1);

        static readonly List<SslApplicationProtocol> Alpn = new List<SslApplicationProtocol> {new SslApplicationProtocol("sample") };

    sealed class SampleConnection : QuicConnection, IDisposable
        {
            // Allow msquic to hang onto reference to connection, as it drives itself and we don't need to hang onto it.
            public SampleConnection() : base(GCHandleType.Normal) { }

            internal readonly TaskCompletionSource<object?> shutdown = new TaskCompletionSource<object?>();

            public void Dispose() => _handle.Dispose();

            protected override int HandleConnectionEvent(ref QUIC_CONNECTION_EVENT connectionEvent)
            {
                switch (connectionEvent.Type)
                {
                    case QUIC_CONNECTION_EVENT_TYPE.CONNECTED:
                        Debug.Log($"[conn][{HandleInt:X11}] Connected");
                        base.HandleEventConnected(ref connectionEvent.CONNECTED);
                        ClientSend(this);
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
                        shutdown.SetResult(null);
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

        sealed class SampleStream : QuicStream, IDisposable
        {
            // allow msquic to keep this stream type alive, as we're not going to hold onto our own reference to it.
            public SampleStream(QuicConnection connection, QUIC_STREAM_OPEN_FLAGS flags) : base(connection, flags, GCHandleType.Normal)
            {
            }

            public void Dispose() => _handle.Dispose();

            protected override unsafe int HandleStreamEvent(ref QUIC_STREAM_EVENT streamEvent)
            {
                switch (streamEvent.Type)
                {
                    case QUIC_STREAM_EVENT_TYPE.SEND_COMPLETE:
                        Debug.Log($"[strm][{HandleInt:X11}] Data sent");
                        var send_handle = GCHandle.FromIntPtr((IntPtr)streamEvent.SEND_COMPLETE.ClientContext);
                        var buffers = (MsQuicBuffers?)send_handle.Target;
                        send_handle.Free();
                        buffers?.Dispose();
                        break;
                    case QUIC_STREAM_EVENT_TYPE.RECEIVE:
                        Debug.Log($"[strm][{HandleInt:X11}] Received {streamEvent.RECEIVE.TotalBufferLength} bytes");
                        break;
                    case QUIC_STREAM_EVENT_TYPE.PEER_SEND_ABORTED:
                        Debug.Log($"[strm][{HandleInt:X11}] Peer aborted, 0x{streamEvent.PEER_SEND_ABORTED.ErrorCode:x}");
                        break;
                    case QUIC_STREAM_EVENT_TYPE.PEER_SEND_SHUTDOWN:
                        Debug.Log($"[strm][{HandleInt:X11}] Peer shutdown");
                        break;
                    case QUIC_STREAM_EVENT_TYPE.SHUTDOWN_COMPLETE:
                        Debug.Log($"[strm][{HandleInt:X11}] All done");
                        if (streamEvent.SHUTDOWN_COMPLETE.AppCloseInProgress == 0)
                        {
                            _handle.Dispose();
                        }
                        break;
                    default:
                        break;
                }
                return MsQuic.QUIC_STATUS_SUCCESS;
            }

            public void Send()
            {
                MsQuicApi.Api.StreamStart(_handle, QUIC_STREAM_START_FLAGS.NONE);


            }
        }

        class ConnectionContext
        {
            internal readonly TaskCompletionSource<object?> shutdown = new TaskCompletionSource<object?>();
            internal MsQuicTlsSecret? tlsSecret;
        }

        public static Task RunClient(ReadOnlySpan<byte> hostname, bool insecure)
        {
            try
            {
                var settings = default(QUIC_SETTINGS);
                settings.IdleTimeoutMs = 1000;
                settings.IsSet.IdleTimeoutMs = 1;

                var flags = QUIC_CREDENTIAL_FLAGS.CLIENT;
                if (insecure)
                {
                    flags |= QUIC_CREDENTIAL_FLAGS.NO_CERTIFICATE_VALIDATION;
                }

                var config = MsQuicConfiguration.CreateInternal(settings, flags, null, null, Alpn, QUIC_ALLOWED_CIPHER_SUITE_FLAGS.NONE);

                var connection = new SampleConnection();

                connection.Start(config, MsQuic.QUIC_ADDRESS_FAMILY_UNSPEC, hostname, 4567);

                return connection.shutdown.Task;

                //await connection.shutdown.Task; // wait for shutdown
            } catch (Exception ex) {
                Debug.LogError("Caught exception in RunClient");
                Debug.LogError(ex);

                return null;
            }
        }

        private static unsafe void ClientSend(SampleConnection connection)
        {
            var stream = new SampleStream(connection, QUIC_STREAM_OPEN_FLAGS.NONE);

            // QUIC_HANDLE* stream_handle;
            int status;
            // if (MsQuic.StatusFailed(status = MsQuicApi.Api.StreamOpen(connection, QUIC_STREAM_OPEN_FLAGS.NONE, &ClientStreamCallback, null, &stream_handle)))
            // {
            //     Debug.Log($"StreamOpen failed, 0x{status:x}!");
            //     MsQuicApi.Api.ConnectionShutdown(connection, QUIC_CONNECTION_SHUTDOWN_FLAGS.NONE, 0);
            //     return;
            // }

            var stream_handle = stream.Handle;

            Debug.Log($"[strm][{(nint)stream_handle.QuicHandle:X11}] Starting...");

            if (MsQuic.StatusFailed(status = MsQuicApi.Api.StreamStart(stream_handle, QUIC_STREAM_START_FLAGS.NONE)))
            {
                Debug.Log($"StreamStart failed, 0x{status:x}!");
                stream.Dispose();
                connection.Shutdown(QUIC_CONNECTION_SHUTDOWN_FLAGS.NONE, 0);
                return;
            }

            var send_data = MemoryMarshal.AsBytes("hello".AsSpan()).ToArray().AsMemory();
            var buffers = new MsQuicBuffers();
            buffers.Initialize(send_data);
            var buffers_handle = GCHandle.Alloc(buffers);
            if (MsQuic.StatusFailed(status = MsQuicApi.Api.StreamSend(stream_handle, buffers.Buffers, (uint)buffers.Count, QUIC_SEND_FLAGS.FIN, (void*)GCHandle.ToIntPtr(buffers_handle))))
            {
                Debug.Log($"StreamSend failed, 0x{status:x}!");
                buffers_handle.Free();
                buffers.Dispose();
                connection.Shutdown(QUIC_CONNECTION_SHUTDOWN_FLAGS.NONE, 0);
                return;
            }
        }


        [UnmanagedCallersOnly(CallConvs = new Type[] { typeof(CallConvCdecl) })]
        private static unsafe int ClientStreamCallback(QUIC_HANDLE* stream, void* context, QUIC_STREAM_EVENT* streamEvent)
        {
            switch (streamEvent->Type)
            {
                case QUIC_STREAM_EVENT_TYPE.SEND_COMPLETE:
                    Debug.Log($"[strm][{(nint)stream:X11}] Data sent");
                    var send_handle = GCHandle.FromIntPtr((IntPtr)streamEvent->SEND_COMPLETE.ClientContext);
                    var buffers = (MsQuicBuffers?)send_handle.Target;
                    send_handle.Free();
                    buffers?.Dispose();
                    break;
                case QUIC_STREAM_EVENT_TYPE.RECEIVE:
                    Debug.Log($"[strm][{(nint)stream:X11}] Received {streamEvent->RECEIVE.TotalBufferLength} bytes");
                    break;
                case QUIC_STREAM_EVENT_TYPE.PEER_SEND_ABORTED:
                    Debug.Log($"[strm][{(nint)stream:X11}] Peer aborted, 0x{streamEvent->PEER_SEND_ABORTED.ErrorCode:x}");
                    break;
                case QUIC_STREAM_EVENT_TYPE.PEER_SEND_SHUTDOWN:
                    Debug.Log($"[strm][{(nint)stream:X11}] Peer shutdown");
                    break;
                case QUIC_STREAM_EVENT_TYPE.SHUTDOWN_COMPLETE:
                    Debug.Log($"[strm][{(nint)stream:X11}] All done");
                    if (streamEvent->SHUTDOWN_COMPLETE.AppCloseInProgress == 0)
                    {
                        MsQuicApi.Api.ApiTable->StreamClose(stream);
                    }
                    break;
                default:
                    break;
            }
            return MsQuic.QUIC_STATUS_SUCCESS;
        }
    }
}