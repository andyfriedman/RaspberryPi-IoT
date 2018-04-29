using System.Configuration;
using System.IO;
using System.Linq;
using System.Text;
using Nancy;

namespace PiWebHost.Modules
{
    public class LogViewerModule : NancyModule
    {
        private static readonly string LogPath = ConfigurationManager.AppSettings["LogPath"];

        public LogViewerModule()
        {
            Get["/logs"] = parameters =>
            {
                const string htmlTemplate = "<html><head><title>Logs</title></head><body><h2>Recent Logs</h2><hr><br>\r\n{0}</body></html>";
                const string linkTemplate = "<a href=\"/log/{0}\">{0}</a><br>\r\n";

                // get the last 20 log file names
                var files = Directory.GetFiles(LogPath)
                    .Reverse()
                    .Take(20)
                    .Reverse()
                    .Select(Path.GetFileName);

                var sb = new StringBuilder();
                foreach (var file in files)
                {
                    sb.Append(string.Format(linkTemplate, Path.GetFileName(file)));
                }

                return new HtmlContent(string.Format(htmlTemplate, sb.ToString()));
            };

            Get["/log/{logFile}"] = parameters =>
            {
                const string htmlTemplate = "<html><head><title>{0}</title></head><body><h2>{0}</h2><hr>\r\n{1}</body></html>";
                
                var logFile = (string)parameters["logFile"].Value;
                var logFilePath = Path.Combine(LogPath, logFile);

                if (File.Exists(logFilePath))
                {
                    var log = File.ReadAllText(logFilePath).Replace(new string(new[] {'\r', '\n'}), "<br>\r\n");
                    return new HtmlContent(string.Format(htmlTemplate, logFile, log));
                }

                // will generate the default Nancy 404 page since logFile parameter is missing hence there is no defined route
                return Response.AsRedirect("/log"); 
            };
        }
    }
}
