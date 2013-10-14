// MessageService.cs
// 
//  Service to send/receive messages from/to users
//
// Author(s):
//  Daniel Lacroix <dlacroix@erasme.org>
// 
// Copyright (c) 2012-2013 Departement du Rhone
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
using System.Text;
using System.Data;
using System.Threading.Tasks;
using System.Collections.Generic;
using Mono.Data.Sqlite;
using Erasme.Http;
using Erasme.Json;

namespace Erasme.Cloud.Message
{
	public class MessageService: HttpHandler, IDisposable
	{
		class MessageClient: WebSocketHandler
		{
			public MessageClient(MessageService service, long user)
			{
				Service = service;
				User = user;								
			}

			public long User { get; private set; }
			
			public MessageService Service { get; private set; }
						
			public override void OnOpen()
			{
				bool signalUser = false;
				lock(Service.instanceLock) {
					if(Service.clients.ContainsKey(User))
						Service.clients[User].Add(this);
					else {
						WebSocketHandlerCollection<MessageClient> list = new WebSocketHandlerCollection<MessageClient>();
						list.Add(this);
						Service.clients[User] = list;
						signalUser = true;
					}
				}
				if(signalUser)
					Service.RaisesUserWatched(User);
			}

			public override void OnClose()
			{
				bool signalUser = false;
				lock(Service.instanceLock) {
					if(Service.clients.ContainsKey(User)) {
						WebSocketHandlerCollection<MessageClient> list = Service.clients[User];
						list.Remove(this);
						if(list.Count == 0) {
							Service.clients.Remove(User);
							signalUser = true;
						}
					}
				}
				if(signalUser)
					Service.RaisesUserUnwatched(User);
			}
		}

		string basePath;

		object instanceLock = new object();
		Dictionary<long, WebSocketHandlerCollection<MessageClient>> clients = new Dictionary<long, WebSocketHandlerCollection<MessageClient>>();

		IDbConnection dbcon;

		public MessageService(string basepath)
		{
			basePath = basepath;

			if(!Directory.Exists(basepath))
				Directory.CreateDirectory(basepath);

			bool createNeeded = !File.Exists(basepath + "messages.db");

			dbcon = (IDbConnection)new SqliteConnection("URI=file:" + this.basePath + "messages.db");
			dbcon.Open();

			if(createNeeded) {

				// create the message table
				using (IDbCommand dbcmd = dbcon.CreateCommand()) {
					dbcmd.CommandText = "CREATE TABLE message (id INTEGER PRIMARY KEY AUTOINCREMENT, origin_id INTEGER, destination_id INTEGER, content VARCHAR, create_date INTEGER, seen_date INTEGER, type VARCHAR DEFAULT NULL)";
					dbcmd.ExecuteNonQuery();
				}

				// create connection log table
				using (IDbCommand dbcmd = dbcon.CreateCommand()) {
					dbcmd.CommandText = "CREATE TABLE log (id INTEGER PRIMARY KEY AUTOINCREMENT, user INTEGER, address INTEGER, port INTEGER, open INTEGER(1) DEFAULT 1, date INTEGER)";
					dbcmd.ExecuteNonQuery();
				}
			}

			// disable disk sync.
			using(IDbCommand dbcmd = dbcon.CreateCommand()) {
				dbcmd.CommandText = "PRAGMA synchronous=0";
				dbcmd.ExecuteNonQuery();
			}
		}

		public void Log(long user, long address, long port, bool open)
		{
			lock(dbcon) {
				// create the log entry
				using(IDbCommand dbcmd = dbcon.CreateCommand()) {
					dbcmd.CommandText = "INSERT INTO log (user,address,port,open,date) VALUES ("+user+","+address+","+port+","+(open?"1":"0")+",datetime('now'))";
					dbcmd.ExecuteNonQuery();
				}
			}
		}
		
		public delegate void MessageCreatedEventHandler(JsonValue message);
		List<MessageCreatedEventHandler> messageCreatedHandlers = new List<MessageCreatedEventHandler>();
		public event MessageCreatedEventHandler MessageCreated {
			add {
				lock(messageCreatedHandlers) {
					messageCreatedHandlers.Add(value);
				}
			}
			remove {
				lock(messageCreatedHandlers) {
					messageCreatedHandlers.Remove(value);
				}
			}
		}
		void RaisesMessageCreated(JsonValue message)
		{
			List<MessageCreatedEventHandler> handlers;
			lock(messageCreatedHandlers) {
				handlers = new List<MessageCreatedEventHandler>(messageCreatedHandlers);
			}
			foreach(MessageCreatedEventHandler handler in handlers) {
				try {
					handler(message);
				}
				catch(Exception) {}
			}
		}

