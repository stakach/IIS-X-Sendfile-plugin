using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Web;
using System.Web.Hosting;
using System.Configuration;
using System.IO;

namespace XSendfile
{
    public class XSendfileHttpModule : IHttpModule
    {
        public void Init(HttpApplication context)
        {
            context.EndRequest += new EventHandler(context_EndRequest);
        }

        private void context_EndRequest(object sender, EventArgs e)
        {
            HttpResponse response = HttpContext.Current.Response;

            string filePath = response.Headers.Get("X-Sendfile");
            if (filePath == null)
                filePath = response.Headers.Get("X-Accel-Redirect");

            if (filePath != null)
            {
                filePath = Path.Combine(ConfigurationManager.AppSettings["XSendDir"], filePath);
                response.Clear();                               // Clears output buffer
                response.Headers.Remove("X-Sendfile");          // Remove unwanted headers
                response.Headers.Remove("X-Accel-Redirect");

                if(ConfigurationManager.AppSettings["XSendCache"] == null)
                    response.Cache.SetCacheability(HttpCacheability.NoCache);
                else if(ConfigurationManager.AppSettings["XSendCache"] == "Public")
                    response.Cache.SetCacheability(HttpCacheability.Public);
                else
                    response.Cache.SetCacheability(HttpCacheability.Private);

                FileInfo file = new FileInfo(filePath);
                response.Cache.SetLastModified(file.LastWriteTimeUtc);
                response.AddHeader("Content-Length", file.Length.ToString());


                //System.Configuration.Configuration staticContent = ConfigurationManager.GetSection("staticContent");
                //var mimeMap = staticContent.GetCollection();
                //var mt = mimeMap.Where(
                //    a => (string)a.Attributes["fileExtension"].Value == ".pdf"
                //    ).FirstOrDefault();

                //if (mt != null)
                //    response.ContentType = mt["mimeType"];
                //else
                    response.ContentType = "application/octet-stream";

                response.AppendHeader("Content-Disposition", "inline;filename=" + file.Name);
                response.TransmitFile(file.FullName);
            }
        }

        public void Dispose() { }
    }
}
