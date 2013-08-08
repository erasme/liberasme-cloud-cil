// QueueService.cs
// 
//  Provide a named queue to send and receive messages
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
using System.Net;
using System.Collections.Generic;
using System.Threading.Tasks;
using Erasme.Http;
using Erasme.Json;

namespace Erasme.Cloud.Queue
{
	public class QueueService: IHttpHandler
	{
		object instanceLock = new object();
		Dictionary<string,WebSocketHandlerCollection<MonitorClient>> clients = new Dictionary<string,WebSocketHandlerCollection<MonitorClient>>();

		class MonitorClient: WebSocketHandler
		{
			string channel;

			public MonitorClient(QueueService service, string channel)
			{
				Service = service;
				this.channel = channel;
			}

			public QueueService Service { get; private set; }
			
			public string Channel
			{
				get {
					return channel;
				}
			}

			public override void OnOpen()
			{
				lock(Service.instanceLock) {
					WebSocketHandlerCollection<MonitorClient> channelClients;
					if(Service.clients.ContainsKey(Channel)) {
						channelClients = Service.clients[Channel];
					}
					else {
						channelClients = new WebSocketHandlerCollection<MonitorClient>();
						Service.clients[Channel] = channelClients;
					}
					channelClients.Add(this);
				}
			}

			public override void OnMessage(string message)
			{
				Service.SendMessage(Channel, JsonValue.Parse(message));
			}

			public override void OnError()
			{
			}

			public override void OnClose()
			{
				lock(Service.instanceLock) {
					if(Service.clients.ContainsKey(Channel)) {
						WebSocketHandlerCollection<MonitorClient> channelClients;
						channelClients = Service.clients[Channel];
						channelClients.Remove(this);
						// remove the channel is empty
						if(channelClients.Count == 0)
							Service.clients.Remove(Channel);
					}
				}
			}

			public void Send(JsonValue  message)
			{
				Service.SendMessage(Channel, message);
			}
		}

		public QueueService()
		{
		}

		public bool SendMessage(string channel, JsonValue message)
		{
			bool done = false;
			string str = message.ToString();
			lock(instanceLock) {
				if(clients.ContainsKey(channel)) {
					WebSocketHandlerCollection<MonitorClient> channelClients = clients[channel];
					channelClients.Broadcast(str);
					done = true;
				}
			}
			return done;
		}

		public async Task ProcessRequestAsync(HttpContext context)
		{
			string[] parts = context.Request.Path.Split(new char[] { '/' }, System.StringSplitOptions.RemoveEmptyEntries);
			if(parts.Length == 1) {
				string channel = parts[0];
				if(context.Request.IsWebSocketRequest)
					// accept the web socket and process it
					await context.AcceptWebSocketRequest(new MonitorClient(this, channel));
				else {
					if((context.Request.Method == "POST") && (parts.Length == 1)) {
						JsonValue json = await context.Request.ReadAsJsonAsync();
						if(SendMessage(channel, json))
							context.Response.StatusCode = 200;
						else
							context.Response.StatusCode = 404;
					}
				}
			}
		}
	}
}
