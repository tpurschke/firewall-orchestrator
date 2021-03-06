﻿using FWO.Logging;
using System;

namespace FWO.Middleware.Server
{
    class Program
    {
        static void Main()
        {
            try
            {
                MiddlewareServer Server = new MiddlewareServer();
            }
            catch (Exception exception)
            {
                // Log error
                Log.WriteError("Unhandled unexpected exception", "Unhandled unexpected exception caught at Programm.cs", exception);
                // Exit auth module with error
                Environment.Exit(1);
            }
        }
    }
}

