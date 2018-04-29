using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Mono.Unix;
using RaspberryPi.Utilities;

namespace UnixSignalHandlerTester
{
    class Program
    {
        static void Main(string[] args)
        {
            var mre = new ManualResetEvent(false);
            var signalHandler = new UnixSignalHandler();

            var handled = signalHandler.HandleSignal(x =>
            {
                Console.WriteLine("Signal received: " + x);
                mre.Set();
            });

            if (handled)
            {
                Console.WriteLine("Waiting for a signal...");
                mre.WaitOne();
            }
            else
            {
                Console.WriteLine("Unix signals not supported on this platform");
            }

            Console.WriteLine("Exiting program");
            signalHandler.Dispose();
        }
    }
}
