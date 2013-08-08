// MainClass.cs
// 
//  HTTP client program to test TestCloudServer
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
using System.Threading;
using System.Collections.Generic;
using Erasme.Http;
using Erasme.Json;

namespace TestCloudClient
{
	delegate bool TestHandler();

	class MainClass
	{
		static int NbRequest = 100;
		static int NbThread = 4;
		static int SlowRequestLevel = 50;

		public static bool TestStaticFilesService()
		{
			bool done = false;
			using (HttpClient client = HttpClient.Create("localhost", 3333)) {
				HttpClientRequest request = new HttpClientRequest();
				request.Method = "GET";
				request.Path = "/files/test.txt";
				client.SendRequest(request);
				HttpClientResponse response = client.GetResponse();
				string res = response.ReadAsString();
				done = (res == "Hello World !\n");
			}
			return done;
		}

		public static bool TestMimeIconService()
		{
			bool done = false;
			using (HttpClient client = HttpClient.Create("localhost", 3333)) {
				HttpClientRequest request = new HttpClientRequest();
				request.Method = "GET";
				request.Path = "/mimeicon/application/pdf";
				client.SendRequest(request);
				HttpClientResponse response = client.GetResponse();
				done = (response.Headers["content-type"] == "image/png");
			}
			return done;
		}

		public static bool TestSendmailService()
		{
			bool done = false;
			using (HttpClient client = HttpClient.Create("localhost", 3333)) {
				HttpClientRequest request = new HttpClientRequest();
				request.Method = "POST";
				request.Path = "/sendmail";
				JsonValue content = new JsonObject();
				content["from"] = "dlacroix@erasme.org";
				content["to"] = "dlacroix@erasme.org";
				content["subject"] = "Test Sendmail erasme-cloud-cil";
				content["body"] = "Test only. Trash me";
				request.Content = new JsonContent(content);
				client.SendRequest(request);
				HttpClientResponse response = client.GetResponse();
				done = (response.StatusCode == 200);
			}
			return done;
		}

		public static bool TestMessageService()
		{
			bool done = false;
			using (HttpClient client = HttpClient.Create("localhost", 3333)) {
				HttpClientRequest request = new HttpClientRequest();
				request.Method = "POST";
				request.Path = "/message";
				JsonValue content = new JsonObject();
				content["origin"] = 1;
				content["destination"] = 2;
				content["type"] = "message";
				content["content"] = "Hello user 2";
				request.Content = new JsonContent(content);
				client.SendRequest(request);
				HttpClientResponse response = client.GetResponse();
				if(response.StatusCode == 200) {
					JsonValue json = response.ReadAsJson();
					done = (json["origin"] == 1) && (json["destination"] == 2);
				}
			}
			return done;
		}

		public static bool TestMessageServiceSearch()
		{
			bool done = false;
			using (HttpClient client = HttpClient.Create("localhost", 3333)) {
				HttpClientRequest request = new HttpClientRequest();
				request.Method = "GET";
				request.Path = "/message";
				request.QueryString["user"] = "1";
				client.SendRequest(request);
				HttpClientResponse response = client.GetResponse();
				if(response.StatusCode == 200) {
					JsonValue json = response.ReadAsJson();
					done = (json.Count == 1);
				}
			}
			return done;
		}

		public static bool TestMessageServiceDelete()
		{
			bool done = false;
			using(HttpClient client = HttpClient.Create("localhost", 3333)) {
				HttpClientRequest request = new HttpClientRequest();
				request.Method = "DELETE";
				request.Path = "/message/1";
				client.SendRequest(request);
				HttpClientResponse response = client.GetResponse();
				done = (response.StatusCode == 200);
			}
			return done;
		}

		public static bool TestStorageServiceCreate()
		{
			bool done = false;
			using(HttpClient client = HttpClient.Create("localhost", 3333)) {
				HttpClientRequest request = new HttpClientRequest();
				request.Method = "POST";
				request.Path = "/storage";
				JsonValue json = new JsonObject();
				json["quota"] = -1;
				request.Content = new JsonContent(json);
				client.SendRequest(request);
				HttpClientResponse response = client.GetResponse();
				done = (response.StatusCode == 200);
			}
			return done;
		}

