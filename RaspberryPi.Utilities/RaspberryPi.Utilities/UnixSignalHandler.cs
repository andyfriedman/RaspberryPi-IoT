using System;
using System.Threading;
using Mono.Unix;
using Mono.Unix.Native;
using RaspberryPi.Logger;

namespace RaspberryPi.Utilities
{
    public class UnixSignalHandler : IDisposable
    {
        private readonly UnixSignal[] _signals;
        private Thread _signalThread;
        private bool _signaled;

        public ILogger Logger { get; set; }

        public UnixSignalHandler()
        {
            try
            {
                _signals = new UnixSignal[]
                {
                    new UnixSignal(Signum.SIGINT),
                    new UnixSignal(Signum.SIGTERM),
                    new UnixSignal(Signum.SIGHUP),
                    new UnixSignal(Signum.SIGQUIT),
                    new UnixSignal(Signum.SIGUSR1),
                };
            }
            catch
            { }
        }
     
        public bool HandleSignal(Action<Signum> signalFunc)
        {
            if (_signals == null)
            {
                if (Logger != null)
                    Logger.Warning(typeof(UnixSignalHandler), "Unix signals not supported on this platform");
                return false;
            }

            if (_signalThread != null && _signalThread.IsAlive)
                return true;
           
            _signalThread = new Thread(() =>
            {
                try
                {
                    if (_signals != null)
                    {
                        // Wait for a signal to be delivered
                        var index = UnixSignal.WaitAny(_signals);
                        var signal = _signals[index].Signum;
                        _signaled = true;

                        if (signalFunc != null)
                            signalFunc(signal);
                    }
                }
                catch (ThreadAbortException)
                {
                    // eat it
                }
                catch (Exception ex)
                {
                    if (Logger != null)
                        Logger.Error(typeof(UnixSignalHandler), "Error occurred in signal thread: " + ex.Message);
                }
            }) { IsBackground = true };

            _signalThread.Start();
            return true;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_signalThread != null)
                {
                    if (_signaled)
                        _signalThread.Join();
                    else
                        _signalThread.Abort();
                }

                if (_signals != null)
                {
                    foreach (var signal in _signals)
                    {
                        try
                        {
                            signal.Close();
                        }
                        catch { }
                    }
                }
            }
        }
    }
}