		List<MessageCreatedEventHandler> messageChangedHandlers = new List<MessageCreatedEventHandler>();
		public event MessageCreatedEventHandler MessageChanged {
			add {
				lock(messageChangedHandlers) {
					messageChangedHandlers.Add(value);
				}
			}
			remove {
				lock(messageChangedHandlers) {
					messageChangedHandlers.Remove(value);
				}
			}
		}
		void RaisesMessageChanged(JsonValue message)
		{
			List<MessageCreatedEventHandler> handlers;
			lock(messageChangedHandlers) {
				handlers = new List<MessageCreatedEventHandler>(messageChangedHandlers);
			}
			foreach(MessageCreatedEventHandler handler in handlers) {
				try {
					handler(message);
				}
				catch(Exception) {}
			}
		}

		public delegate void UserWatchedEventHandler(long user);
		List<UserWatchedEventHandler> userWatchedHandlers = new List<UserWatchedEventHandler>();
		public event UserWatchedEventHandler UserWatched {
			add {
				lock(userWatchedHandlers) {
					userWatchedHandlers.Add(value);
				}
			}
			remove {
				lock(userWatchedHandlers) {
					userWatchedHandlers.Remove(value);
				}
			}
		}
		internal void RaisesUserWatched(long user)
		{
			List<UserWatchedEventHandler> handlers;
			lock(userWatchedHandlers) {
				handlers = new List<UserWatchedEventHandler>(userWatchedHandlers);
			}
			foreach(UserWatchedEventHandler handler in handlers) {
				try {
					handler(user);
				}
				catch(Exception) {}
			}
		}


		List<UserWatchedEventHandler> userUnwatchedHandlers = new List<UserWatchedEventHandler>();
		public event UserWatchedEventHandler UserUnwatched {
			add {
				lock(userUnwatchedHandlers) {
					userUnwatchedHandlers.Add(value);
				}
			}
			remove {
				lock(userUnwatchedHandlers) {
					userUnwatchedHandlers.Remove(value);
				}
			}
		}
		internal void RaisesUserUnwatched(long user)
		{
			List<UserWatchedEventHandler> handlers;
			lock(userUnwatchedHandlers) {
				handlers = new List<UserWatchedEventHandler>(userUnwatchedHandlers);
			}
			foreach(UserWatchedEventHandler handler in handlers) {
				try {
					handler(user);
				}
				catch(Exception) {}
			}
		}

		public bool DeleteMessage(long id)
		{
			bool done;
			lock(dbcon) {
				// delete messages
				using(IDbCommand dbcmd = dbcon.CreateCommand()) {
					dbcmd.CommandText = "DELETE FROM message WHERE id = "+id;
					done = (dbcmd.ExecuteNonQuery() == 1);
				}
			}
			return done;
		}

		public void DeleteUserMessages(long user)
		{
			lock(dbcon) {
				// delete messages
				using(IDbCommand dbcmd = dbcon.CreateCommand()) {
					dbcmd.CommandText = "DELETE FROM message WHERE origin_id = "+user+" OR destination_id = "+user;
					dbcmd.ExecuteNonQuery();
				}
			}
		}

		public JsonValue GetMessage(long id)
		{
			JsonValue json;
			lock(dbcon) {
				using(IDbTransaction transaction = dbcon.BeginTransaction()) {
					json = GetMessage(dbcon, transaction, id);
					transaction.Commit();
				}
			}
			return json;
		}
		
		JsonValue GetMessage(IDbConnection dbcon, IDbTransaction transaction, long id)
		{
			JsonValue message = new JsonObject();
			using(IDbCommand dbcmd = dbcon.CreateCommand()) {
				dbcmd.Transaction = transaction;
				dbcmd.CommandText = "SELECT id,type,origin_id,destination_id,content,strftime('%s',create_date),strftime('%s',seen_date) FROM message WHERE id="+id;
				using(IDataReader reader = dbcmd.ExecuteReader()) {
					if(!reader.Read())
						return null;
					message["id"] = reader.GetInt64(0);
					message["type"] = reader.GetString(1);
					message["origin"] = reader.GetInt64(2);
					message["destination"] = reader.GetInt64(3);
					message["content"] = reader.GetString(4);
					message["create"] = Convert.ToInt64(reader.GetString(5));
					if(reader.IsDBNull(6))
						message["seen"] = -1;
					else
						message["seen"] = Convert.ToInt64(reader.GetString(6));
					message["persist"] = true;
				}
			}
			return message;
		}

		public Task<JsonValue> SendMessageAsync(JsonValue message)
		{
			return Task<JsonValue>.Factory.StartNew((a) => SendMessage((JsonValue)a), message);
		}

