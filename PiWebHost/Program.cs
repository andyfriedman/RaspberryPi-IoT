using System;
using System.Configuration;
using System.Threading;
using Microsoft.Owin.Hosting;

namespace PiWebHost
{
    class Program
    {
        static void Main(string[] args)
        {
            var port = int.Parse(ConfigurationManager.AppSettings["Port"]);

            using (WebApp.Start<Startup>(string.Format("http://*:{0}", port)))
            {
                Console.WriteLine("Pi web host running on port {0}...", port);
                while (true)
                {
                    Thread.Sleep(1);
                }
            }
        }
    }
}
