// AuthSession.cs
// 
//  The AuthSession keep the users authorizations a given time
//
// Author(s):
//  Daniel Lacroix <dlacroix@erasme.org>
// 
// Copyright (c) 2011-2014 Departement du Rhone
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
using System.Collections;
using System.Collections.Generic;
using Mono.Data.Sqlite;
using Erasme;
using Erasme.Http;
using Erasme.Json;

namespace Erasme.Cloud.Authentication
{
	public class AuthSessionService: HttpHandler, IDisposable
	{
		string headerKey;
		string cookieKey;
		double sessionTimeout;

		object instanceLock = new object();
		IDbConnection dbcon;
		DateTime lastClean = DateTime.MinValue;

		public AuthSessionService(string basepath, double timeout, string headerKey, string cookieKey)
		{
			sessionTimeout = timeout;
			this.headerKey = headerKey.ToLower();
			this.cookieKey = cookieKey;
			
			if(!Directory.Exists(basepath))
				Directory.CreateDirectory(basepath);

			bool createNeeded = !File.Exists(basepath+"sessions.db");

			dbcon = (IDbConnection)new SqliteConnection("URI=file:"+basepath+"sessions.db");
			dbcon.Open();

			if(createNeeded) {
				// create the session table
				using(IDbCommand dbcmd = dbcon.CreateCommand()) {
					dbcmd.CommandText = "CREATE TABLE session (id VARCHAR PRIMARY KEY, user VARCHAR, start INTEGER, last INTEGER, permanent INTEGER(1) DEFAULT 0)";
					dbcmd.ExecuteNonQuery();
				}
			}
			// disable disk sync. AuthSession are not critical
			using(IDbCommand dbcmd = dbcon.CreateCommand()) {
				dbcmd.CommandText = "PRAGMA synchronous=0";
				dbcmd.ExecuteNonQuery();
			}

			Rights = new DummyAuthSessionRights();
		}

		public IAuthSessionRights Rights { get; set; }

		void CleanSessions()
		{
			DateTime now = DateTime.Now;
			lock(instanceLock) {
				TimeSpan delta = now - lastClean;
				if(delta.TotalSeconds > sessionTimeout) {
					// delete old sessions
					using(IDbCommand dbcmd = dbcon.CreateCommand()) {
						dbcmd.CommandText = "DELETE FROM session WHERE last < DATETIME('now','-"+Convert.ToUInt64(sessionTimeout)+" second') AND permanent=0";
						dbcmd.ExecuteNonQuery();
					}
					lastClean = now;
				}
			}
		}

		public JsonValue Get(string session)
		{
			JsonValue res = null;
			CleanSessions();

			lock(instanceLock) {
				using(IDbTransaction transaction = dbcon.BeginTransaction()) {
					res = Get(dbcon, transaction, session, true);
					transaction.Commit();
				}
			}
			return res;
		}
		
		JsonValue Get(IDbConnection dbcon, IDbTransaction transaction, string session, bool updateLast)
		{
			JsonValue res = new JsonObject();
			res["id"] = session;
			string user = null;
			double deltaSec = 0;
			// get the session
			using(IDbCommand dbcmd = dbcon.CreateCommand()) {
				dbcmd.Transaction = transaction;
				dbcmd.CommandText = "SELECT user,(julianday(datetime('now'))-julianday(last))*24*3600 FROM session WHERE id=@id";
				dbcmd.Parameters.Add(new SqliteParameter("id", session));
				using(IDataReader reader = dbcmd.ExecuteReader()) {
					if(!reader.Read())
						return null;
					user = reader.GetString(0);
					deltaSec = reader.GetDouble(1);
					reader.Close();
				}
			}
			res["user"] = user;

			// only update if last time is at least a quarter of the timeout
			// dont do it all the time, because data base updates are slow
			if(updateLast && (deltaSec > sessionTimeout / 4)) {
				// update the last time seen
				using(IDbCommand dbcmd = dbcon.CreateCommand()) {
					dbcmd.Transaction = transaction;
					dbcmd.CommandText = "UPDATE session SET last=DATETIME('now') WHERE id=@id";
					dbcmd.Parameters.Add(new SqliteParameter("id", session));
					dbcmd.ExecuteNonQuery();
				}
			}
			return res;
		}

		public JsonValue Create(string user)
		{
			return Create(user, false);
		}

