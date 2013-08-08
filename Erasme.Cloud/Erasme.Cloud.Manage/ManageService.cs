// ManageService.cs
// 
//  Provide services to monitor server status
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
using System.Threading.Tasks;
using System.Collections.Generic;
using Erasme.Http;
using Erasme.Json;

namespace Erasme.Cloud.Manage
{
	public class ManageService: IHttpHandler
	{
		public ManageService()
		{
		}

		public JsonValue GetClients(HttpContext context)
		{
			// get connected HTTP clients
			JsonArray clients = new JsonArray();
			foreach(HttpServerClient client in context.Client.Server.Clients) {
				JsonObject jsonClient = new JsonObject();
				jsonClient["remote"] = client.RemoteEndPoint.ToString();
				jsonClient["local"] = client.LocalEndPoint.ToString();
				jsonClient["websocket"] = (client.WebSocket != null);
				jsonClient["uptime"] = (DateTime.Now - client.StartTime).TotalSeconds;
				jsonClient["readcounter"] = client.ReadCounter;
				jsonClient["writecounter"] = client.WriteCounter;
				jsonClient["requestcounter"] = client.RequestCounter;

				if(client.Context != null) {
					jsonClient["path"] = client.Context.Request.AbsolutePath;
					jsonClient["user"] = client.Context.User;
				}
				clients.Add(jsonClient);
			}
			return clients;
		}

		public bool CloseClient(HttpContext context, string addressOrUser)
		{
			bool done = false;
			Console.WriteLine("ClientClient("+addressOrUser+")");
			foreach(HttpServerClient client in context.Client.Server.Clients) {
				if((client != context.Client) && (
					(client.RemoteEndPoint.ToString() == addressOrUser) ||
					((client.Context != null) && (client.Context.User == addressOrUser)))) {
					client.Close();
					done = true;
				}
			}
			return done;
		}

		public Task ProcessRequestAsync(HttpContext context)
		{
			string[] parts = context.Request.Path.Split(new char[] { '/' }, System.StringSplitOptions.RemoveEmptyEntries);

			if((context.Request.Method == "GET") && (parts.Length == 0)) {
				context.Response.StatusCode = 200;
				context.Response.Headers["cache-control"] = "no-cache, must-revalidate";
				JsonValue json = new JsonObject();
				context.Response.Content = new JsonContent(json);
				// get connected HTTP clients
				json["clients"] = GetClients(context);
				context.Response.Content = new JsonContent(json);
			}
			// GET /clients get all connected HTTP clients
			else if((context.Request.Method == "GET") && (context.Request.Path == "/clients")) {
				context.Response.StatusCode = 200;
				context.Response.Headers["cache-control"] = "no-cache, must-revalidate";
				context.Response.Content = new JsonContent(GetClients(context));
			}
			// DELETE /clients/[address or user] close a client connection
			else if((context.Request.Method == "DELETE") && (parts.Length == 2) && (parts[0] == "clients")) {
				CloseClient(context, parts[1]);
			}
			return Task.FromResult<Object>(null);
		}
	}
}
