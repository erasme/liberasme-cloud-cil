// MessageService.cs
// 
//  Service to send/receive messages from/to users
//
// Author(s):
//  Daniel Lacroix <dlacroix@erasme.org>
// 
// Copyright (c) 2012-2014 Departement du Rhone
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
			public MessageClient(MessageService service, string user)
			{
				Service = service;
				User = user;								
			}

			public string User { get; private set; }
			
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
		Dictionary<string, WebSocketHandlerCollection<MessageClient>> clients = new Dictionary<string, WebSocketHandlerCollection<MessageClient>>();

		IDbConnection dbcon;

		public MessageService(string basepath)
		{
			basePath = basepath;

			if(!Directory.Exists(basepath))
				Directory.CreateDirectory(basepath);

			bool createNeeded = !File.Exists(basepath+"messages.db");

			dbcon = (IDbConnection)new SqliteConnection("URI=file:"+this.basePath+"messages.db");
			dbcon.Open();

			if(createNeeded) {

				// create the message table
				using(IDbCommand dbcmd = dbcon.CreateCommand()) {
					dbcmd.CommandText = "CREATE TABLE message (id INTEGER PRIMARY KEY AUTOINCREMENT, origin_id VARCHAR, destination_id VARCHAR, content VARCHAR DEFAULT NULL, create_date INTEGER, seen_date INTEGER, type VARCHAR DEFAULT NULL)";
					dbcmd.ExecuteNonQuery();
				}
			}

			// disable disk sync.
			using(IDbCommand dbcmd = dbcon.CreateCommand()) {
				dbcmd.CommandText = "PRAGMA synchronous=0";
				dbcmd.ExecuteNonQuery();
			}
			Rights = new DummyMessageRights();
		}

		public IMessageRights Rights { get; set; }

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

		public delegate void UserWatchedEventHandler(string user);
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
		internal void RaisesUserWatched(string user)
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
		internal void RaisesUserUnwatched(string user)
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
					dbcmd.CommandText = "DELETE FROM message WHERE id=@id";
					dbcmd.Parameters.Add(new SqliteParameter("id", id));
					done = (dbcmd.ExecuteNonQuery() == 1);
				}
			}
			return done;
		}

		public void DeleteUserMessages(string user)
		{
			lock(dbcon) {
				// delete messages
				using(IDbCommand dbcmd = dbcon.CreateCommand()) {
					dbcmd.CommandText = "DELETE FROM message WHERE origin_id=@user OR destination_id=@user";
					dbcmd.Parameters.Add(new SqliteParameter("user", user));
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
					message["origin"] = reader.GetString(2);
					message["destination"] = reader.GetString(3);
					if(reader.IsDBNull(4))
						message["content"] = null;
					else
						message["content"] = JsonValue.Parse(reader.GetString(4));
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
			string origin;
			string destination;
			JsonValue json;
			if(message.ContainsKey("persist") && (message["persist"].Value is Boolean) && ((bool)message["persist"] == false)) {
				json = new JsonObject();
				string type = "message";
				if(message.ContainsKey("type"))
					type = (string)message["type"].Value;
				origin = (string)message["origin"].Value;
				destination = (string)message["destination"].Value;
				string content = null;
				if(message.ContainsKey("content"))
					content = message["content"];
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
				origin = (string)json["origin"];
				destination = (string)json["destination"];
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
			string origin = (string)message["origin"].Value;
			string destination = (string)message["destination"].Value;
			string content = null;
			if(message.ContainsKey("content"))
				content = message["content"].ToString();

			// insert into message table
			using(IDbCommand dbcmd = dbcon.CreateCommand()) {
				dbcmd.Transaction = transaction;
				dbcmd.CommandText = 
					"INSERT INTO message (type,origin_id,destination_id,content,create_date) VALUES "+
					"(@type,@origin,@destination,@content,datetime('now'))";
				dbcmd.Parameters.Add(new SqliteParameter("type", type));
				dbcmd.Parameters.Add(new SqliteParameter("origin", origin));
				dbcmd.Parameters.Add(new SqliteParameter("destination", destination));
				dbcmd.Parameters.Add(new SqliteParameter("content", content));
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
	
		public JsonArray SearchMessages(string user, string with, int limit)
		{
			limit = Math.Min(1000, Math.Max(0, limit));

			string withFilter = "";
			if(with != null)
				withFilter = " AND (destination_id=@with OR origin_id=@with) ";

			JsonArray messages = new JsonArray();

			lock(dbcon) {
				// select from the message table
				using(IDbCommand dbcmd = dbcon.CreateCommand()) {
					dbcmd.CommandText =
						"SELECT id,origin_id,destination_id,content,strftime('%s',create_date),strftime('%s',seen_date),type "+
						"FROM message WHERE (destination_id=@user OR origin_id=@user) "+withFilter+" ORDER BY id DESC LIMIT @limit";
					dbcmd.Parameters.Add(new SqliteParameter("user", user));
					dbcmd.Parameters.Add(new SqliteParameter("limit", limit));
					dbcmd.Parameters.Add(new SqliteParameter("with", with));

					using(IDataReader reader = dbcmd.ExecuteReader()) {
						while(reader.Read()) {
							JsonValue message = new JsonObject();
							messages.Add(message);
							message["id"] = reader.GetInt64(0);
							message["origin"] = reader.GetString(1);
							message["destination"] = reader.GetString(2);
							if(reader.IsDBNull(3))
								message["content"] = null;
							else
								message["content"] = JsonValue.Parse(reader.GetString(3));
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
						dbcmd.CommandText = "UPDATE message SET seen_date=DATETIME('now') WHERE id=@id";
						dbcmd.Parameters.Add(new SqliteParameter("id", id));
						dbcmd.Transaction = transaction;
						dbcmd.ExecuteNonQuery();
					}
					json = GetMessage(dbcon, transaction, id);
					transaction.Commit();
				}
			}
			if(json != null) {
				// signal the changed message to the watched users
				string origin = (string)json["origin"];
				string destination = (string)json["destination"];
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

		public bool GetIsUserWatched(string user)
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
			if((context.Request.IsWebSocketRequest) && (parts.Length == 1)) {
				Rights.EnsureCanMonitorUser(context, parts[0]);

				string user = parts[0];
				MessageClient client = new MessageClient(this, user);
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
					Rights.EnsureCanReadMessage(context, json["origin"], json["destination"]);
					context.Response.StatusCode = 200;
					context.Response.Content = new JsonContent(json);
				}
			}
			// GET /?user=[user_id]&with=[user_id]&limit=[limit]
			else if((context.Request.Method == "GET") && (context.Request.Path == "/") && context.Request.QueryString.ContainsKey("user")) {
				string user = context.Request.QueryString["user"];
				string with = null;
				if(context.Request.QueryString.ContainsKey("with"))
					with = context.Request.QueryString["with"];

				Rights.EnsureCanReadMessage(context, user, with);
				Rights.EnsureCanReadMessage(context, with, user);

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
					json["destination"] = context.Request.QueryString["destination"];
				if(context.Request.QueryString.ContainsKey("origin"))
					json["origin"] = context.Request.QueryString["origin"];
				if(!json.ContainsKey("type"))
					json["type"] = "message";
				Rights.EnsureCanCreateMessage(context, json["origin"], json["destination"]);

				context.Response.StatusCode = 200;
				context.Response.Headers["cache-control"] = "no-cache, must-revalidate";
				context.Response.Content = new JsonContent(SendMessage(json));
			}
			// PUT /[id]
			else if((context.Request.Method == "PUT") && (parts.Length == 1) && (long.TryParse(parts[0], out id))) {
				JsonValue json = GetMessage(id);
				Rights.EnsureCanUpdateMessage(context, json["origin"], json["destination"]);

				context.Response.Headers["cache-control"] = "no-cache, must-revalidate";
				json = MarkSeenMessage(id);
				if(json == null)
					context.Response.StatusCode = 404;
				else {
					context.Response.StatusCode = 200;
					context.Response.Content = new JsonContent(json);
				}
			}
			// DELETE /[id] delete a message
			else if((context.Request.Method == "DELETE") && (parts.Length == 1) && (long.TryParse(parts[0], out id))) {
				JsonValue json = GetMessage(id);
				Rights.EnsureCanDeleteMessage(context, json["origin"], json["destination"]);

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