		public JsonValue SendMessage(JsonValue message)
		{
			long origin;
			long destination;
			JsonValue json;
			if(message.ContainsKey("persist") && (message["persist"].Value is Boolean) && ((bool)message["persist"] == false)) {
				json = new JsonObject();
				string type = "message";
				if(message.ContainsKey("type"))
					type = (string)message["type"].Value;
				origin = Convert.ToInt64(message["origin"].Value);
				destination = Convert.ToInt64(message["destination"].Value);
				string content = String.Empty;
				if(message.ContainsKey("content"))
					content = (string)message["content"].Value;

				json["type"] = type;
				json["origin"] = origin;
				json["destination"] = destination;
				json["content"] = content;
				json["persist"] = false;
			}
			else {
				lock(dbcon) {
					using (IDbTransaction transaction = dbcon.BeginTransaction()) {
						long id = SendMessage(dbcon, transaction, message);
						json = GetMessage(dbcon, transaction, id);
						transaction.Commit();
					}
				}
				origin = (long)json["origin"];
				destination = (long)json["destination"];
			}
			// signal the created message to the watched users
			JsonValue messageJson = new JsonObject();
			messageJson["event"] = "messagecreated";
			messageJson["message"] = json;
			string jsonString = messageJson.ToString();
			lock(instanceLock) {
				if(clients.ContainsKey(origin))
					clients[origin].Broadcast(jsonString);
				if(clients.ContainsKey(destination))
					clients[destination].Broadcast(jsonString);
			}
			RaisesMessageCreated(json);
			return json;
		}

		long SendMessage(IDbConnection dbcon, IDbTransaction transaction, JsonValue message)
		{
			string type = "message";
			if(message.ContainsKey("type"))
				type = (string)message["type"];
			long origin = Convert.ToInt64(message["origin"].Value);
			long destination = Convert.ToInt64(message["destination"].Value);
			string content = String.Empty;
			if(message.ContainsKey("content"))
				content = (string)message["content"].Value;

			// insert into message table
			using(IDbCommand dbcmd = dbcon.CreateCommand()) {
				dbcmd.Transaction = transaction;
				dbcmd.CommandText = "INSERT INTO message (type,origin_id,destination_id,content,create_date) VALUES ('"+type.Replace("'","''")+"',"+origin+","+destination+",'"+content.Replace("'","''")+"',datetime('now'))";
				int count = dbcmd.ExecuteNonQuery();
				if(count != 1)
					throw new Exception("Create message fails");
			}
					
			// get the insert id
			long msgId = -1;
			using(IDbCommand dbcmd = dbcon.CreateCommand()) {
				dbcmd.Transaction = transaction;
				dbcmd.CommandText = "SELECT last_insert_rowid()";
				msgId = Convert.ToInt64(dbcmd.ExecuteScalar());
			}
			return msgId;
		}
	
		public JsonArray SearchMessages(long user, long? with, int limit)
		{
			limit = Math.Min(1000, Math.Max(0, limit));

			string withFilter = "";
			if(with != null)
				withFilter = " AND (destination_id="+(long)with+" OR origin_id="+(long)with+") ";

			JsonArray messages = new JsonArray();

			lock(dbcon) {
				// select from the message table
				using(IDbCommand dbcmd = dbcon.CreateCommand()) {
					dbcmd.CommandText = "SELECT id,origin_id,destination_id,content,strftime('%s',create_date),strftime('%s',seen_date),type FROM message WHERE (destination_id="+user+" OR origin_id="+user+") "+withFilter+" ORDER BY id DESC LIMIT "+limit;
					using(IDataReader reader = dbcmd.ExecuteReader()) {
						while(reader.Read()) {
							JsonValue message = new JsonObject();
							messages.Add(message);
							message["id"] = reader.GetInt64(0);
							message["origin"] = reader.GetInt64(1);
							message["destination"] = reader.GetInt64(2);
							message["content"] = reader.GetString(3);
							message["create"] = Convert.ToInt64(reader.GetString(4));
							if(reader.IsDBNull(5))
								message["seen"] = -1;
							else
								message["seen"] = Convert.ToInt64(reader.GetString(5));
							if(reader.IsDBNull(6))
								message["type"] = null;
							else
								message["type"] = reader.GetString(6);
						}
						// clean up
						reader.Close();
					}
				}						
			}
			return messages;
		}


