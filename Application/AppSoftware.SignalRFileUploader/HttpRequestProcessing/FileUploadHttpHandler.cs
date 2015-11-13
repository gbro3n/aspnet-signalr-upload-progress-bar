using System;
using System.Configuration;
using System.IO;
using System.Threading.Tasks;
using System.Web;
using AppSoftware.SignalRFileUploader.Hubs;
using Microsoft.AspNet.SignalR;

namespace AppSoftware.SignalRFileUploader.HttpRequestProcessing
{
    public class FileUploadHttpHandler : IHttpHandler
    {
        private decimal m_lastStreamProgress = 0m;

        public bool IsReusable
        {
            get { return false; }
        }

        public void ProcessRequest(HttpContext context)
        {
            string str = context.Request.Url.ToString();

            // Handler is mapped to all requests to .upl extension.
            // HttpHandler is used over MVC controller action, as request will
            // have been fully uploaded by the action code runs.

            // We process the request using GetBufferlessInputStream() to avoid
            // useing any methods or properties on the request object that would
            // cause ASP.NET to wait for request to be recieved in it's entirety.
            // Calls to certain methods and properties can cause the code to block until
            // full request stream has been recieved by ASP.NET, such as Files,
            // InputStream (buffered version) etc. (http://msdn.microsoft.com/en-us/library/ff406798.aspx)

            // As a result, we can not read parameters from Request.Form without blocking. The
            // signalR connection id is used as the path file name so we can read this value
            // before beginning processing.

            // New BufferlessInputStream functionality in ASP.NET 4.5, don't think this
            // helps me though http://www.asp.net/vnext/overview/aspnet/whats-new

            string signalRConnectionId = Path.GetFileNameWithoutExtension(str);

            // Get a signalR context and client for notifying the UI of progress.

            IHubContext uploadHubContext = GlobalHost.ConnectionManager.GetHubContext<UploadHub>();

            dynamic currentClient = uploadHubContext.Clients.Client(signalRConnectionId);

            ClientUpdateStatus(currentClient, "Preparing to upload");

            bool uploadFailed = false;

            int bytesCopied = 0;
            int requestContentLength = context.Request.ContentLength;
            int bytesRemaining = requestContentLength;

            int filesUploaded = 0;

            Task asyncTask = Task.Factory.StartNew(() =>
            {
                var appSettings = ConfigurationManager.AppSettings;

                string fileNameGuid = Guid.NewGuid().ToString();

                string saveDirectoryPath = appSettings["AppSoftware.SignalRFileUploader.UploaderContentFolder"];

                string requestTempSavePath = saveDirectoryPath + @"Temp\" + fileNameGuid + ".upl";

                // Write the request out to our own temp file for processing
                // when complete. This enables the handling of larger files

                using (var fileStream = new FileStream(requestTempSavePath, FileMode.CreateNew))
                {
                    byte[] buffer = new byte[8 * 1024];

                    int length;

                    // Use a bufferless input stream to prevent ASP.NET from blocking
                    // until the entire request has been uploaded. 

                    Stream bufferlessInputStream = context.Request.GetBufferlessInputStream();

                    int readSize = buffer.Length;

                    while (bytesRemaining > 0)
                    {
                        // We use a specified read size here rather than full buffer length 
                        // because BufferlessInputStream won't know when the stream is fully uploaded.
                        // Attempting to read bytes that are never coming results in a hang. This
                        // is why read size is calculated from bytesRemaining, which is initialy
                        // set to the request content size.

                        length = bufferlessInputStream.Read(buffer, 0, readSize);

                        fileStream.Write(buffer, 0, length);

                        bytesCopied += length;

                        bytesRemaining -= length;

                        if (readSize > bytesRemaining)
                        {
                            readSize = bytesRemaining;
                        }

                        ClientUpdateUploadProgress(currentClient, bytesCopied, requestContentLength);
                    }

                    bufferlessInputStream.Close();

                    fileStream.Close();
                }

                ClientUpdateStatus(currentClient, "Processing temporary data");

                try
                {
                    // Create a FileStream from the raw request data that is currently
                    // persisted to a temporary file. the FileStream is loaded into StreamIndexer
                    // which provides compatibility with HttpMultipartContentTemplateParser
                    // adapted from decompiled .NETs System.Web, while allowing us to
                    // process the raw request data with out fully loading into
                    // memory while we split the multipart content

                    using (var fileStream = new FileStream(requestTempSavePath, FileMode.Open))
                    {
                        byte[] multiPartBoundary = HttpMultipartContentTemplateParser.GetMultipartBoundary(context.Request.ContentType);

                        // Each element maintains a reference to a StreamIndexer, which has a reference to
                        // the file stream that provides access to the raw request data currently saved.
                        // When SaveAsFile is called, the data is streamed out of the fileStream using
                        // positional information stored during parse stage.

                        var multiContentElements = HttpMultipartContentTemplateParser.Parse(
                            new StreamIndexer(fileStream),
                            fileStream.Length,
                            multiPartBoundary,
                            context.Request.ContentEncoding
                        );

                        foreach (var element in multiContentElements)
                        {
                            if (element.IsFile)
                            {
                                element.SaveAsFile(saveDirectoryPath + fileNameGuid + "_" + element.FileName);

                                filesUploaded++;
                            }
                            else
                            {
                                // Optionaly do something with value e.g.

                                // string name = element.Name;
                                // string value = element.GetAsString(context.Request.ContentEncoding);
                            }
                        }
                    }
                }
                finally
                {
                    ClientUpdateStatus(currentClient, "Cleaning up");

                    // Clean up temp file

                    File.Delete(requestTempSavePath);
                }

            }).ContinueWith(task =>
            {
                uploadFailed = task.IsFaulted;
            });

            try
            {
                asyncTask.Wait();
            }
            catch(AggregateException aEx)
            {
                // Set aggregate exception handled, this will otherwise be thrown
                // at finally stage

                http://msdn.microsoft.com/en-GB/library/dd537614.aspx

                aEx.Handle(x => false); // ToDo: Log and set true 
            }

            // Final update

            ClientUpdateStatus(currentClient, string.Format("Complete ({0} files uploaded). {1}", filesUploaded, uploadFailed ? "Errors occurred during this upload. Check log files." : String.Empty));
            ClientComplete(currentClient);

            // Send a response, which redirects the iframe. Sending a no content 
            // response seems to cause issue with subsequent uploads in firefox at least

            context.Response.StatusCode = 204;
            
        }

