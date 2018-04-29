using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Nancy;

namespace PiWebHost
{
    internal class HtmlContent : Response
    {
        public HtmlContent(string content)
        {
            ContentType = "text/html";
            Contents = strm =>
            {
                var data = Encoding.UTF8.GetBytes(content);
                strm.Write(data, 0, data.Length);
            };
        }
    }
}