		public static bool TestStorageServiceCreateFile()
		{
			bool done = false;
			using(HttpClient client = HttpClient.Create("localhost", 3333)) {
				HttpClientRequest request = new HttpClientRequest();
				request.Method = "POST";
				request.Path = "/storage/1/0";
				MultipartContent content = new MultipartContent();
				FileContent fileContent = new FileContent("../../data/document/text.txt");
				Dictionary<string,string> disposition = new Dictionary<string,string>();
				disposition["name"] = "file";
				disposition["filename"] = "text.txt";
				fileContent.Headers["content-disposition"] = ContentDisposition.Encode(disposition);
				content.Add(fileContent);
				request.Content = content;
				client.SendRequest(request);
				HttpClientResponse response = client.GetResponse();
				done = (response.StatusCode == 200);
			}
			return done;
		}

		public static bool TestStorageServiceDeleteFile()
		{
			bool done = false;
			using(HttpClient client = HttpClient.Create("localhost", 3333)) {
				HttpClientRequest request = new HttpClientRequest();
				request.Method = "DELETE";
				request.Path = "/storage/1/1";
				client.SendRequest(request);
				HttpClientResponse response = client.GetResponse();
				done = (response.StatusCode == 200);
			}
			return done;
		}

		public static bool TestStorageServiceDelete()
		{
			bool done = false;
			using(HttpClient client = HttpClient.Create("localhost", 3333)) {
				HttpClientRequest request = new HttpClientRequest();
				request.Method = "DELETE";
				request.Path = "/storage/1";
				client.SendRequest(request);
				HttpClientResponse response = client.GetResponse();
				done = (response.StatusCode == 200);
			}
			return done;
		}

		public static void Display(string desc, TestHandler handler)
		{
			Console.Write("Test "+desc+": ");
			if(handler()) {
				Console.ForegroundColor = ConsoleColor.Green;
				Console.WriteLine("DONE");
			}
			else {
				Console.ForegroundColor = ConsoleColor.Red;
				Console.WriteLine("FAILS");
			}
			Console.ForegroundColor = ConsoleColor.Black;
		}

		public static void Bench(string display, TestHandler handler)
		{
			DateTime start = DateTime.Now;
			if(NbThread == 1) {
				BenchThreadStart(handler);
			}
			else {
				List<Thread> threads = new List<Thread>();
				for(int i = 0; i < NbThread; i++) {
					Thread thread = new Thread(BenchThreadStart);
					threads.Add(thread);
					thread.Start(handler);
				}
				foreach(Thread thread in threads) {
					thread.Join();
				}
			}
			TimeSpan duration = DateTime.Now - start;
			Console.Write("Bench "+display+" ");
			int reqSecond = (int)Math.Round((NbRequest*NbThread)/duration.TotalSeconds);
			if(reqSecond < SlowRequestLevel)
				Console.ForegroundColor = ConsoleColor.Red;
			else
				Console.ForegroundColor = ConsoleColor.DarkBlue;
			Console.Write("{0}", reqSecond);
			Console.ForegroundColor = ConsoleColor.Black;
			Console.WriteLine(" req/s"); 
		}

		static void BenchThreadStart(object obj)
		{
			TestHandler handler = (TestHandler)obj;
			for(int i = 0; i < NbRequest; i++) {
				handler();
			}
		}

		public static void Main(string[] args)
		{
			Console.WriteLine("Start tests...");

			Display("StaticFilesService", TestStaticFilesService);
			Display("MimeIconService", TestMimeIconService);
			Display("SendmailService", TestSendmailService);
			Display("MessageService Send", TestMessageService);
			Display("MessageService Search", TestMessageServiceSearch);
			Display("MessageService Delete", TestMessageServiceDelete);
			Display("StorageService Create", TestStorageServiceCreate);
			Display("StorageService Create File", TestStorageServiceCreateFile);
			Display("StorageService Delete File", TestStorageServiceDeleteFile);
			Display("StorageService Delete", TestStorageServiceDelete);

			Bench("StaticFilesService", TestStaticFilesService);
			Bench("MimeIconService", TestMimeIconService);
			Bench("MessageService Send", TestMessageService);

			Console.WriteLine("Stop tests");
		}
	}
}
