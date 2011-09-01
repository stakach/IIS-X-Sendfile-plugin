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

            //
            // Check for the X-Send headers
            //
            string filePath = response.Headers.Get("X-Sendfile");
            if (filePath == null)
                filePath = response.Headers.Get("X-Accel-Redirect");

            if (filePath != null)
            {
                //
                // Determine the file path and ready teh response
                //
                filePath = Path.Combine(ConfigurationManager.AppSettings["XSendDir"], filePath);
                response.Clear();                               // Clears output buffer
                response.Headers.Remove("X-Sendfile");          // Remove unwanted headers
                response.Headers.Remove("X-Accel-Redirect");


                //
                // Set the cache policy
                //
                if(ConfigurationManager.AppSettings["XSendCache"] == null)
                    response.Cache.SetCacheability(HttpCacheability.NoCache);
                else if(ConfigurationManager.AppSettings["XSendCache"] == "Public")
                    response.Cache.SetCacheability(HttpCacheability.Public);
                else
                    response.Cache.SetCacheability(HttpCacheability.Private);


                //
                // Get the file information and set headers appropriately
                //
                FileInfo file = new FileInfo(filePath);
                response.Cache.SetLastModified(file.LastWriteTimeUtc);
                response.Headers.Remove("Content-Length");
                response.AddHeader("Content-Length", file.Length.ToString());


                //
                // Check if we want to detect the mime type of the current content
                //
                if (ConfigurationManager.AppSettings["XSendMime"] == null)
                {
                    //System.Configuration.Configuration staticContent = ConfigurationManager.GetSection("staticContent");
                    //var mimeMap = staticContent.GetCollection();
                    //var mt = mimeMap.Where(
                    //    a => (string)a.Attributes["fileExtension"].Value == ".pdf"
                    //    ).FirstOrDefault();

                    //if (mt != null)
                    //    response.ContentType = mt["mimeType"];
                    //else
                    response.ContentType = "application/octet-stream";
                }


                //
                // Set a content disposition if it is not already set by the application
                //
                if (response.Headers["Content-Disposition"] == null)
                    response.AppendHeader("Content-Disposition", "inline;filename=" + file.Name);


                //
                //  Send the file without loading it into memory
                //
                response.TransmitFile(file.FullName);
            }
        }

        public void Dispose() { }
    }
}
