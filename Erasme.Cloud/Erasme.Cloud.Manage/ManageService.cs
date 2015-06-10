// ManageService.cs
// 
//  Provide services to monitor server status
//
// Author(s):
//  Daniel Lacroix <dlacroix@erasme.org>
// 
// Copyright (c) 2013-2014 Departement du Rhone
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
using System.Text.RegularExpressions;
using Erasme.Http;
using Erasme.Json;
using Erasme.Cloud.Utils;

namespace Erasme.Cloud.Manage
{
	public class ManageService: IHttpHandler
	{
		PriorityTaskScheduler scheduler;

		public ManageService(PriorityTaskScheduler scheduler)
		{
			this.scheduler = scheduler;
			Rights = new DummyManageRights();
		}

		public IManageRights Rights { get; set; }

		bool CheckFilters(JsonObject json, Dictionary<string, string> filters)
		{
			if(filters == null)
				return true;
			foreach(string key in filters.Keys) {
				if(!json.ContainsKey(key))
					return false;
				if(!Regex.IsMatch((string)json[key], filters[key], RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace))
					return false;
			}
			return true;
		}

		public JsonValue GetClients(HttpContext context)
		{
			return GetClients(context, null);
		}

		public JsonValue GetClients(HttpContext context, Dictionary<string,string> filters)
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
					if(client.Context.Request.Headers.ContainsKey("x-forwarded-for"))
						jsonClient["x-forwarded-for"] = client.Context.Request.Headers["x-forwarded-for"];
					if(client.Context.Request.Headers.ContainsKey("user-agent"))
						jsonClient["user-agent"] = client.Context.Request.Headers["user-agent"];

					WebSocketHandler handler = client.Context.WebSocketHandler;
					if(handler != null) {
						IManageExtension extension = handler as IManageExtension;
						if(extension != null) {
							extension.GetStatus(jsonClient);
						}
					}

				}
				if(CheckFilters(jsonClient, filters))
					clients.Add(jsonClient);
			}
			return clients;
		}

		public bool CloseClient(HttpContext context, string addressOrUser)
		{
			bool done = false;
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

		public JsonValue GetTasks()
		{
			return GetTasks(null);
		}

		public JsonValue GetTasks(Dictionary<string,string> filters)
		{
			LongTask[] allTasks = scheduler.Tasks;

			// get connected HTTP clients
			JsonArray tasks = new JsonArray();
			foreach(LongTask task in allTasks) {
				JsonObject jsonTask = new JsonObject();
				jsonTask["id"] = task.Id;
				jsonTask["status"] = task.Status.ToString();
				jsonTask["create"] = task.CreateDate.ToString("yyyy'-'MM'-'dd'T'HH':'mm':'ss'.'fff'Z'");
				jsonTask["owner"] = task.Owner;
				jsonTask["description"] = task.Description;
				jsonTask["priority"] = task.Priority.ToString();
				if(CheckFilters(jsonTask, filters))
					tasks.Add(jsonTask);
			}
			return tasks;
		}

		public bool AbortTask(string id)
		{
			bool done = false;
			foreach(LongTask task in scheduler.Tasks) {
				if(task.Id == id) {
					task.Abort();
					done = true;
					break;
				}
			}
			return done;
		}

		public Task ProcessRequestAsync(HttpContext context)
		{
			string[] parts = context.Request.Path.Split(new char[] { '/' }, System.StringSplitOptions.RemoveEmptyEntries);

			if((context.Request.Method == "GET") && (parts.Length == 0)) {
				Rights.EnsureCanReadClients(context);
				Rights.EnsureCanReadTasks(context);

				context.Response.StatusCode = 200;
				context.Response.Headers["cache-control"] = "no-cache, must-revalidate";
				JsonValue json = new JsonObject();
				context.Response.Content = new JsonContent(json);
				// get connected HTTP clients
				json["clients"] = GetClients(context, context.Request.QueryString);
				// get running tasks
				json["tasks"] = GetTasks(context.Request.QueryString);
				context.Response.Content = new JsonContent(json);
			}
			// GET /clients get all connected HTTP clients
			else if((context.Request.Method == "GET") && (context.Request.Path == "/clients")) {
				Rights.EnsureCanReadClients(context);

				context.Response.StatusCode = 200;
				context.Response.Headers["cache-control"] = "no-cache, must-revalidate";
				context.Response.Content = new JsonContent(GetClients(context, context.Request.QueryString));
			}
			// GET /tasks get all scheduled tasks
			else if((context.Request.Method == "GET") && (context.Request.Path == "/tasks")) {
				Rights.EnsureCanReadTasks(context);

				context.Response.StatusCode = 200;
				context.Response.Headers["cache-control"] = "no-cache, must-revalidate";
				context.Response.Content = new JsonContent(GetTasks(context.Request.QueryString));
			}
			// DELETE /clients/[address or user] close a client connection
			else if((context.Request.Method == "DELETE") && (parts.Length == 2) && (parts[0] == "clients")) {
				Rights.EnsureCanDeleteClients(context);

				CloseClient(context, parts[1]);
				context.Response.StatusCode = 200;
				context.Response.Headers["cache-control"] = "no-cache, must-revalidate";
			}
			// DELETE /tasks/[ID] abort a task
			else if((context.Request.Method == "DELETE") && (parts.Length == 2) && (parts[0] == "tasks")) {
				Rights.EnsureCanDeleteTasks(context);

				AbortTask(parts[1]);

				context.Response.StatusCode = 200;
				context.Response.Headers["cache-control"] = "no-cache, must-revalidate";
			}
			return Task.FromResult<Object>(null);
		}
	}
}