		public JsonValue Create(string user, bool permanent)
		{
			JsonValue session = null;
			string id;
			lock(instanceLock) {
				using(IDbTransaction transaction = dbcon.BeginTransaction()) {
					int count = 0;
					// generate the random session id
					do {
						string randchars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
						Random rand = new Random();
						StringBuilder sb = new StringBuilder();
						for(int i = 0; i < 10; i++)
							sb.Append(randchars[rand.Next(randchars.Length)]);
						id = sb.ToString();
						
						// check if session already exists
						using(IDbCommand dbcmd = dbcon.CreateCommand()) {
							dbcmd.Transaction = transaction;
							dbcmd.CommandText = "SELECT COUNT(id) FROM session WHERE id=@id";
							dbcmd.Parameters.Add(new SqliteParameter("id", id));
							count = Convert.ToInt32(dbcmd.ExecuteScalar());
						}
					} while(count > 0);

					// insert the new session
					using(IDbCommand dbcmd = dbcon.CreateCommand()) {
						dbcmd.Transaction = transaction;
						dbcmd.CommandText = "INSERT INTO session (id,user,start,last,permanent) " +
							"VALUES (@id,@user,DATETIME('now'),DATETIME('now'),@permanent)";
						dbcmd.Parameters.Add(new SqliteParameter("id", id));
						dbcmd.Parameters.Add(new SqliteParameter("user", user));
						dbcmd.Parameters.Add(new SqliteParameter("permanent", (permanent?1:0)));
						dbcmd.ExecuteNonQuery();
					}
					session = Get(dbcon, transaction, id, false);
					transaction.Commit();
				}
			}
			return session;
		}
				
		public void Delete(string session)
		{
			lock(instanceLock) {
				// delete old sessions
				using(IDbCommand dbcmd = dbcon.CreateCommand()) {
					dbcmd.CommandText = "DELETE FROM session WHERE id=@id";
					dbcmd.Parameters.Add(new SqliteParameter("id", session));
					dbcmd.ExecuteNonQuery();
				}
			}
		}

		JsonArray SearchSessions(bool? permanent, int limit)
		{
			JsonArray sessions = new JsonArray();
			lock(instanceLock) {
				using(IDbTransaction transaction = dbcon.BeginTransaction()) {
					// select from the session table
					using(IDbCommand dbcmd = dbcon.CreateCommand()) {
						dbcmd.Transaction = transaction;
						
						string filter = "";
						if(permanent != null)
							filter = "permanent=" + (((bool)permanent)?"1":"0");
						if(filter != "")
							filter = "WHERE " + filter;
						dbcmd.CommandText = "SELECT id FROM session " + filter + " LIMIT " + limit;
					
						using(IDataReader reader = dbcmd.ExecuteReader()) {
							while(reader.Read())
								sessions.Add(Get(dbcon, transaction, reader.GetString(0), false));
							// clean up
							reader.Close();
						}
					}
					transaction.Commit();
				}
			}
			return sessions;
		}

		static object AuthenticatedUserKey = new object();

		public string GetAuthenticatedUser(HttpContext context)
		{
			if(!context.Data.ContainsKey(AuthenticatedUserKey)) {
				string sessionId = null;
				if(context.Request.Headers.ContainsKey(headerKey))
					sessionId = context.Request.Headers[headerKey];
				else if(context.Request.Cookies.ContainsKey(cookieKey))
					sessionId = context.Request.Cookies[cookieKey];
				string user = null;
				if(sessionId != null) {
					JsonValue session = Get(sessionId);
					if(session != null)
						user = (string)session["user"];
				}
				context.Data[AuthenticatedUserKey] = user;
				if(user != null)
					context.User = user;
			}
			return (string)context.Data[AuthenticatedUserKey];
		}

