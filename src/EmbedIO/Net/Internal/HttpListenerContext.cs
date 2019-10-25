﻿using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using EmbedIO.Internal;
using EmbedIO.Routing;
using EmbedIO.Sessions;
using EmbedIO.Utilities;
using EmbedIO.WebSockets;
using EmbedIO.WebSockets.Internal;
using Swan.Logging;

namespace EmbedIO.Net.Internal
{
    // Provides access to the request and response objects used by the HttpListener class.
    internal sealed class HttpListenerContext : IHttpContextImpl
    {
        private readonly Lazy<IDictionary<object, object>> _items =
            new Lazy<IDictionary<object, object>>(() => new Dictionary<object, object>(), true);

        private readonly TimeKeeper _ageKeeper = new TimeKeeper();

        private readonly Stack<Action<IHttpContext>> _closeCallbacks = new Stack<Action<IHttpContext>>();

        private bool _closed;

        internal HttpListenerContext(HttpConnection cnc)
        {
            Connection = cnc;
            Request = new HttpListenerRequest(this);
            Response = new HttpListenerResponse(this);
            User = null;
            Id = UniqueIdGenerator.GetNext();
            LocalEndPoint = Request.LocalEndPoint;
            RemoteEndPoint = Request.RemoteEndPoint;
        }

        public string Id { get; }

        public CancellationToken CancellationToken { get; set; }

        public long Age => _ageKeeper.ElapsedTime;

        public IPEndPoint LocalEndPoint { get; }

        public IPEndPoint RemoteEndPoint { get; }

        public IHttpRequest Request { get; }

        public RouteMatch Route { get; set; }

        public string? RequestedPath => Route.SubPath;

        public IHttpResponse Response { get; }

        public IPrincipal? User { get; }

        public ISessionProxy Session { get; set; }

        public bool SupportCompressedRequests { get; set; }

        public IDictionary<object, object> Items => _items.Value;

        public bool IsHandled { get; private set; }

        public MimeTypeProviderStack MimeTypeProviders { get; } = new MimeTypeProviderStack();

        internal HttpListenerRequest HttpListenerRequest => Request as HttpListenerRequest;

        internal HttpListenerResponse HttpListenerResponse => Response as HttpListenerResponse;

        internal HttpListener Listener { get; set; }

        internal string? ErrorMessage { get; set; }

        internal bool HaveError => ErrorMessage != null;

        internal HttpConnection Connection { get; }

        public void SetHandled() => IsHandled = true;

        public void OnClose(Action<IHttpContext> callback)
        {
            if (_closed)
                throw new InvalidOperationException("HTTP context has already been closed.");

            _closeCallbacks.Push(Validate.NotNull(nameof(callback), callback));
        }

        public void Close()
        {
            _closed = true;

            // Always close the response stream no matter what.
            Response.Close();

            foreach (var callback in _closeCallbacks)
            {
                try
                {
                    callback(this);
                }
                catch (Exception e)
                {
                    e.Log("HTTP context", $"[{Id}] Exception thrown by a HTTP context close callback.");
                }
            }
        }

        public async Task<IWebSocketContext> AcceptWebSocketAsync(
            IEnumerable<string> requestedProtocols,
            string? acceptedProtocol,
            int receiveBufferSize,
            TimeSpan keepAliveInterval,
            CancellationToken cancellationToken)
        {
            var webSocket = await WebSocket.AcceptAsync(this, acceptedProtocol).ConfigureAwait(false);
            return new WebSocketContext(this, WebSocket.SupportedVersion, requestedProtocols, acceptedProtocol, webSocket, cancellationToken);
        }

        public string GetMimeType(string extension)
            => MimeTypeProviders.GetMimeType(extension);

        public bool TryDetermineCompression(string mimeType, out bool preferCompression)
            => MimeTypeProviders.TryDetermineCompression(mimeType, out preferCompression);
    }
}