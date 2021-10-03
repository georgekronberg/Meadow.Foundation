﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Meadow.Foundation.Web.Maple.Server.Routing;

namespace Meadow.Foundation.Web.Maple.Server
{
    /// <summary>
    /// A lightweight web server.
    /// </summary>
    public partial class MapleServer
    {
        public const int MAPLE_SERVER_BROADCASTPORT = 17756;
        public const int DefaultPort = 5417;

        private RequestMethodCache MethodCache { get; }

        private Dictionary<Type, IRequestHandler> _handlerCache = new Dictionary<Type, IRequestHandler>();
        private readonly HttpListener _httpListener = new HttpListener();

        public ILogger Logger { get; }
        public IPAddress IPAddress { get; private set; }
        public int Port { get; private set; }

        /// <summary>
        /// Whether or not the server is listening for requests.
        /// </summary>
        public bool Running { get; protected set; } = false;

        /// <summary>
        /// Whether the server should operate on requests serially or in parallel.
        /// </summary>
        public RequestProcessMode ThreadingMode { get; protected set; }

        /// <summary>
        /// Whether or not the server should advertise it's name
        /// and IP via UDP for discovery.
        /// </summary>
        public bool Advertise { get; protected set; } = false;

        /// <summary>
        /// The interval, in milliseconds of how often to advertise.
        /// </summary>
        public int AdvertiseIntervalMs { get; set; } = 2000;

        /// <summary>
        /// The name of the device to advertise via UDP.
        /// </summary>
        public string DeviceName { get; set; } = "Meadow";

        public MapleServer(
            string ipAddress,
            int port = DefaultPort,
            bool advertise = false,
            RequestProcessMode processMode = RequestProcessMode.Serial,
            ILogger logger = null)
            : this(IPAddress.Parse(ipAddress), port, advertise, processMode, logger)
        {
        }

        /// <summary>
        /// Creates a new MapleServer that listens on the specified IP Address
        /// and Port.
        /// </summary>
        /// <param name="ipAddress"></param>
        /// <param name="port">Defaults to 5417.</param>
        /// <param name="advertise">Whether or not to advertise via UDP.</param>
        /// <param name="processMode">Whether or not the server should respond to
        /// requests in parallel or serial. For Meadow, only Serial works
        /// reliably today.</param>
        public MapleServer(
            IPAddress ipAddress,
            int port = DefaultPort,
            bool advertise = false,
            RequestProcessMode processMode = RequestProcessMode.Serial,
            ILogger logger = null)
        {
            Logger =  logger ?? new ConsoleLogger();
            MethodCache = new RequestMethodCache(Logger);

            Create(ipAddress, port, advertise, processMode);
        }

        private void Create(IPAddress ipAddress,
            int port,
            bool advertise,
            RequestProcessMode processMode)
        {
            IPAddress = ipAddress ?? throw new ArgumentNullException(nameof(ipAddress));
            Port = port;

            Advertise = advertise;
            ThreadingMode = processMode;

            if (IPAddress.Equals(IPAddress.Any))
            {
                // because .NET is apparently too stupid to understand "bind to all"
                foreach (var ni in NetworkInterface
                    .GetAllNetworkInterfaces()
                    .SelectMany(i => i.GetIPProperties().UnicastAddresses))
                {
                    if (ni.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        // for now, just use IPv4
                        Console.WriteLine($"Listening on http://{ni.Address}:{port}/");

                        _httpListener.Prefixes.Add($"http://{ni.Address}:{port}/");
                    }
                }
            }
            else
            {
                Console.WriteLine($"Listening on http://{IPAddress}:{port}/");

                _httpListener.Prefixes.Add($"http://{IPAddress}:{port}/");
            }

            LoadRequestHandlers();

            Initialize();
        }

        protected void Initialize()
        {

        }

        /// <summary>
        /// Starts listening to requests, and optionally advertises on UDP.
        /// </summary>
        public async void Start()
        {
            try
            {
                _httpListener.Start();
            }
            catch (HttpListenerException)
            {
                if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                {
                    throw new Exception(
                        $"The server application needs elevated privileges or you must open permission on the URL (e.g. `netsh http add urlacl url=http://{IPAddress}:{Port}/ user=DOMAIN\\user`)");
                }

                throw;
            }

            if (Advertise)
            {
                StartUdpAdvertisement();
            }
            await StartListeningToIncomingRequests();
            _httpListener.Close();
        }

        /// <summary>
        /// Stops listening to requests and advertising (if running).
        /// </summary>
        public void Stop()
        {
            Running = false;
        }

        //public void AddHandler(IRequestHandler handler)
        //{
        //    requestHandlers.Add(handler);
        //}

        //public void RemoveHandler(IRequestHandler handler)
        //{
        //    requestHandlers.Remove(handler);
        //}

        /// <summary>
        /// Begins advertising the server name and IP via UDP.
        /// </summary>
        protected void StartUdpAdvertisement()
        {
            Task.Run(() =>
            {
                using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                {
                    EndPoint remoteEndPoint = new IPEndPoint(IPAddress.Parse("255.255.255.255"), MAPLE_SERVER_BROADCASTPORT);
                    socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, true);

                    string broadcastData = $"{DeviceName}::{IPAddress}";

                    while (Running)
                    {
                        socket.SendTo(UTF8Encoding.UTF8.GetBytes(broadcastData), remoteEndPoint);
                        Logger?.Info("UDP Broadcast: " + broadcastData + ", port: " + MAPLE_SERVER_BROADCASTPORT);

                        Thread.Sleep(AdvertiseIntervalMs);
                    }
                }
            });
        }