		public override void ProcessRequest(HttpContext context)
		{
			string[] parts = context.Request.Path.Split(new char[] { '/' }, System.StringSplitOptions.RemoveEmptyEntries);

			// POST / create a new session
			if((context.Request.Method == "POST") && (parts.Length == 0)) {
				JsonValue json = context.Request.ReadAsJson();

				string user = json["user"];

				Rights.EnsureCanCreateSession(context, user);

				bool permanent = false;
				if(json.ContainsKey("permanent"))
					permanent = json["permanent"];

				context.Response.StatusCode = 200;
				context.Response.Headers["cache-control"] = "no-cache, must-revalidate";
				context.Response.Content = new JsonContent(Create(user, permanent));
			}
			// GET /?permanent=[true|false]&limit=100 search for sessions
			else if((context.Request.Method == "GET") && (parts.Length == 0)) {

				Rights.EnsureCanSearchSessions(context);

				bool? permanent = null;
				if(context.Request.QueryString.ContainsKey("permanent"))
					permanent = (context.Request.QueryString["permanent"].ToLower() == "true");
				int limit = 200;
				if(context.Request.QueryString.ContainsKey("limit"))
					limit = Math.Max(0, Math.Min(1000, Convert.ToInt32(context.Request.QueryString["limit"])));

				context.Response.StatusCode = 200;
				context.Response.Headers["cache-control"] = "no-cache, must-revalidate";
				context.Response.Content = new JsonContent(SearchSessions(permanent, limit));
			}
			// GET /current get the current session
			else if((context.Request.Method == "GET") && (parts.Length == 1) && (parts[0] == "current")) {
				string sessionId = null;
				if(context.Request.Headers.ContainsKey(headerKey))
					sessionId = context.Request.Headers[headerKey];
				else if(context.Request.Cookies.ContainsKey(cookieKey))
					sessionId = context.Request.Cookies[cookieKey];
				if(sessionId == null) {
					context.Response.StatusCode = 400;
					context.Response.Headers["cache-control"] = "no-cache, must-revalidate";
					context.Response.Content = new StringContent("No session given to search for");
				}
				else {
					JsonValue session = Get(sessionId);
					if(session == null) {
						context.Response.StatusCode = 404;
						context.Response.Headers["cache-control"] = "no-cache, must-revalidate";
						context.Response.Content = new StringContent("Session '"+sessionId+"' not found or expired");
					}
					else {
						Rights.EnsureCanReadSession(context, sessionId, session["user"]);

						if(context.Request.QueryString.ContainsKey("setcookie"))
							context.Response.Headers["set-cookie"] = cookieKey+"="+sessionId+"; Path=/";
						context.Response.StatusCode = 200;
						context.Response.Headers["cache-control"] = "no-cache, must-revalidate";
						context.Response.Content = new JsonContent(session);
					}
				}
			}
			// GET /[session] get a session
			else if((context.Request.Method == "GET") && (parts.Length == 1)) {
				string sessionId = parts[0];
				JsonValue session = Get(sessionId);
				if(session == null) {
					context.Response.StatusCode = 404;
				}
				else {
					Rights.EnsureCanReadSession(context, sessionId, session["user"]);

					if(context.Request.QueryString.ContainsKey("setcookie"))
						context.Response.Headers["set-cookie"] = cookieKey+"="+sessionId+"; Path=/";
					context.Response.StatusCode = 200;
					context.Response.Headers["cache-control"] = "no-cache, must-revalidate";
					context.Response.Content = new JsonContent(session);
				}
			}
			// DELETE /current delete current context session
			else if((context.Request.Method == "DELETE") && (parts.Length == 1) && (parts[0] == "current")) {
				string sessionId = null;
				if(context.Request.Headers.ContainsKey(headerKey.ToLower()))
					sessionId = context.Request.Headers[headerKey.ToLower()];
				else if(context.Request.Cookies.ContainsKey(cookieKey))
					sessionId = context.Request.Cookies[cookieKey];
				if(sessionId == null) {
					context.Response.StatusCode = 404;
					context.Response.Headers["cache-control"] = "no-cache, must-revalidate";
				}
				else {
					JsonValue session = Get(sessionId);
					Rights.EnsureCanDeleteSession(context, sessionId, session["user"]);

					Delete(sessionId);
					context.Response.StatusCode = 200;
					context.Response.Headers["cache-control"] = "no-cache, must-revalidate";
					if(context.Request.QueryString.ContainsKey("setcookie"))
						context.Response.Headers["set-cookie"] = cookieKey+"=; expires=Thu, 01-Jan-1970 00:00:01 GMT; Path=/";
				}
			}
			// DELETE /[session] delete a session
			else if((context.Request.Method == "DELETE") && (parts.Length == 1)) {

				JsonValue session = Get(parts[0]);
				Rights.EnsureCanDeleteSession(context, parts[0], session["user"]);

				Delete(parts[0]);
				context.Response.StatusCode = 200;
				context.Response.Headers["cache-control"] = "no-cache, must-revalidate";
				if(context.Request.QueryString.ContainsKey("setcookie"))
					context.Response.Headers["set-cookie"] = cookieKey+"=; expires=Thu, 01-Jan-1970 00:00:01 GMT; Path=/";
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
