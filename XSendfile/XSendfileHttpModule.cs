using System;
using System.Globalization;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Web;
using System.Web.Hosting;
using System.Web.Util;
using System.Configuration;
using System.IO;
using System.Runtime.InteropServices;

//
// This feels hacky
//
using Microsoft.Web.Administration;

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
            HttpRequest request = HttpContext.Current.Request;

            //
            // Check for the X-Send headers
            //
            bool remove = false;
            // we check for temp first, so any software that wants to use it can, as a fallback,
            // also set the regular x-sendfile header in case on that system this version of the
            // dll is not deployed
            string filePath = response.Headers.Get("X-Sendfile-Temporary");
            if (filePath == null) {
                filePath = response.Headers.Get("X-Sendfile");
            } else {
                remove = true;
            }
            if (filePath == null)
                filePath = response.Headers.Get("X-Accel-Redirect");

            if (filePath != null)
            {
                //
                // Determine the file path and ready the response
                //
                if (ConfigurationManager.AppSettings["XSendDir"] != null)
                    filePath = Path.Combine(ConfigurationManager.AppSettings["XSendDir"], filePath);    // if there is a base path set (file will be located above this)
                else if (ConfigurationManager.AppSettings["XAccelLocation"] != null)
                    filePath = filePath.Replace(ConfigurationManager.AppSettings["XAccelLocation"], ConfigurationManager.AppSettings["XAccelRoot"]);

                response.Clear();                               // Clears output buffer
                response.Headers.Remove("X-Sendfile");          // Remove unwanted headers
                response.Headers.Remove("X-Sendfile-Temporary");
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
                if (!file.Exists)
                {
                    throw new HttpException(404, "File_does_not_exist");
                }

                if (filePath[filePath.Length - 1] == '.')
                {
                    throw new HttpException(404, "File_does_not_exist");
                }

                
                response.Cache.SetLastModified(file.LastWriteTimeUtc);
                response.Headers.Remove("Content-Length");


                if (!String.IsNullOrEmpty(request.ServerVariables["HTTP_RANGE"]))
                {
                    //request for chunk
                    RangeDownload(file.FullName, HttpContext.Current);
                } else {
                    response.AddHeader("Content-Length", file.Length.ToString());
                    response.AppendHeader("Accept-Ranges", "bytes");


                    //
                    // Check if we want to detect the mime type of the current content
                    //
                    if (ConfigurationManager.AppSettings["XSendMime"] == null)
                    {
                        Microsoft.Web.Administration.ConfigurationSection staticContentSection = WebConfigurationManager.GetSection(HttpContext.Current, "system.webServer/staticContent");
                        Microsoft.Web.Administration.ConfigurationElementCollection staticContentCollection = staticContentSection.GetCollection();

                        var mt = staticContentCollection.Where(
                            a => a.Attributes["fileExtension"].Value.ToString().ToLower() == file.Extension.ToLower()
                            ).FirstOrDefault();

                        if (mt != null)
                            response.ContentType = mt.GetAttributeValue("mimeType").ToString();
                        else
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
                    if (remove)
                    {
                        // note that the little Flush below causes the full file to load into 
                        // IIS memory but we need that be able to delete it
                        // Note that on many concurrent requests, that means each request
                        // will load the full output into memory, which wil remain there for
                        // a while because the client needs time to download. Unfortunately
                        // I found no way to dispose of the file once the last byte has been
                        // sent
                        response.Flush();
                        File.Delete(file.FullName);
                    }
                }
            }
        }

        public void Dispose() { }

        //
        // http://blogs.visigo.com/chriscoulson/easy-handling-of-http-range-requests-in-asp-net/
        //
        private void RangeDownload(string fullpath, HttpContext context)
        {
            long size, start, end, length, fp = 0;

            using (StreamReader reader = new StreamReader(fullpath))
            {
         
                size = reader.BaseStream.Length;
                start = 0;
                end = size - 1;
                length = size;
                // Now that we've gotten so far without errors we send the accept range header
                /* At the moment we only support single ranges.
                 * Multiple ranges requires some more work to ensure it works correctly
                 * and comply with the spesifications: http://www.w3.org/Protocols/rfc2616/rfc2616-sec19.html#sec19.2
                 *
                 * Multirange support annouces itself with:
                 * header('Accept-Ranges: bytes');
                 *
                 * Multirange content must be sent with multipart/byteranges mediatype,
                 * (mediatype = mimetype)
                 * as well as a boundry header to indicate the various chunks of data.
                 */
                context.Response.AddHeader("Accept-Ranges", "0-" + size);
                // header('Accept-Ranges: bytes');
                // multipart/byteranges
                // http://www.w3.org/Protocols/rfc2616/rfc2616-sec19.html#sec19.2         
                long anotherStart = start;
                long anotherEnd = end;
                string[] arr_split = context.Request.ServerVariables["HTTP_RANGE"].Split(new char[] { Convert.ToChar("=") });
                string range = arr_split[1];
     
                // Make sure the client hasn't sent us a multibyte range
                if (range.IndexOf(",") > -1)
                {
                    // (?) Shoud this be issued here, or should the first
                    // range be used? Or should the header be ignored and
                    // we output the whole content?
                    context.Response.AddHeader("Content-Range", "bytes " + start + "-" + end + "/" + size);
                    throw new HttpException(416, "Requested Range Not Satisfiable");
     
                }
     
                // If the range starts with an '-' we start from the beginning
                // If not, we forward the file pointer
                // And make sure to get the end byte if spesified
                if (range.StartsWith("-"))
                {
                    // The n-number of the last bytes is requested
                    anotherStart = size - Convert.ToInt64(range.Substring(1));
                }
                else
                {
                    arr_split = range.Split(new char[] { Convert.ToChar("-") });
                    anotherStart = Convert.ToInt64(arr_split[0]);
                    long temp = 0;
                    anotherEnd = (arr_split.Length > 1 && Int64.TryParse(arr_split[1].ToString(), out temp)) ? Convert.ToInt64(arr_split[1]) : size;
                }
                /* Check the range and make sure it's treated according to the specs.
                 * http://www.w3.org/Protocols/rfc2616/rfc2616-sec14.html
                 */
                // End bytes can not be larger than $end.
                anotherEnd = (anotherEnd > end) ? end : anotherEnd;
                // Validate the requested range and return an error if it's not correct.
                if (anotherStart > anotherEnd || anotherStart > size - 1 || anotherEnd >= size)
                {
     
                    context.Response.AddHeader("Content-Range", "bytes " + start + "-" + end + "/" + size);
                    throw new HttpException(416, "Requested Range Not Satisfiable");
                }
                start = anotherStart;
                end = anotherEnd;
     
                length = end - start + 1; // Calculate new content length
                fp = reader.BaseStream.Seek(start, SeekOrigin.Begin);
                context.Response.StatusCode = 206;
            }
            // Notify the client the byte range we'll be outputting
            context.Response.AddHeader("Content-Range", "bytes " + start + "-" + end + "/" + size);
            context.Response.AddHeader("Content-Length", length.ToString());
            // Start buffered download

            // Don't buffer output as the file might be very large
            context.Response.BufferOutput = false;
            context.Response.WriteFile(fullpath, fp, length);
            context.Response.End();
        }

    }
}