        private void ClientUpdateUploadProgress(dynamic currentClient, int bytesCopied, int requestContentLength)
        {
            decimal streamProgress = ((decimal) bytesCopied / requestContentLength) * 100;

            const int bytesInKiloByte = 1024;
            const int bytesInMegaByte = 1048576;

            // Only update if progress has increased significantly (1% or more)

            if ((streamProgress - m_lastStreamProgress) > 1)
            {
                string progressString;

                if (requestContentLength >= bytesInMegaByte)
                {
                    progressString = string.Format("{0:0.0}% ({1:0.0} MB of {2:0.0} MB)", streamProgress,
                                                   (decimal) bytesCopied / bytesInMegaByte, (decimal) requestContentLength / bytesInMegaByte);
                }
                else
                {
                    progressString = string.Format("{0:0.0}% ({1:0.0} KB of {2:0.0} KB)", streamProgress,
                                                   (decimal) bytesCopied / bytesInKiloByte, (decimal) requestContentLength / bytesInKiloByte);
                }

                string outputStr = (streamProgress == 100 ? "Upload complete, processing pending..." : progressString);

                currentClient.updateProgress(streamProgress);
                currentClient.updateStatus(outputStr);

                m_lastStreamProgress = streamProgress;
            }
        }

        private static void ClientUpdateStatus(dynamic currentClient, string status)
        {
            currentClient.updateStatus(status);
        }

        private static void ClientComplete(dynamic currentClient)
        {
            currentClient.complete();
        }
    }
}