		public JsonValue MarkSeenMessage(long id)
		{
			JsonValue json;
			lock(dbcon) {
				using (IDbTransaction transaction = dbcon.BeginTransaction()) {

					// insert into message table
					using (IDbCommand dbcmd = dbcon.CreateCommand()) {
						dbcmd.CommandText = "UPDATE message SET seen_date=DATETIME('now') WHERE id=" + id;
						dbcmd.Transaction = transaction;
						dbcmd.ExecuteNonQuery();
					}
					json = GetMessage(dbcon, transaction, id);
					transaction.Commit();
				}
			}
			if(json != null) {
				// signal the changed message to the watched users
				long origin = (long)json["origin"];
				long destination = (long)json["destination"];
				JsonValue messageJson = new JsonObject();
				messageJson["event"] = "messagechanged";
				messageJson["message"] = json;
				string jsonString = messageJson.ToString();
				lock(instanceLock) {
					if(clients.ContainsKey(origin))
						clients[origin].Broadcast(jsonString);
					if(clients.ContainsKey(destination))
						clients[destination].Broadcast(jsonString);
				}
				RaisesMessageChanged(json);
			}
			return json;
		}

		public bool GetIsUserWatched(long user)
		{
			bool res;
			lock(instanceLock) {
				res = clients.ContainsKey(user);
			}
			return res;
		}

		public override async Task ProcessRequestAsync(HttpContext context)
		{
			string[] parts = context.Request.Path.Split(new char[] { '/' }, System.StringSplitOptions.RemoveEmptyEntries);
			long id = 0;

			// WS /[id] monitor a user messages
			if((context.Request.IsWebSocketRequest) && (parts.Length == 1) && (long.TryParse(parts[0], out id))) {
				MessageClient client = new MessageClient(this, id);
				await context.AcceptWebSocketRequestAsync(client);
				//
			}
			// GET /[id] get a message
			else if((context.Request.Method == "GET") && (parts.Length == 1) && (long.TryParse(parts[0], out id))) {
				JsonValue json = GetMessage(id);
				context.Response.Headers["cache-control"] = "no-cache, must-revalidate";
				if(json == null) {
					context.Response.StatusCode = 404;
				}
				else {
					context.Response.StatusCode = 200;
					context.Response.Content = new JsonContent(json);
				}
			}
			// GET /?user=[user_id]&with=[user_id]&limit=[limit]
			else if((context.Request.Method == "GET") && (context.Request.Path == "/") && context.Request.QueryString.ContainsKey("user")) {
				long user = Convert.ToInt64(context.Request.QueryString["user"]);
				long? with = null;
				if(context.Request.QueryString.ContainsKey("with"))
					with = Convert.ToInt64(context.Request.QueryString["with"]);
				int limit = 1000;
				if(context.Request.QueryString.ContainsKey("limit"))
					limit = Convert.ToInt32(context.Request.QueryString["limit"]);
				context.Response.StatusCode = 200;
				context.Response.Headers["cache-control"] = "no-cache, must-revalidate";
				context.Response.Content = new JsonContent(SearchMessages(user, with, limit));
			}
			// POST /?destination=[destination]&origin=[origin]
			// content: { destination: [destination], origin: [origin], type: [type], content: [content] }
			else if((context.Request.Method == "POST") && (context.Request.Path == "/")) {
				JsonValue json = context.Request.ReadAsJson();

				if(context.Request.QueryString.ContainsKey("destination"))
					json["destination"] = Convert.ToInt64(context.Request.QueryString["destination"]);
				if(context.Request.QueryString.ContainsKey("origin"))
					json["origin"] = Convert.ToInt64(context.Request.QueryString["origin"]);
				if(!json.ContainsKey("type"))
					json["type"] = "message";

				context.Response.StatusCode = 200;
				context.Response.Headers["cache-control"] = "no-cache, must-revalidate";
				context.Response.Content = new JsonContent(SendMessage(json));
			}
			// PUT /[id]
			else if((context.Request.Method == "PUT") && (parts.Length == 1) && (long.TryParse(parts[0], out id))) {
				context.Response.Headers["cache-control"] = "no-cache, must-revalidate";
				JsonValue json = MarkSeenMessage(id);
				if(json == null)
					context.Response.StatusCode = 404;
				else {
					context.Response.StatusCode = 200;
					context.Response.Content = new JsonContent(json);
				}
			}
			// DELETE /[id] delete a message
			else if((context.Request.Method == "DELETE") && (parts.Length == 1) && (long.TryParse(parts[0], out id))) {
				context.Response.Headers["cache-control"] = "no-cache, must-revalidate";
				if(DeleteMessage(id))
					context.Response.StatusCode = 200;
				else
					context.Response.StatusCode = 404;
			}
		}

		public void Dispose()
		{
			if(dbcon != null) {
				dbcon.Close();
				dbcon.Dispose();
				dbcon = null;
			}
		}
	}
}