        /// <summary>
        /// Looks for IRequestHandlers and adds them to the `requestHandlers`
        /// collection for use later.
        /// </summary>
        protected void LoadRequestHandlers()
        {
            // Get classes that implement IRequestHandler
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            var typesAdded = 0;

            // loop through each assembly in the app and all the classes in it
            foreach (var assembly in assemblies)
            {
                var types = assembly.GetTypes();
                foreach (var t in types)
                {
                    // if it inherits `IRequestHandler`, add it to the list
                    if (t.BaseType != null)
                    {
                        if (t.BaseType.GetInterfaces().Contains(typeof(IRequestHandler)))
                        {
                            MethodCache.AddType(t);
                            typesAdded++;
                        }
                    }
                }
            }

            if (typesAdded == 0)
            {
                Console.WriteLine("Warning: No Maple Server `IRequestHandler`s found. Server will not operate.");
            }
            else
            {
                Logger?.Info($"requestHandlers.Count: {typesAdded}");
            }
        }

        /// <summary>
        /// Starts a thread that listens to incoming Http requests and handles
        /// them. Note that the current implementation handles requests serially,
        /// rather than in parallel.
        /// </summary>
        /// <returns></returns>
        protected async Task StartListeningToIncomingRequests()
        {
            if (Running)
            {
                Logger.Error("Already running.");
                return;
            }

            Running = true;

            await Task.Run(async () =>
            {
                Logger?.Info("starting up listener.");

                while (Running)
                {
                    try
                    {
                        // wait for a request to come in
                        var context = await _httpListener.GetContextAsync();
                        Logger?.Info("got one!");

                        // depending on our processing mode, process either
                        // synchronously, or spin off a thread and immediately
                        // process the next request (as it comes in)
                        switch (ThreadingMode)
                        {
                            case RequestProcessMode.Serial:
                                ProcessRequest(context).Wait();
                                break;
                            case RequestProcessMode.Parallel:
                                _ = ProcessRequest(context);
                                break;
                        }
                    }
                    catch (SocketException e)
                    {
                        Logger?.Error("Socket Exception: " + e.ToString());
                    }
                    catch (Exception ex)
                    {
                        Logger?.Error(ex.ToString());
                    }
                }
            });
        }

        public virtual async Task Return404(HttpListenerContext context)
        {
            // TODO: potentially load content from a file?
            byte[] data = Encoding.UTF8.GetBytes("<head><body>404. Not found.</body><head>");
            context.Response.ContentType = "text/html";
            context.Response.ContentEncoding = Encoding.UTF8;
            context.Response.ContentLength64 = data.LongLength;
            context.Response.StatusCode = 404;
            await context.Response.OutputStream.WriteAsync(data, 0, data.Length);
            context.Response.Close();
        }

        private IRequestHandler GetHandlerInstance(Type handlerType, out bool shouldDispose)
        {
            IRequestHandler target;
            shouldDispose = false;

            if (_handlerCache.ContainsKey(handlerType))
            {
                target = _handlerCache[handlerType];
            }
            else
            {
                // instantiate the handler, set the context (which contains all the request info)
                target = Activator.CreateInstance(handlerType) as IRequestHandler;

                if (target.IsReusable)
                {
                    // cache for later use
                    _handlerCache.Add(handlerType, target);
                }
                else
                {
                    shouldDispose = true;
                }
            }

            return target;
        }

        protected Task ProcessRequest(HttpListenerContext context)
        {
            return Task.Run(async () =>
            {
                string[] urlQuery = context.Request.RawUrl.Substring(1).Split('?');
                string[] urlParams = urlQuery[0].Split('/');
                string requestedMethodName = urlParams[0].ToLower();

                Logger?.Info("Received " + context.Request.HttpMethod + " " + context.Request.RawUrl + " - Invoking " + requestedMethodName);

                var handlerInfo = MethodCache.Match(context.Request.HttpMethod, context.Request.RawUrl, out object param);
                if (handlerInfo == null)
                {
                    await Return404(context);
                    return;
                }
                else
                {
                    var handlerInstance = GetHandlerInstance(handlerInfo.HandlerType, out bool shouldDispose);

                    handlerInstance.Context = context;

                    object[] paramObjects = null;

                    if (handlerInfo.Parameter != null)
                    {
                        paramObjects = new object[]
                        {
                            param
                        };
                    }

                    try
                    {
                        if (typeof(IActionResult).IsAssignableFrom(handlerInfo.Method.ReturnType))
                        {
                            var result = handlerInfo.Method.Invoke(handlerInstance, paramObjects) as IActionResult;
                            await result.ExecuteResultAsync(context);
                        }
                        else
                        {
                            handlerInfo.Method.Invoke(handlerInstance, paramObjects);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger?.Error(ex.Message);
                        context.Response.StatusCode = 500;
                        context.Response.Close();
                    }

                    // if the handler is not reusable, clean up
                    if (shouldDispose)
                    {
                        handlerInstance.Dispose();
                    }
                }
            });
        }
    }
}