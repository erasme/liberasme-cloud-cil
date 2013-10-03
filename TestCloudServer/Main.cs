// MainClass.cs
// 
//  HTTP server to test Erasme.Cloud services
//
// Author(s):
//  Daniel Lacroix <dlacroix@erasme.org>
// 
// Copyright (c) 2013 Departement du Rhone
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
//

using System;
using System.IO;
using Erasme.Http;
using Erasme.Cloud;
using Erasme.Cloud.Logger;

namespace TestCloudServer
{
	class MainClass
	{
		public const int CacheDuration = 3600;
		public const string SmtpServer = "smtp.erasme.org";

		public static void Main(string[] args)
		{
			ILogger logger = new ConsoleLogger();

			string temporaryDirectory = "/tmp/liberasme-cloud-cil";
			Directory.CreateDirectory(temporaryDirectory);
			Directory.CreateDirectory(temporaryDirectory+"/tmp");

			TestCloudServer server = new TestCloudServer(3333);

			PathMapper mapper = new PathMapper();
			server.Add(mapper);

			mapper.Add("/proxy", new Erasme.Cloud.HttpProxy.ProxyService());
			mapper.Add("/files", new Erasme.Cloud.StaticFiles.StaticFilesService("../../data/files/", CacheDuration));
			mapper.Add("/mimeicon", new Erasme.Cloud.Mime.MimeIconService("../../data/mimeicon/", CacheDuration));
			mapper.Add("/sendmail", new Erasme.Cloud.Mail.SendmailService(SmtpServer));
			mapper.Add("/queue", new Erasme.Cloud.Queue.QueueService());
			Directory.CreateDirectory(temporaryDirectory+"/message");
			mapper.Add("/message", new Erasme.Cloud.Message.MessageService(temporaryDirectory+"/message/"));
			Directory.CreateDirectory(temporaryDirectory+"/storage");
			Erasme.Cloud.Storage.StorageService storage = new Erasme.Cloud.Storage.StorageService(
				temporaryDirectory+"/storage/", temporaryDirectory+"/tmp/", CacheDuration, logger);
			mapper.Add("/storage", storage);
			Directory.CreateDirectory(temporaryDirectory+"/preview");
			mapper.Add("/preview", new Erasme.Cloud.Preview.PreviewService(temporaryDirectory+"/preview/", storage, 512, 512, temporaryDirectory+"/tmp/", CacheDuration, logger));

			server.Start();

			Console.WriteLine("Press enter to exit...");
			Console.ReadLine();

			server.Stop();
			Directory.Delete(temporaryDirectory, true);
		}
	}
}
