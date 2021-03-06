// StorageService.cs
// 
//  Provide a file storage system
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
using Mono.Data.Sqlite;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Erasme.Http;
using Erasme.Json;
using Erasme.Cloud.Logger;

namespace Erasme.Cloud.Storage
{
	public class StorageService: IHttpHandler, IDisposable
	{
		object instanceLock = new object();
		Dictionary<string,WebSocketHandlerCollection<MonitorClient>> clients = new Dictionary<string,WebSocketHandlerCollection<MonitorClient>>();
		Dictionary<string,List<IStoragePlugin>> mimePlugins = new Dictionary<string,List<IStoragePlugin>>();

		class MonitorClient: WebSocketHandler
		{
			string storage;

			public MonitorClient(StorageService service, string storage)
			{
				Service = service;
				this.storage = storage;
			}

			public StorageService Service { get; private set; }
			
			public string Storage
			{
				get {
					return storage;
				}
			}

			public override void OnOpen()
			{
				lock(Service.instanceLock) {
					WebSocketHandlerCollection<MonitorClient> channelClients;
					if(Service.clients.ContainsKey(storage)) {
						channelClients = Service.clients[storage];
					}
					else {
						channelClients = new WebSocketHandlerCollection<MonitorClient>();
						Service.clients[storage] = channelClients;
					}
					channelClients.Add(this);
				}

				long quota, used, ctime, mtime, rev;
				Service.GetStorageInfo(storage, out quota, out used, out ctime, out mtime, out rev);

				JsonValue json = new JsonObject();
				json["storage"] = Storage;
				json["action"] = "open";
				json["rev"] = rev;
				Send(json.ToString());
			}

			public override void OnError()
			{
			}

			public override void OnClose()
			{
				lock(Service.instanceLock) {
					if(Service.clients.ContainsKey(Storage)) {
						WebSocketHandlerCollection<MonitorClient> channelClients;
						channelClients = Service.clients[Storage];
						channelClients.Remove(this);
						// remove the channel is empty
						if(channelClients.Count == 0)
							Service.clients.Remove(Storage);
					}
				}
			}
		}

		public class StorageFile
		{
			StorageService service;
			IDbConnection dbcon;
			IDbTransaction transaction;
			string storage;
			JsonValue data;
			Dictionary<string,string> cache = null;

			internal StorageFile(StorageService service, IDbConnection dbcon, IDbTransaction transaction, string storage, JsonValue data)
			{
				this.service = service;
				this.dbcon = dbcon;
				this.transaction = transaction;
				this.storage = storage;
				this.data = data;
			}

			public JsonValue Data {
				get {
					return data;
				}
			}

			public string GetCacheString(string key)
			{
				if((cache != null) && (cache.ContainsKey(key)))
					return cache[key];
				else if(this.data["cache"].ContainsKey(key))
					return this.data["cache"][key];
				else
					return null;
			}

			public void SetCacheString(string key, string value)
			{
				if(cache == null)
					cache = new Dictionary<string,string>();
				cache[key] = value;
			}

			internal void UpdateCache()
			{
				if(cache != null) {
					foreach(string key in cache.Keys) {
						string value = (string)cache[key];
						if(value == null) {
							using(IDbCommand dbcmd = dbcon.CreateCommand()) {
								dbcmd.Transaction = transaction;
								dbcmd.CommandText = "DELETE FROM cache  WHERE owner_id=@file AND key=@key";
								dbcmd.Parameters.Add(new SqliteParameter("file", (long)data["id"]));
								dbcmd.Parameters.Add(new SqliteParameter("key", key));
								dbcmd.ExecuteNonQuery();
							}
						}
						else {
							using(IDbCommand dbcmd = dbcon.CreateCommand()) {
								dbcmd.Transaction = transaction;
								dbcmd.CommandText = "REPLACE INTO cache (owner_id,key,value) VALUES (@file,@key,@value)";
								dbcmd.Parameters.Add(new SqliteParameter("file", (long)data["id"]));
								dbcmd.Parameters.Add(new SqliteParameter("key", key));
								dbcmd.Parameters.Add(new SqliteParameter("value", value));
								dbcmd.ExecuteNonQuery();
							}
						}
					}
				}
			}
		}

		string basePath;
		string temporaryDirectory;
		int cacheDuration;
		ILogger logger;

		IDbConnection dbcon;

		public StorageService(string basePath, string temporaryDirectory, int cacheDuration, ILogger logger)
		{
			this.basePath = basePath;
			this.temporaryDirectory = temporaryDirectory;
			this.cacheDuration = cacheDuration;
			this.logger = logger;

			if(!Directory.Exists(basePath))
				Directory.CreateDirectory(basePath);

			bool createNeeded = !File.Exists(basePath+"storages.db");

			dbcon = (IDbConnection) new SqliteConnection("URI=file:"+this.basePath+"storages.db");
			dbcon.Open();

			if(createNeeded) {			
				// create the storage table
				using(IDbCommand dbcmd = dbcon.CreateCommand()) {
					dbcmd.CommandText = "CREATE TABLE storage (id VARCHAR PRIMARY KEY, quota INTEGER DEFAULT 0, used INTEGER DEFAULT 0, ctime INTEGER, mtime INTEGER, rev INTEGER DEFAULT 0)";
					dbcmd.ExecuteNonQuery();
				}

				// create the file table
				using(IDbCommand dbcmd = dbcon.CreateCommand()) {
					dbcmd.CommandText = "CREATE TABLE file (id INTEGER PRIMARY KEY AUTOINCREMENT, storage_id VARCHAR, parent_id INTEGER DEFAULT 0, name VARCHAR, mimetype VARCHAR, ctime INTEGER, mtime INTEGER, rev INTEGER DEFAULT 0, size INTEGER DEFAULT 0, position INTEGER DEFAULT 0)";
					dbcmd.ExecuteNonQuery();
				}

				// create the meta table (description fields attached to files)
				using(IDbCommand dbcmd = dbcon.CreateCommand()) {
					dbcmd.CommandText = "CREATE TABLE meta (owner_id INTEGER, key VARCHAR, value VARCHAR)";
					dbcmd.ExecuteNonQuery();
				}

				// create the cache table (description fields attached to files)
				using(IDbCommand dbcmd = dbcon.CreateCommand()) {
					dbcmd.CommandText = "CREATE TABLE cache (owner_id INTEGER, key VARCHAR, value VARCHAR)";
					dbcmd.ExecuteNonQuery();
				}

				// create the comment table (user comments attached to files)
				using(IDbCommand dbcmd = dbcon.CreateCommand()) {
					dbcmd.CommandText = "CREATE TABLE comment "+
						"(id INTEGER PRIMARY KEY AUTOINCREMENT, file_id INTEGER, "+
						"user_id VARCHAR, content VARCHAR, ctime INTEGER, mtime INTEGER)";
					dbcmd.ExecuteNonQuery();
				}
			}
			// disable disk sync. AuthSession are not critical
			using(IDbCommand dbcmd = dbcon.CreateCommand()) {
				dbcmd.CommandText = "PRAGMA synchronous=0";
				dbcmd.ExecuteNonQuery();
			}
			Rights = new DummyStorageRights();
		}

		public IStorageRights Rights { get; set; }

		public void AddPlugin(IStoragePlugin plugin)
		{
			foreach(string mimetype in plugin.MimeTypes) {
				List<IStoragePlugin> plugins;
				if(mimePlugins.ContainsKey(mimetype))
					plugins = mimePlugins[mimetype];
				else {
					plugins = new List<IStoragePlugin>();
					mimePlugins[mimetype] = plugins;
				}
				plugins.Add(plugin);
			}
		}

		void MonitorClientSignalChanged(string storage, long rev)
		{
			lock(clients) {
				if(clients.ContainsKey(storage)) {
					JsonValue json = new JsonObject();
					json["storage"] = storage;
					json["action"] = "changed";
					json["rev"] = rev;
					string jsonString = json.ToString();

					clients[storage].Broadcast(jsonString);
				}
			}
		}

		void MonitorClientSignalDeleted(string storage)
		{
			JsonValue json = new JsonObject();
			json["storage"] = storage;
			json["action"] = "deleted";
			string jsonString = json.ToString();
			lock(clients) {
				if(clients.ContainsKey(storage)) {
					clients[storage].Broadcast(jsonString);
				}
			}
		}

		public delegate void StorageCreatedEventHandler(string storage);
		List<StorageCreatedEventHandler> storageCreatedHandlers = new List<StorageCreatedEventHandler>();
		public event StorageCreatedEventHandler StorageCreated {
			add {
				lock(storageCreatedHandlers) {
					storageCreatedHandlers.Add(value);
				}
			}
			remove {
				lock(storageCreatedHandlers) {
					storageCreatedHandlers.Remove(value);
				}
			}
		}
		void RaisesStorageCreated(string storage)
		{
			List<StorageCreatedEventHandler> handlers;
			lock(storageCreatedHandlers) {
				handlers = new List<StorageCreatedEventHandler>(storageCreatedHandlers);
			}
			foreach(StorageCreatedEventHandler handler in handlers) {
				try {
					handler(storage);
				}
				catch(Exception e) {
					logger.Log(LogLevel.Error, "On StorageCreated handler fails ("+e.ToString()+")");
				}
			}
		}

		public string CreateStorage(long quota)
		{
			return CreateStorage(null, quota);
		}

		string GenerateRandomId(int size = 10)
		{
			// generate the random id
			string randchars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
			Random rand = new Random();
			StringBuilder sb = new StringBuilder(size);
			for(int i = 0; i < size; i++)
				sb.Append(randchars[rand.Next(randchars.Length)]);
			return sb.ToString();
		}

		/// <summary>
		/// Create a new storage. Return a unique identifier.
		/// </summary>
		public string CreateStorage(string id, long quota)
		{
			lock(dbcon) {
				using(IDbTransaction transaction = dbcon.BeginTransaction()) {
				
					if(id == null) {
						int count = 0;
						// generate the random resource id
						do {
							id = GenerateRandomId();
							// check if resource id already exists
							using(IDbCommand dbcmd = dbcon.CreateCommand()) {
								dbcmd.Transaction = transaction;
								dbcmd.CommandText = "SELECT COUNT(id) FROM storage WHERE id=@id";
								dbcmd.Parameters.Add(new SqliteParameter("id", id));
								count = Convert.ToInt32(dbcmd.ExecuteScalar());
							}
						} while(count > 0);
					}

					// insert into storage table
					using(IDbCommand dbcmd = dbcon.CreateCommand()) {
						dbcmd.Transaction = transaction;
						dbcmd.CommandText = "INSERT INTO storage (id,quota,used,ctime,mtime) VALUES (@id,@quota,0,datetime('now'),datetime('now'))";
						dbcmd.Parameters.Add(new SqliteParameter("id", id));
						dbcmd.Parameters.Add(new SqliteParameter("quota", quota));
						int count = dbcmd.ExecuteNonQuery();
						if(count != 1)
							throw new Exception("Create Storage fails");
					}

					// create the corresponding directory
					// to store files
					Directory.CreateDirectory(basePath+"/"+id);
					Directory.CreateDirectory(basePath+"/"+id+"/cache");

					// commit the transaction
					transaction.Commit();
				}
			}
			RaisesStorageCreated(id);
			return id;
		}
				
		public delegate void StorageDeletedEventHandler(string storage);
		List<StorageDeletedEventHandler> storageDeletedHandlers = new List<StorageDeletedEventHandler>();
		public event StorageDeletedEventHandler StorageDeleted {
			add {
				lock(storageDeletedHandlers) {
					storageDeletedHandlers.Add(value);
				}
			}
			remove {
				lock(storageDeletedHandlers) {
					storageDeletedHandlers.Remove(value);
				}
			}
		}
		void RaisesStorageDeleted(string storage)
		{
			List<StorageDeletedEventHandler> handlers;
			lock(storageDeletedHandlers) {
				handlers = new List<StorageDeletedEventHandler>(storageDeletedHandlers);
			}
			foreach(StorageDeletedEventHandler handler in handlers) {
				try {
					handler(storage);
				}
				catch(Exception e) {
					logger.Log(LogLevel.Error, "StorageDeleted handler fails ("+e.ToString()+")");
				}
			}
		}

		public void DeleteStorage(string storage)
		{
			lock(dbcon) {
				using(IDbTransaction transaction = dbcon.BeginTransaction()) {

					// delete all the meta
					using(IDbCommand dbcmd = dbcon.CreateCommand()) {
						dbcmd.Transaction = transaction;
						dbcmd.CommandText = "DELETE FROM meta WHERE owner_id IN (SELECT id FROM file WHERE storage_id=@storage)";
						dbcmd.Parameters.Add(new SqliteParameter("storage", storage));
						dbcmd.ExecuteNonQuery();
					}

					// delete all the comments
					using(IDbCommand dbcmd = dbcon.CreateCommand()) {
						dbcmd.Transaction = transaction;
						dbcmd.CommandText = "DELETE FROM comment WHERE file_id IN (SELECT id FROM file WHERE storage_id=@storage)";
						dbcmd.Parameters.Add(new SqliteParameter("storage", storage));
						dbcmd.ExecuteNonQuery();
					}
				
					// delete all the files
					using(IDbCommand dbcmd = dbcon.CreateCommand()) {
						dbcmd.Transaction = transaction;
						dbcmd.CommandText = "DELETE FROM file WHERE storage_id=@storage";
						dbcmd.Parameters.Add(new SqliteParameter("storage", storage));
						dbcmd.ExecuteNonQuery();
					}
				
					// delete from the storage table
					using(IDbCommand dbcmd = dbcon.CreateCommand()) {
						dbcmd.Transaction = transaction;
						dbcmd.CommandText = "DELETE FROM storage WHERE id=@storage";
						dbcmd.Parameters.Add(new SqliteParameter("storage", storage));
						int count = dbcmd.ExecuteNonQuery();
						if(count != 1)
							throw new Exception("Delete fails. Storage does not exists");
					}

					// delete the corresponding directory
					// that store files
					Directory.Delete(basePath+"/"+storage.ToString(), true);
				
					// commit the transaction
					transaction.Commit();
				}
			}
			MonitorClientSignalDeleted(storage);
			RaisesStorageDeleted(storage);
		}

		public delegate void StorageChangedEventHandler(string storage);
		List<StorageChangedEventHandler> storageChangedHandlers = new List<StorageChangedEventHandler>();
		public event StorageChangedEventHandler StorageChanged {
			add {
				lock(storageChangedHandlers) {
					storageChangedHandlers.Add(value);
				}
			}
			remove {
				lock(storageChangedHandlers) {
					storageChangedHandlers.Remove(value);
				}
			}
		}
		void RaisesStorageChanged(string storage)
		{
			List<StorageChangedEventHandler> handlers;
			lock(storageChangedHandlers) {
				handlers = new List<StorageChangedEventHandler>(storageChangedHandlers);
			}
			foreach(StorageChangedEventHandler handler in handlers) {
				try {
					handler(storage);
				}
				catch(Exception e) {
					logger.Log(LogLevel.Error, "On StorageChanged handler fails ("+e.ToString()+")");
				}
			}
		}

		public bool ChangeStorage(string storage, JsonValue diff)
		{
			if(!diff.ContainsKey("quota"))
				return false;
			
			long quota = (long)diff["quota"];
			long used = 0;
			long rev = 0;

			lock(dbcon) {
				using(IDbTransaction transaction = dbcon.BeginTransaction()) {
				
					// get quota and quota used
					using(IDbCommand dbcmd = dbcon.CreateCommand()) {
						dbcmd.Transaction = transaction;
						dbcmd.CommandText = "SELECT used,rev FROM storage WHERE id=@storage";
						dbcmd.Parameters.Add(new SqliteParameter("storage", storage));
						using(IDataReader reader = dbcmd.ExecuteReader()) {
							if(!reader.Read())
								return false;
							used = reader.GetInt64(0);
							rev = reader.GetInt64(1);
							reader.Close();
						}
					}
				
					if(quota != -1)
						quota = Math.Max(quota,used);
				
					// update quota and mtime
					using(IDbCommand dbcmd = dbcon.CreateCommand()) {
						dbcmd.Transaction = transaction;
						dbcmd.CommandText = "UPDATE storage SET quota=@quota,mtime=datetime('now'),rev=@rev WHERE id=@storage";
						dbcmd.Parameters.Add(new SqliteParameter("storage", storage));
						dbcmd.Parameters.Add(new SqliteParameter("quota", quota));
						dbcmd.Parameters.Add(new SqliteParameter("rev", rev+1));
						dbcmd.ExecuteNonQuery();
					}
				
					// commit the transaction
					transaction.Commit();
				}
			}
			MonitorClientSignalChanged(storage, rev + 1);
			RaisesStorageChanged(storage);
			return true;
		}

		public JsonValue GetStorageInfo(string storage)
		{
			long quota;
			long used;
			long ctime;
			long mtime;
			long rev;

			if(GetStorageInfo(storage, out quota, out used, out ctime, out mtime, out rev)) {
				JsonValue res = new JsonObject();
				res["id"] = storage;
				res["quota"] = quota;
				res["used"] = used;
				res["ctime"] = ctime;
				res["mtime"] = mtime;
				res["rev"] = rev;
				return res;
			}
			else {
				return null;
			}
		}
		
		public bool GetStorageInfo(string storage, out long quota, out long used, out long ctime, out long mtime, out long rev)
		{
			bool res;
			lock(dbcon) {
				using(IDbTransaction transaction = dbcon.BeginTransaction()) {
					res = GetStorageInfo(dbcon, transaction, storage, out quota, out used, out ctime, out mtime, out rev);
				}
			}
			return res;
		}

		bool GetStorageInfo(IDbConnection dbcon, IDbTransaction transaction, string storage, out long quota, out long used, out long ctime, out long mtime, out long rev)
		{
			// get the storage info
			using(IDbCommand dbcmd = dbcon.CreateCommand()) {
				dbcmd.Transaction = transaction;
				dbcmd.CommandText = "SELECT quota,used,strftime('%s',ctime),strftime('%s',mtime),rev FROM storage WHERE id=@storage";
				dbcmd.Parameters.Add(new SqliteParameter("storage", storage));
				using(IDataReader reader = dbcmd.ExecuteReader()) {
					if(!reader.Read()) {
						quota = 0;
						used = 0;
						ctime = 0;
						mtime = 0;
						rev = 0;
						return false;
					}
					quota = reader.GetInt64(0);
					used = reader.GetInt64(1);
					ctime = Convert.ToInt64(reader.GetString(2));
					mtime = Convert.ToInt64(reader.GetString(3));
					rev = reader.GetInt64(4);
					// clean up
					reader.Close();
				}
			}
			return true;
		}
		
		public delegate void FileCreatedEventHandler(string storage, long file);
		List<FileCreatedEventHandler> fileCreatedHandlers = new List<FileCreatedEventHandler>();
		public event FileCreatedEventHandler FileCreated {
			add {
				lock(fileCreatedHandlers) {
					fileCreatedHandlers.Add(value);
				}
			}
			remove {
				lock(fileCreatedHandlers) {
					fileCreatedHandlers.Remove(value);
				}
			}
		}
		void RaisesFileCreated(string storage, long file)
		{
			List<FileCreatedEventHandler> handlers;
			lock(fileCreatedHandlers) {
				handlers = new List<FileCreatedEventHandler>(fileCreatedHandlers);
			}
			foreach(FileCreatedEventHandler handler in handlers) {
				try {
					handler(storage, file);
				}
				catch(Exception e) {
					logger.Log(LogLevel.Error, "On FileCreated plugin handler fails ("+e.ToString()+")");
				}
			}
		}

		public long CreateFileFromUrl(string storage, long parent, string name, string mimetype, string downloadUrl, JsonValue define, bool signal)
		{
			string tmpFile = temporaryDirectory+"/"+Guid.NewGuid().ToString();

			using(FileStream fileStream = File.Create(tmpFile)) {
				// get the file
				using(WebRequest request = new WebRequest(downloadUrl, allowAutoRedirect: true)) {
					HttpClientResponse response = request.GetResponse();
					if(response.StatusCode != 200)
						throw new Exception("URL download fails HTTP (url: "+downloadUrl+", status: " + response.StatusCode + ")");
					if((mimetype == null) && response.Headers.ContainsKey("content-type"))
						mimetype = response.Headers["content-type"];
					if(name == null) {
						int slashPos = downloadUrl.LastIndexOf("/");
						if((slashPos > 0) && (slashPos < downloadUrl.Length - 1))
							name = downloadUrl.Substring(slashPos + 1);
						else
							name = "unknown";
					}
					if(mimetype == null)
						FileContent.MimeType(name);
					response.InputStream.CopyTo(fileStream);
				}
			}
			return CreateFile(storage, parent, name, mimetype, tmpFile, define, signal);
		}

		public long CreateFile(string storage, long parent, string name, string mimetype, string tmpFile, JsonValue define, bool signal)
		{
			long file;

			long size = 0;
			if(tmpFile != null) {
				FileInfo fileInfo = new FileInfo(tmpFile);
				size = fileInfo.Length;
			}

			long rev = 0;
			lock(dbcon) {
				using(IDbTransaction transaction = dbcon.BeginTransaction()) {
					long quota = 0;
					long used = 0;

					// get quota and quota used
					using(IDbCommand dbcmd = dbcon.CreateCommand()) {
						dbcmd.Transaction = transaction;
						dbcmd.CommandText = "SELECT quota,used,rev FROM storage WHERE id=@storage";
						dbcmd.Parameters.Add(new SqliteParameter("storage", storage));
						using(IDataReader reader = dbcmd.ExecuteReader()) {
							if(!reader.Read())
								throw new Exception("Create fails, storage not found");
							quota = reader.GetInt64(0);
							used = reader.GetInt64(1);
							rev = reader.GetInt64(2);
							reader.Close();
						}
					}

					// check the quota
					if((quota != -1) && (used + size > quota))
						throw new Exception("Cant create file, storage is over quota");

					long position = System.Int64.MaxValue / 4;
					if(define.ContainsKey("position"))
						position = Convert.ToInt64((double)define["position"]);

					// insert into file table
					using (IDbCommand dbcmd = dbcon.CreateCommand()) {
						dbcmd.Transaction = transaction;
						dbcmd.CommandText = "INSERT INTO file (storage_id,parent_id,mimetype,name,ctime,mtime,rev,size,position) "+
							"VALUES (@storage,@parent,@mimetype,@name,datetime('now'),datetime('now'),0,@size,@position)";
						dbcmd.Parameters.Add(new SqliteParameter("storage", storage));
						dbcmd.Parameters.Add(new SqliteParameter("parent", parent));
						dbcmd.Parameters.Add(new SqliteParameter("mimetype", mimetype));
						dbcmd.Parameters.Add(new SqliteParameter("name", name));
						dbcmd.Parameters.Add(new SqliteParameter("size", size));
						dbcmd.Parameters.Add(new SqliteParameter("position", position*2));

						if(dbcmd.ExecuteNonQuery() != 1)
							throw new Exception("File create fails");
					}

					// get the insert id
					using (IDbCommand dbcmd = dbcon.CreateCommand()) {
						dbcmd.Transaction = transaction;
						dbcmd.CommandText = "SELECT last_insert_rowid()";
						file = Convert.ToInt64(dbcmd.ExecuteScalar());
					}

					// create the meta
					if(define.ContainsKey("meta")) {
						JsonObject meta = (JsonObject)define["meta"];

						foreach(string key in meta.Keys) {
							string value = (string)meta[key];
							using(IDbCommand dbcmd = dbcon.CreateCommand()) {
								dbcmd.Transaction = transaction;
								dbcmd.CommandText = "INSERT INTO meta (owner_id,key,value) VALUES (@file,@key,@value)";
								dbcmd.Parameters.Add(new SqliteParameter("file", file));
								dbcmd.Parameters.Add(new SqliteParameter("key", key));
								dbcmd.Parameters.Add(new SqliteParameter("value", value));
								dbcmd.ExecuteNonQuery();
							}
						}
					}

					// clean the parent childs positions
					CleanPositions(dbcon, transaction, storage, parent);

					// update the storage
					using (IDbCommand dbcmd = dbcon.CreateCommand()) {
						dbcmd.Transaction = transaction;
						dbcmd.CommandText = "UPDATE storage SET mtime=datetime('now'),used=@used,rev=@rev WHERE id=@storage";
						dbcmd.Parameters.Add(new SqliteParameter("storage", storage));
						dbcmd.Parameters.Add(new SqliteParameter("used", Math.Max(0, used + size)));
						dbcmd.Parameters.Add(new SqliteParameter("rev", rev + 1));
						dbcmd.ExecuteNonQuery();
					}

					// move the temporary file to its storage
					if(tmpFile != null) {
						FileInfo fileInfo = new FileInfo(tmpFile);
						fileInfo.MoveTo(basePath + "/" + storage + "/" + file);
					}
					else if(mimetype != "application/x-directory") {
						FileStream stream = File.Create(basePath + "/" + storage + "/" + file);
						stream.Close();
					}
					// commit the transaction
					transaction.Commit();
				}
			}

			if(signal) {
				MonitorClientSignalChanged(storage, rev + 1);
				RaisesFileCreated(storage, file);
			}

			return file;
		}

		public delegate void CommentCreatedEventHandler(string storage, long file, long comment);
		List<CommentCreatedEventHandler> commentCreatedHandlers = new List<CommentCreatedEventHandler>();
		public event CommentCreatedEventHandler CommentCreated {
			add {
				lock(commentCreatedHandlers) {
					commentCreatedHandlers.Add(value);
				}
			}
			remove {
				lock(commentCreatedHandlers) {
					commentCreatedHandlers.Remove(value);
				}
			}
		}
		void RaisesCommentCreated(string storage, long file, long comment)
		{
			List<CommentCreatedEventHandler> handlers;
			lock(commentCreatedHandlers) {
				handlers = new List<CommentCreatedEventHandler>(commentCreatedHandlers);
			}
			foreach(CommentCreatedEventHandler handler in handlers) {
				try {
					handler(storage, file, comment);
				}
				catch(Exception e) {
					logger.Log(LogLevel.Error, "On CommentCreated handler fails ("+e.ToString()+")");
				}
			}
		}

		public long CreateComment(string storage, long file, string user, string content, bool signal)
		{
			long id;
			long rev = 0;

			lock(dbcon) {
				using (IDbTransaction transaction = dbcon.BeginTransaction()) {
					// get storage revision
					using (IDbCommand dbcmd = dbcon.CreateCommand()) {
						dbcmd.Transaction = transaction;
						dbcmd.CommandText = "SELECT rev FROM storage WHERE id=@storage";
						dbcmd.Parameters.Add(new SqliteParameter("storage", storage));
						using(IDataReader reader = dbcmd.ExecuteReader()) {
							if (!reader.Read ())
								throw new Exception ("Create fails, storage not found");
							rev = reader.GetInt64 (0);
							reader.Close ();
						}
					}

					long fileRev;

					// get old file infos
					using(IDbCommand dbcmd = dbcon.CreateCommand()) {
						dbcmd.Transaction = transaction;
						dbcmd.CommandText = "SELECT rev FROM file WHERE storage_id=@storage AND id=@file";
						dbcmd.Parameters.Add(new SqliteParameter("storage", storage));
						dbcmd.Parameters.Add(new SqliteParameter("file", file));
						using(IDataReader reader = dbcmd.ExecuteReader()) {
							if(!reader.Read())
								throw new Exception("Change fails, file not found");
							fileRev = reader.GetInt64(0);
							reader.Close ();
						}
					}
							
					// insert into comment table
					using(IDbCommand dbcmd = dbcon.CreateCommand()) {
						dbcmd.Transaction = transaction;
						dbcmd.CommandText = "INSERT INTO comment (file_id,user_id,content,ctime,mtime) "+
							"VALUES (@file,@user,@content,datetime('now'),datetime('now'))";
						dbcmd.Parameters.Add(new SqliteParameter("file", file));
						dbcmd.Parameters.Add(new SqliteParameter("user", user));
						dbcmd.Parameters.Add(new SqliteParameter("content", content));
						if(dbcmd.ExecuteNonQuery() != 1)
							throw new Exception("Comment create fails");
					}

					// get the insert id
					using(IDbCommand dbcmd = dbcon.CreateCommand()) {
						dbcmd.Transaction = transaction;
						dbcmd.CommandText = "SELECT last_insert_rowid()";
						id = Convert.ToInt64(dbcmd.ExecuteScalar ());
					}

					// update the file
					using(IDbCommand dbcmd = dbcon.CreateCommand()) {
						dbcmd.Transaction = transaction;
						dbcmd.CommandText = "UPDATE file SET mtime=datetime('now'),rev=@rev WHERE storage_id=@storage AND id=@file";
						dbcmd.Parameters.Add(new SqliteParameter("storage", storage));
						dbcmd.Parameters.Add(new SqliteParameter("file", file));
						dbcmd.Parameters.Add(new SqliteParameter("rev", fileRev + 1));
						dbcmd.ExecuteNonQuery();
					}

					// update the storage
					using(IDbCommand dbcmd = dbcon.CreateCommand()) {
						dbcmd.Transaction = transaction;
						dbcmd.CommandText = "UPDATE storage SET mtime=datetime('now'),rev=@rev WHERE id=@storage";
						dbcmd.Parameters.Add(new SqliteParameter("rev", rev + 1));
						dbcmd.Parameters.Add(new SqliteParameter("storage", storage));
						dbcmd.ExecuteNonQuery();
					}
				
					// commit the transaction
					transaction.Commit();
				}
			}

			if(signal) {
				MonitorClientSignalChanged(storage, rev + 1);
				RaisesCommentCreated(storage, file, id);
				RaisesFileChanged(storage, file);
			}
			return id;
		}

		public void ChangeComment(long id, string storage, long file, string user, string content, bool signal)
		{
			long rev = 0;

			lock(dbcon) {
				using(IDbTransaction transaction = dbcon.BeginTransaction()) {
					// get storage revision
					using (IDbCommand dbcmd = dbcon.CreateCommand()) {
						dbcmd.Transaction = transaction;
						dbcmd.CommandText = "SELECT rev FROM storage WHERE id=@storage";
						dbcmd.Parameters.Add(new SqliteParameter("storage", storage));
						using (IDataReader reader = dbcmd.ExecuteReader()) {
							if (!reader.Read ())
								throw new Exception("Create fails, storage not found");
							rev = reader.GetInt64(0);
							reader.Close();
						}
					}

					long fileRev;

					// get old file infos
					using(IDbCommand dbcmd = dbcon.CreateCommand()) {
						dbcmd.Transaction = transaction;
						dbcmd.CommandText = "SELECT rev FROM file WHERE storage_id=@storage AND id=@file";
						dbcmd.Parameters.Add(new SqliteParameter("storage", storage));
						dbcmd.Parameters.Add(new SqliteParameter("file", file));
						using(IDataReader reader = dbcmd.ExecuteReader()) {
							if(!reader.Read ())
								throw new Exception("Change fails, file not found");
							fileRev = reader.GetInt64(0);
							reader.Close();
						}
					}
							
					// update comment table
					using(IDbCommand dbcmd = dbcon.CreateCommand()) {
						dbcmd.Transaction = transaction;
						dbcmd.CommandText = "UPDATE comment SET content=@content, "+
							"user_id=@user, mtime=datetime('now') WHERE "+
							"id=@id AND file_id=@file";
						dbcmd.Parameters.Add(new SqliteParameter("content", content));
						dbcmd.Parameters.Add(new SqliteParameter("user", user));
						dbcmd.Parameters.Add(new SqliteParameter("file", file));
						dbcmd.Parameters.Add(new SqliteParameter("id", id));
						if (dbcmd.ExecuteNonQuery () != 1)
							throw new Exception ("Comment change fails");
					}

					// update the file
					using (IDbCommand dbcmd = dbcon.CreateCommand()) {
						dbcmd.Transaction = transaction;
						dbcmd.CommandText = "UPDATE file SET mtime=datetime('now'),rev=@rev WHERE storage_id=@storage AND id=@file";
						dbcmd.Parameters.Add(new SqliteParameter("rev", fileRev + 1));
						dbcmd.Parameters.Add(new SqliteParameter("storage", storage));
						dbcmd.Parameters.Add(new SqliteParameter("file", file));
						dbcmd.ExecuteNonQuery ();
					}

					// update the storage
					using (IDbCommand dbcmd = dbcon.CreateCommand()) {
						dbcmd.Transaction = transaction;
						dbcmd.CommandText = "UPDATE storage SET mtime=datetime('now'),rev=@rev WHERE id=@storage";
						dbcmd.Parameters.Add(new SqliteParameter("rev", rev + 1));
						dbcmd.Parameters.Add(new SqliteParameter("storage", storage));
						dbcmd.ExecuteNonQuery ();
					}
				
					// commit the transaction
					transaction.Commit();
				}
			}

			if(signal) {
				MonitorClientSignalChanged(storage, rev + 1);
				RaisesCommentCreated(storage, file, id);
				RaisesFileChanged(storage, file);
			}
		}


		public void DeleteComment(string storage, long file, long comment, bool signal)
		{
			long rev = 0;
			lock(dbcon) {
				using(IDbTransaction transaction = dbcon.BeginTransaction()) {
					// get storage revision
					using(IDbCommand dbcmd = dbcon.CreateCommand()) {
						dbcmd.Transaction = transaction;
						dbcmd.CommandText = "SELECT rev FROM storage WHERE id=@storage";
						dbcmd.Parameters.Add(new SqliteParameter("storage", storage));
						using(IDataReader reader = dbcmd.ExecuteReader()) {
							if(!reader.Read())
								throw new Exception("Delete fails, storage not found");
							rev = reader.GetInt64(0);
							reader.Close();
						}
					}

					long fileRev;

					// get old file infos
					using(IDbCommand dbcmd = dbcon.CreateCommand()) {
						dbcmd.Transaction = transaction;
						dbcmd.CommandText = "SELECT rev FROM file WHERE storage_id=@storage AND id=@file";
						dbcmd.Parameters.Add(new SqliteParameter("storage", storage));
						dbcmd.Parameters.Add(new SqliteParameter("file", file));
						using(IDataReader reader = dbcmd.ExecuteReader()) {
							if(!reader.Read())
								throw new Exception("Change fails, file not found");
							fileRev = reader.GetInt64(0);
							reader.Close();
						}
					}
							
					// delete the comment
					using(IDbCommand dbcmd = dbcon.CreateCommand()) {
						dbcmd.Transaction = transaction;
						dbcmd.CommandText = "DELETE FROM comment WHERE file_id=@file AND id=@comment";
						dbcmd.Parameters.Add(new SqliteParameter("file", file));
						dbcmd.Parameters.Add(new SqliteParameter("comment", comment));
						if(dbcmd.ExecuteNonQuery() != 1)
							throw new Exception("Comment delete fails");
					}

					// update the file
					using(IDbCommand dbcmd = dbcon.CreateCommand()) {
						dbcmd.Transaction = transaction;
						dbcmd.CommandText = "UPDATE file SET mtime=datetime('now'),rev=@rev WHERE storage_id=@storage AND id=@file";
						dbcmd.Parameters.Add(new SqliteParameter("rev", fileRev + 1));
						dbcmd.Parameters.Add(new SqliteParameter("storage", storage));
						dbcmd.Parameters.Add(new SqliteParameter("file", file));
						dbcmd.ExecuteNonQuery();
					}

					// update the storage
					using(IDbCommand dbcmd = dbcon.CreateCommand()) {
						dbcmd.Transaction = transaction;
						dbcmd.CommandText = "UPDATE storage SET mtime=datetime('now'),rev=@rev WHERE id=@storage";
						dbcmd.Parameters.Add(new SqliteParameter("storage", storage));
						dbcmd.Parameters.Add(new SqliteParameter("rev", rev + 1));
						dbcmd.ExecuteNonQuery();
					}
				
					// commit the transaction
					transaction.Commit();
				}
			}
			if(signal) {
				MonitorClientSignalChanged(storage, rev + 1);
				RaisesFileChanged(storage, file);
			}
		}

		public delegate void FileDeletedEventHandler(string storage, long file);
		List<FileDeletedEventHandler> fileDeletedHandlers = new List<FileDeletedEventHandler>();
		public event FileDeletedEventHandler FileDeleted {
			add {
				lock(fileDeletedHandlers) {
					fileDeletedHandlers.Add(value);
				}
			}
			remove {
				lock(fileDeletedHandlers) {
					fileDeletedHandlers.Remove(value);
				}
			}
		}
		void RaisesFileDeleted(string storage, long file)
		{
			List<FileDeletedEventHandler> handlers;
			lock(fileDeletedHandlers) {
				handlers = new List<FileDeletedEventHandler>(fileDeletedHandlers);
			}
			foreach(FileDeletedEventHandler handler in handlers) {
				try {
					handler(storage, file);
				}
				catch(Exception e) {
					logger.Log(LogLevel.Error, "On FileDeleted handler fails ("+e.ToString()+")");
				}
			}
		}
		
		public bool DeleteFile(string storage, long file)
		{
			long rev = 0;
			List<long> childs = new List<long>();
			lock(dbcon) {
				using(IDbTransaction transaction = dbcon.BeginTransaction()) {
				
					long size = 0;
					long parent_id = 0;
					// get the file info
					using(IDbCommand dbcmd = dbcon.CreateCommand()) {
						dbcmd.CommandText = "SELECT mimetype,size,parent_id FROM file WHERE id=@file";
						dbcmd.Parameters.Add(new SqliteParameter("file", file));
						dbcmd.Transaction = transaction;
						using(IDataReader reader = dbcmd.ExecuteReader()) {
							if(!reader.Read())
								return false;
							size = reader.GetInt64(1);
							parent_id = reader.GetInt64(2);
							reader.Close();
						}
					}

					// delete the meta
					using(IDbCommand dbcmd = dbcon.CreateCommand()) {
						dbcmd.Transaction = transaction;
						dbcmd.CommandText = "DELETE FROM meta WHERE owner_id=@file";
						dbcmd.Parameters.Add(new SqliteParameter("file", file));
						dbcmd.ExecuteNonQuery();
					}

					// delete the comments
					using(IDbCommand dbcmd = dbcon.CreateCommand()) {
						dbcmd.Transaction = transaction;
						dbcmd.CommandText = "DELETE FROM comment WHERE file_id=@file";
						dbcmd.Parameters.Add(new SqliteParameter("file", file));
						dbcmd.ExecuteNonQuery();
					}

					// delete the file storage table
					using(IDbCommand dbcmd = dbcon.CreateCommand()) {
						dbcmd.Transaction = transaction;
						dbcmd.CommandText = "DELETE FROM file WHERE id=@file";
						dbcmd.Parameters.Add(new SqliteParameter("file", file));
						if(dbcmd.ExecuteNonQuery() != 1)
							throw new Exception("File delete fails, file not found");
					}

					long used = 0;
					// get storage quota used
					using(IDbCommand dbcmd = dbcon.CreateCommand()) {
						dbcmd.Transaction = transaction;
						dbcmd.CommandText = "SELECT used,rev FROM storage WHERE id=@storage";
						dbcmd.Parameters.Add(new SqliteParameter("storage", storage));
						using(IDataReader reader = dbcmd.ExecuteReader()) {
							if(!reader.Read())
								throw new Exception("Delete fails, storage not found");
							used = reader.GetInt64(0);
							rev = reader.GetInt64(1);
							reader.Close();
						}
					}

					// update the storage
					using(IDbCommand dbcmd = dbcon.CreateCommand()) {
						dbcmd.Transaction = transaction;
						dbcmd.CommandText = "UPDATE storage SET mtime=datetime('now'),used=@used,rev=@rev WHERE id=@storage";
						dbcmd.Parameters.Add(new SqliteParameter("used", Math.Max(0,used-size)));
						dbcmd.Parameters.Add(new SqliteParameter("rev", rev + 1));
						dbcmd.Parameters.Add(new SqliteParameter("storage", storage));
						dbcmd.ExecuteNonQuery();
					}
				
					// clean the parent childs positions of the removed file
					CleanPositions(dbcon, transaction, storage, parent_id);

					// remove the possible file on the disk
					if(File.Exists(basePath+"/"+storage+"/"+file))
						File.Delete(basePath+"/"+storage+"/"+file);
				
					// select the children of the current file to delete them
					using(IDbCommand dbcmd = dbcon.CreateCommand()) {
						dbcmd.CommandText = "SELECT id FROM file WHERE parent_id=@file";
						dbcmd.Parameters.Add(new SqliteParameter("file", file));
						dbcmd.Transaction = transaction;
						using(IDataReader reader = dbcmd.ExecuteReader()) {
							while(reader.Read()) {
								childs.Add(reader.GetInt64(0));
							}
							reader.Close();
						}
					}

					// commit the transaction
					transaction.Commit();
				}										
			}

			MonitorClientSignalChanged(storage, rev + 1);
			RaisesFileDeleted(storage, file);
			
			// delete the selected children
			foreach(long child in childs)					
				DeleteFile(storage, child);
			return true;
		}
		
		public void GetDownloadFileInfo(string storage, long file, out string mimetype, out string filename, out long rev)
		{
			lock(dbcon) {
				// get the insert id
				using(IDbCommand dbcmd = dbcon.CreateCommand()) {
					dbcmd.CommandText = "SELECT mimetype,name,rev FROM file WHERE storage_id=@storage AND id=@file";
					dbcmd.Parameters.Add(new SqliteParameter("storage", storage));
					dbcmd.Parameters.Add(new SqliteParameter("file", file));
					using(IDataReader reader = dbcmd.ExecuteReader()) {
						if(!reader.Read())
							throw new Exception("File not found");
						
						mimetype = reader.GetString(0);
						filename = reader.GetString(1);
						rev = reader.GetInt64(2);
						
						// clean up
						reader.Close();
					}
				}
			}
		}
		
		public void GetFileInfo(string storage, long file, out long parent, out string mimetype, out string name, out long ctime, out long mtime, out long rev, out long size, out long position, out Dictionary<string,string> meta)
		{
			lock(dbcon) {
				using(IDbTransaction transaction = dbcon.BeginTransaction()) {
				
					// get file infos
					using(IDbCommand dbcmd = dbcon.CreateCommand()) {
						dbcmd.Transaction = transaction;
						dbcmd.CommandText = "SELECT parent_id,mimetype,name,strftime('%s',ctime),strftime('%s',mtime),rev,size,position FROM file WHERE storage_id=@storage AND id=@file";
						dbcmd.Parameters.Add(new SqliteParameter("storage", storage));
						dbcmd.Parameters.Add(new SqliteParameter("file", file));
						using(IDataReader reader = dbcmd.ExecuteReader()) {
							if(!reader.Read())
								throw new Exception("File not found");
							parent = reader.GetInt64(0);
							mimetype = reader.GetString(1);
							name = reader.GetString(2);
							ctime = Convert.ToInt64(reader.GetString(3));
							mtime = Convert.ToInt64(reader.GetString(4));
							rev = reader.GetInt64(5);
							size = reader.GetInt64(6);
							position = reader.GetInt64(7)/2;
							// clean up
							reader.Close();
						}
					}
				
					// get the meta
					meta = new Dictionary<string, string>();
					using(IDbCommand dbcmd = dbcon.CreateCommand()) {
						dbcmd.Transaction = transaction;
						dbcmd.CommandText = "SELECT key,value FROM meta WHERE owner_id=@file";
						dbcmd.Parameters.Add(new SqliteParameter("file", file));
						using(IDataReader reader = dbcmd.ExecuteReader()) {
							while(reader.Read()) {
								string key = reader.GetString(0);
								string value = reader.GetString(1);
								meta[key] = value;
							}
							reader.Close();
						}
					}

					// commit the transaction
					transaction.Commit();
				}
			}
		}
		
		public delegate void FileChangedEventHandler(string storage, long file);
		List<FileChangedEventHandler> fileChangedHandlers = new List<FileChangedEventHandler>();
		public event FileChangedEventHandler FileChanged {
			add {
				lock(fileChangedHandlers) {
					fileChangedHandlers.Add(value);
				}
			}
			remove {
				lock(fileChangedHandlers) {
					fileChangedHandlers.Remove(value);
				}
			}
		}
		void RaisesFileChanged(string storage, long file)
		{
			List<FileChangedEventHandler> handlers;
			lock(fileChangedHandlers) {
				handlers = new List<FileChangedEventHandler>(fileChangedHandlers);
			}
			foreach(FileChangedEventHandler handler in handlers) {
				try {
					handler(storage, file);
				}
				catch(Exception e) {
					logger.Log(LogLevel.Error, "On FileChanged handler fails ("+e.ToString()+")");
				}
			}
		}

		public bool ChangeFile(string storage, long file, string tmpFile, JsonValue define)
		{			
			long rev = 0;
			lock(dbcon) {
				using(IDbTransaction transaction = dbcon.BeginTransaction()) {
				
					long size = 0;
					if(tmpFile != null) {
						FileInfo fileInfo = new FileInfo(tmpFile);
						size = fileInfo.Length;
					}

					// get quota and quota used
					long quota, used = 0;
					using(IDbCommand dbcmd = dbcon.CreateCommand()) {
						dbcmd.Transaction = transaction;
						dbcmd.CommandText = "SELECT quota,used,rev FROM storage WHERE id=@storage";
						dbcmd.Parameters.Add(new SqliteParameter("storage", storage));
						using(IDataReader reader = dbcmd.ExecuteReader()) {
							if(!reader.Read())
								return false;
							quota = reader.GetInt64(0);
							used = reader.GetInt64(1);
							rev = reader.GetInt64(2);
							reader.Close();
						}
					}
				
					long oldSize = 0;
					long oldRev = 0;
					long oldParentId = 0;
					long oldPosition = 0;
				
					// get old file infos
					using(IDbCommand dbcmd = dbcon.CreateCommand()) {
						dbcmd.Transaction = transaction;
						dbcmd.CommandText = "SELECT size,rev,parent_id,position FROM file WHERE storage_id=@storage AND id=@file";
						dbcmd.Parameters.Add(new SqliteParameter("storage", storage));
						dbcmd.Parameters.Add(new SqliteParameter("file", file));
						using(IDataReader reader = dbcmd.ExecuteReader()) {
							if(!reader.Read())
								throw new Exception("Change fails, file not found");
							oldSize = reader.GetInt64(0);
							oldRev = reader.GetInt64(1);
							oldParentId = reader.GetInt64(2);
							oldPosition = reader.GetInt64(3)/2;
							reader.Close();
						}
					}
					if(tmpFile != null) {
						// check the quota
						if((quota != -1) && ((used - oldSize) + size > quota))
							return false;
					}

					long newParentId = -1;
					long newPosition = -1;
				
					// update file request
					if(define.ContainsKey("name") || define.ContainsKey("parent_id") || define.ContainsKey("position") || (tmpFile != null)) {
						string sql = "UPDATE file SET ";
						bool first = true;
						if(define.ContainsKey("name")) {
							if(first)
								first = false;
							else
								sql += ", ";
							sql += "name='"+((string)define["name"]).Replace("'","''")+"'";
						}
						if(define.ContainsKey("parent_id")) {
							if(first)
								first = false;
							else
								sql += ", ";
							newParentId = (long)define["parent_id"];
							sql += "parent_id="+newParentId;

							long tmpPosition = Int64.MaxValue/4;
							if(define.ContainsKey("position"))
								tmpPosition = (long)define["position"];
						
							sql += ", position="+(tmpPosition*2);
						}
						else if(define.ContainsKey("position")) {
							if(first)
								first = false;
							else
								sql += ", ";
							newPosition = (long)define["position"];
							long tmpPosition = 0;
							if(newPosition <= oldPosition)
								tmpPosition = newPosition*2;
							else
								tmpPosition = (newPosition+1)*2;
							sql += "position="+tmpPosition;
						}
						if(tmpFile != null) {
							if(first)
								first = false;
							else
								sql += ", ";
							sql += "size="+size+", rev="+(oldRev+1)+", mtime=datetime('now')";
						}
						sql += " WHERE id="+file;

						using(IDbCommand dbcmd = dbcon.CreateCommand()) {
							dbcmd.Transaction = transaction;
							dbcmd.CommandText = sql;
							dbcmd.ExecuteNonQuery();
						}
					}

					// update the meta
					if(define.ContainsKey("meta")) {
						JsonObject meta = (JsonObject)define["meta"];
						JsonObject oldMeta = new JsonObject();
					
						using(IDbCommand dbcmd = dbcon.CreateCommand()) {
							dbcmd.Transaction = transaction;
							dbcmd.CommandText = "SELECT key,value FROM meta WHERE owner_id="+file;
							using(IDataReader reader = dbcmd.ExecuteReader()) {
								while(reader.Read()) {
									string key = reader.GetString(0);
									string value = reader.GetString(1);
									oldMeta[key] = value;
								}
								reader.Close();
							}
						}

						foreach(string key in meta.Keys) {
							string value = (string)meta[key];
							if(value == null) {
								if(oldMeta.ContainsKey(key))
									oldMeta.Remove(key);
							}
							else {
								oldMeta[key] = value;
							}
						}
						
						// delete all old meta
						using(IDbCommand dbcmd = dbcon.CreateCommand()) {
							dbcmd.Transaction = transaction;
							dbcmd.CommandText = "DELETE FROM meta WHERE owner_id=@file";
							dbcmd.Parameters.Add(new SqliteParameter("file", file));
							dbcmd.ExecuteNonQuery();
						}
						
						// recreate all meta
						foreach(string key in oldMeta.Keys) {
							string value = oldMeta[key];
							using(IDbCommand dbcmd = dbcon.CreateCommand()) {
								dbcmd.Transaction = transaction;
								dbcmd.CommandText = "INSERT INTO meta (owner_id,key,value) VALUES (@file,@key,@value)";
								dbcmd.Parameters.Add(new SqliteParameter("file", file));
								dbcmd.Parameters.Add(new SqliteParameter("key", key));
								dbcmd.Parameters.Add(new SqliteParameter("value", value));
								dbcmd.ExecuteNonQuery();
							}
						}
					}

					// clean parents childs positions if needed (parent change or/and new position)
					if((newParentId != -1) && (newParentId != oldParentId)) {
						CleanPositions(dbcon, transaction, storage, oldParentId);
						CleanPositions(dbcon, transaction, storage, newParentId);
					}
					else if((newPosition != -1) && (newPosition != oldPosition))
						CleanPositions(dbcon, transaction, storage, oldParentId);

					// update the storage
					using(IDbCommand dbcmd = dbcon.CreateCommand()) {
						dbcmd.Transaction = transaction;
						dbcmd.CommandText = "UPDATE storage SET mtime=datetime('now'),used=@used,rev=@rev WHERE id=@storage";
						dbcmd.Parameters.Add(new SqliteParameter("storage", storage));
						dbcmd.Parameters.Add(new SqliteParameter("used", Math.Max(0,used-oldSize+size)));
						dbcmd.Parameters.Add(new SqliteParameter("rev", rev + 1));
						dbcmd.ExecuteNonQuery();
					}

					// move the temporary file to its storage
					if(tmpFile != null) {
						FileInfo fileInfo = new FileInfo(tmpFile);
						fileInfo.Replace(basePath+"/"+storage+"/"+file, null);
					}
					// commit the transaction
					transaction.Commit();
				}				
			}
			MonitorClientSignalChanged(storage, rev + 1);
			RaisesFileChanged(storage, file);
			return true;
		}

		void CleanPositions(IDbConnection dbcon, IDbTransaction transaction, string storage, long parent)
		{
			List<long> files = new List<long>();
			// get all files of a directory
			long i = 0;
			bool cleanNeeded = false;
			using(IDbCommand dbcmd = dbcon.CreateCommand()) {
				dbcmd.Transaction = transaction;
				dbcmd.CommandText = "SELECT id,position FROM file WHERE storage_id=@storage AND parent_id=@parent ORDER BY position ASC";
				dbcmd.Parameters.Add(new SqliteParameter("storage", storage));
				dbcmd.Parameters.Add(new SqliteParameter("parent", parent));
				using(IDataReader reader = dbcmd.ExecuteReader()) {
					while(reader.Read()) {
						files.Add(reader.GetInt64(0));
						cleanNeeded |= (reader.GetInt64(1) != (i * 2)+1);
						i++;
					}
					reader.Close();
				}
			}
			// update the files positions
			if(cleanNeeded) {
				i = 0;
				foreach(long file in files) {
					using(IDbCommand dbcmd = dbcon.CreateCommand()) {
						dbcmd.Transaction = transaction;
						dbcmd.CommandText = "UPDATE file SET position=@position WHERE storage_id=@storage AND id=@file";
						dbcmd.Parameters.Add(new SqliteParameter("storage", storage));
						dbcmd.Parameters.Add(new SqliteParameter("file", file));
						dbcmd.Parameters.Add(new SqliteParameter("position", (i * 2)+1));
						dbcmd.ExecuteNonQuery();
					}
					i++;
				}
			}
		}

		public string GetFullPath(string storage, long file)
		{
			return basePath+"/"+storage+"/"+file;
		}

		public JsonValue GetComment(string storage, long file, long comment)
		{
			JsonValue res;
			lock(dbcon) {
				using(IDbTransaction transaction = dbcon.BeginTransaction()) {
					res = GetComment(dbcon, transaction, storage, file, comment);
					// commit the transaction
					transaction.Commit();
				}
			}
			return res;
		}

		JsonValue GetComment(IDbConnection dbcon, IDbTransaction transaction, string storage, long file, long comment)
		{
			JsonValue res = null;
			// select comment
			using(IDbCommand dbcmd = dbcon.CreateCommand()) {
				dbcmd.Transaction = transaction;
				dbcmd.CommandText = "SELECT id,user_id,content,strftime('%s',ctime),strftime('%s',mtime) FROM comment WHERE file_id=@file AND id=@comment";
				dbcmd.Parameters.Add(new SqliteParameter("file", file));
				dbcmd.Parameters.Add(new SqliteParameter("comment", comment));
				using(IDataReader reader = dbcmd.ExecuteReader()) {
					while(reader.Read()) {
						res = new JsonObject();
						res["id"] = reader.GetInt64(0);
						res["user"] = reader.GetString(1);
						res["content"] = reader.GetString(2);
						res["ctime"] = Convert.ToInt64(reader.GetString(3));
						if(!reader.IsDBNull(4))
							res["mtime"] = Convert.ToInt64(reader.GetString(4));
					}
					reader.Close();
				}
			}
			return res;
		}

		public JsonValue GetFileInfo(string storage, long file, int depth)
		{
			JsonValue res = new JsonObject();
			lock(dbcon) {
				using(IDbTransaction transaction = dbcon.BeginTransaction()) {
					long quota, used, ctime, mtime, storageRev;
					GetStorageInfo(dbcon, transaction, storage, out quota, out used, out ctime, out mtime, out storageRev);

					res = GetFileInfo(dbcon, transaction, storage, file, depth);
					if(res != null) {
						res["storage_rev"] = storageRev;

						//StorageFile storageFile = new StorageFile(this, dbcon, transaction, storage, res);

						// handle plugins
						//if(mimePlugins.ContainsKey("*/*")) {
						//	foreach(IStoragePlugin plugin in mimePlugins["*/*"])
						//		plugin.GetFile(storageFile);
						//}
						//if(mimePlugins.ContainsKey(res["mimetype"])) {
						//	foreach(IStoragePlugin plugin in mimePlugins[res["mimetype"]])
						//		plugin.GetFile(storageFile);
						//}
						//storageFile.UpdateCache();
					}
					// commit the transaction
					transaction.Commit();
				}
			}
			return res;
		}

		public JsonValue GetFileInfo(IDbConnection dbcon, IDbTransaction transaction, string storage, long file, int depth)
		{
			JsonValue res = new JsonObject();

			long parent_id = 0;
			string name = null;
			string mimetype = "application/x-directory";
			long ctime = 0;
			long mtime = 0;
			long rev = 0;
			long size = 0;
			long position = 0;
						
			// get file info if not the root dir
			if(file != 0) {
				using(IDbCommand dbcmd = dbcon.CreateCommand()) {
					dbcmd.Transaction = transaction;
					dbcmd.CommandText = "SELECT parent_id,name,mimetype,strftime('%s',ctime),strftime('%s',mtime),rev,size,position FROM file WHERE storage_id=@storage AND id=@file";
					dbcmd.Parameters.Add(new SqliteParameter("storage", storage));
					dbcmd.Parameters.Add(new SqliteParameter("file", file));
					using(IDataReader reader = dbcmd.ExecuteReader()) {
						if(!reader.Read())
							return null;
						parent_id = reader.GetInt64(0);
						name = reader.GetString(1);
						mimetype = reader.GetString(2);
						ctime = Convert.ToInt64(reader.GetString(3));
						mtime = Convert.ToInt64(reader.GetString(4));
						rev = reader.GetInt64(5);
						size = reader.GetInt64(6);
						position = reader.GetInt64(7) / 2;
						reader.Close();
					}
				}
			}
			// check if storage exists
			else {
				using(IDbCommand dbcmd = dbcon.CreateCommand()) {
					dbcmd.CommandText = "SELECT strftime('%s',ctime),strftime('%s',mtime) FROM storage WHERE id=@storage";
					dbcmd.Parameters.Add(new SqliteParameter("storage", storage));
					using(IDataReader reader = dbcmd.ExecuteReader()) {
						if(!reader.Read())
							return null;
						ctime = Convert.ToInt64(reader.GetString(0));
						mtime = Convert.ToInt64(reader.GetString(1));
						reader.Close();
					}
				}
			}
			
			res["id"] = file;
			res["parent_id"] = parent_id;
			res["name"] = name;
			res["mimetype"] = mimetype;
			res["ctime"] = ctime;
			res["mtime"] = mtime;
			res["rev"] = rev;
			res["size"] = size;
			res["position"] = position;
								
			// select meta
			using(IDbCommand dbcmd = dbcon.CreateCommand()) {
				dbcmd.Transaction = transaction;
				dbcmd.CommandText = "SELECT key,value FROM meta WHERE owner_id=@file";
				dbcmd.Parameters.Add(new SqliteParameter("file", file));
				using(IDataReader reader = dbcmd.ExecuteReader()) {
					JsonValue meta = new JsonObject();
					res["meta"] = meta;
					while(reader.Read())
						meta[reader.GetString(0)] = reader.GetString(1);
					reader.Close();
				}
			}
			// select comment
			using(IDbCommand dbcmd = dbcon.CreateCommand()) {
				dbcmd.Transaction = transaction;
				dbcmd.CommandText = "SELECT id,user_id,content,strftime('%s',ctime),strftime('%s',mtime) FROM comment WHERE file_id=@file ORDER BY ctime DESC";
				dbcmd.Parameters.Add(new SqliteParameter("file", file));
				using(IDataReader reader = dbcmd.ExecuteReader()) {
					JsonArray comments = new JsonArray();
					res["comments"] = comments;
					while(reader.Read()) {
						JsonValue comment = new JsonObject();
						comment["id"] = reader.GetInt64(0);
						comment["user"] = reader.GetString(1);
						comment["content"] = reader.GetString(2);
						comment["ctime"] = Convert.ToInt64(reader.GetString(3));
						if(!reader.IsDBNull(4))
							comment["mtime"] = Convert.ToInt64(reader.GetString(4));
						comments.Add(comment);
					}
					reader.Close();
				}
			}

			// select cache
			using(IDbCommand dbcmd = dbcon.CreateCommand()) {
				dbcmd.Transaction = transaction;
				dbcmd.CommandText = "SELECT key,value FROM cache WHERE owner_id=@file";
				dbcmd.Parameters.Add(new SqliteParameter("file", file));
				using(IDataReader reader = dbcmd.ExecuteReader()) {
					JsonValue cache = new JsonObject();
					res["cache"] = cache;
					while(reader.Read())
						cache[reader.GetString(0)] = reader.GetString(1);
					reader.Close();
				}
			}

			// select subchildren
			if(depth > 0) {
				// clean the childs positions if needed
				CleanPositions(dbcon, transaction, storage, file);

				using(IDbCommand dbcmd = dbcon.CreateCommand()) {
					dbcmd.Transaction = transaction;
					dbcmd.CommandText = "SELECT id FROM file WHERE parent_id=@file AND storage_id=@storage ORDER BY position ASC";
					dbcmd.Parameters.Add(new SqliteParameter("storage", storage));
					dbcmd.Parameters.Add(new SqliteParameter("file", file));
					using(IDataReader reader = dbcmd.ExecuteReader()) {
						if(reader.Read()) {
							JsonArray children = new JsonArray();
							res["children"] = children;
							do {
								children.Add(GetFileInfo(dbcon, transaction, storage, reader.GetInt64(0), depth-1));
							} while(reader.Read());
							reader.Close();
						}
					}
				}
			}
			return res;
		}
				
		public async Task ProcessRequestAsync(HttpContext context)
		{
			string[] parts = context.Request.Path.Split(new char[] { '/' }, System.StringSplitOptions.RemoveEmptyEntries);
			long id = 0;
			long id2 = 0;
			long id3 = 0;

			// WS /[storage] monitor changes in a storage
			if((context.Request.IsWebSocketRequest) && (parts.Length == 1)) {
				string storage = parts[0];
				Rights.EnsureCanReadStorage(context, storage);

				await context.AcceptWebSocketRequestAsync(new MonitorClient(this, storage));
			}
			// POST / create a storage
			else if((context.Request.Method == "POST") && (parts.Length == 0)) {
				JsonValue json = await context.Request.ReadAsJsonAsync();

				Rights.EnsureCanCreateStorage(context);

				string storage = null;
				if(json.ContainsKey("id"))
					storage = (string)json["id"];
				long quota = (long)json["quota"];
				storage = CreateStorage(storage, quota);
				
				context.Response.StatusCode = 200;
				context.Response.Headers["cache-control"] = "no-cache, must-revalidate";
				context.Response.Content = new JsonContent(GetStorageInfo(storage));
			}
			// DELETE /[storage] delete a storage
			else if((context.Request.Method == "DELETE") && (parts.Length == 1)) {

				Rights.EnsureCanDeleteStorage(context, parts[0]);

				DeleteStorage(parts[0]);
				context.Response.StatusCode = 200;
				context.Response.Headers["cache-control"] = "no-cache, must-revalidate";
			}
			// PUT /[storage]  change a storage
			else if((context.Request.Method == "PUT") && (parts.Length == 1) && long.TryParse(parts[0], out id)) {
				string storage = parts[0];
				Rights.EnsureCanUpdateStorage(context, storage);

				JsonValue json = await context.Request.ReadAsJsonAsync();
				if(ChangeStorage(storage, json)) {
					JsonValue info = GetStorageInfo(storage);
					if(info != null) {
						context.Response.StatusCode = 200;
						context.Response.Headers["cache-control"] = "no-cache, must-revalidate";
						context.Response.Content = new JsonContent(info);
					}
					else {
						context.Response.StatusCode = 404;
					}
				}
				else {
					context.Response.StatusCode = 404;
				}
			}
			// GET /[storage] get storage info
			else if((context.Request.Method == "GET") && (parts.Length == 1)) {
				string storage = parts[0];
				Rights.EnsureCanReadStorage(context, storage);

				context.Response.Headers["cache-control"] = "no-cache, must-revalidate";
				JsonValue info = GetStorageInfo(storage);
				if(info == null) {
					context.Response.StatusCode = 404;
				}
				else {
					context.Response.StatusCode = 200;
					context.Response.Content = new JsonContent(info);
				}
			}
			// POST /[storage]/[parent] create a file
			else if((context.Request.Method == "POST") && (parts.Length == 2) && long.TryParse(parts[1], out id2)) {
				string storage = parts[0];
				long parent = id2;

				Rights.EnsureCanCreateFile(context, storage);

				string filename = null;
				string mimetype = null;
				string tmpFile = null;
				JsonValue define = new JsonObject();
				string fileContentType = null;
	
				string contentType = context.Request.Headers["content-type"];
				if(contentType.IndexOf("multipart/form-data") >= 0) {
					MultipartReader reader = context.Request.ReadAsMultipart();
					MultipartPart part;
					while((part = await reader.ReadPartAsync()) != null) {
						// the JSON define part
						if(part.Headers.ContentDisposition["name"] == "define") {
							StreamReader streamReader = new StreamReader(part.Stream, Encoding.UTF8);
							string jsonString = await streamReader.ReadToEndAsync();
							define = JsonValue.Parse(jsonString);

							if(define.ContainsKey("name"))
								filename = (string)define["name"];
							if(define.ContainsKey("mimetype"))
								mimetype = (string)define["mimetype"];
						}
						// the file content
						else if((part.Headers.ContentDisposition["name"] == "file") && (tmpFile == null)) {
							tmpFile = temporaryDirectory+"/"+Guid.NewGuid().ToString();
							using(FileStream fileStream = new FileStream(tmpFile, FileMode.CreateNew, FileAccess.Write)) {
								await part.Stream.CopyToAsync(fileStream);
							}
							if((filename == null) && part.Headers.ContentDisposition.ContainsKey("filename"))
								filename = part.Headers.ContentDisposition["filename"];
							if(part.Headers.ContainsKey("content-type"))
								fileContentType = part.Headers["content-type"];
						}
					}
				}
				else {
					define = await context.Request.ReadAsJsonAsync();
					if(define.ContainsKey("name"))
						filename = (string)define["name"];
					if(define.ContainsKey("mimetype"))
						mimetype = (string)define["mimetype"];
				}

				long file;
				if(define.ContainsKey("downloadUrl") && (tmpFile == null)) {
					string downloadUrl = define["downloadUrl"];
					((JsonObject)define).Remove("downloadUrl");
					file = CreateFileFromUrl(storage, parent, filename, mimetype, downloadUrl, define, true);
				}
				else {
					if(filename == null) {
						filename = "unknown";
						if(mimetype == null)
							mimetype = "application/octet-stream";
					}
					else if(mimetype == null) {
						// if mimetype was not given in the define part, decide it from
						// the file extension
						mimetype = FileContent.MimeType(filename);
						// if not found from the file extension, decide it from the Content-Type
						if((mimetype == "application/octet-stream") && (fileContentType != null))
							mimetype = fileContentType;
					}
					file = CreateFile(storage, parent, filename, mimetype, tmpFile, define, true);
				}
										
				context.Response.StatusCode = 200;
				context.Response.Headers["cache-control"] = "no-cache, must-revalidate";
				context.Response.Content = new JsonContent(GetFileInfo(storage, file, 0));
			}
			// GET /[storage]/[file] get file info
			else if((context.Request.Method == "GET") && (parts.Length == 2) && long.TryParse(parts[1], out id2)) {
				string storage = parts[0];
				long file = id2;

				Rights.EnsureCanReadFile(context, storage);

				int depth = 0;
				if(context.Request.QueryString.ContainsKey("depth"))
					depth = Math.Max(0, Convert.ToInt32(context.Request.QueryString["depth"]));
				context.Response.Headers["cache-control"] = "no-cache, must-revalidate";
				JsonValue info = GetFileInfo(storage, file, depth);
				if(info == null) {
					context.Response.StatusCode = 404;
				}
				else {
					context.Response.StatusCode = 200;
					context.Response.Content = new JsonContent(info);
				}
			}
			// GET /[storage]/[file]/content get file content
			else if((context.Request.Method == "GET") && (parts.Length == 3) && long.TryParse(parts[1], out id2) && (parts[2] == "content")) {
				string storage = parts[0];
				long file = id2;

				Rights.EnsureCanReadFile(context, storage);

				string filename ;
				string mimetype;
				long rev;
				GetDownloadFileInfo(storage, file, out mimetype, out filename, out rev);

				long argRev = -1;
				if(context.Request.QueryString.ContainsKey("rev"))
					argRev = Convert.ToInt64(context.Request.QueryString["rev"]);

				// redirect to the URL with the correct rev
				if(argRev != rev) {
					context.Response.StatusCode = 307;
					context.Response.Headers["location"] = "content?rev=" + rev;
				}
				else {
					if(context.Request.QueryString.ContainsKey("attachment"))
						context.Response.Headers["content-disposition"] = "attachment; filename=\"" + filename + "\"";
					context.Response.Headers["content-type"] = mimetype;

					context.Response.StatusCode = 200;
					if(!context.Request.QueryString.ContainsKey("nocache"))
						context.Response.Headers["cache-control"] = "max-age=" + cacheDuration;
					context.Response.SupportRanges = true;
					context.Response.Content = new FileContent(basePath + "/" + storage + "/" + file);
				}
			}
			// DELETE /[storage]/[file] delete a file
			else if((context.Request.Method == "DELETE") && (parts.Length == 2) && long.TryParse(parts[1], out id2)) {
				string storage = parts[0];
				long file = id2;

				Rights.EnsureCanDeleteFile(context, storage);

				context.Response.Headers["cache-control"] = "no-cache, must-revalidate";
				if(DeleteFile(storage, file))
					context.Response.StatusCode = 200;
				else
					context.Response.StatusCode = 404;
			}
			// PUT /[storage]/[file] modify a file
			else if((context.Request.Method == "PUT") && (parts.Length == 2) && long.TryParse(parts[1], out id2)) {
				string storage = parts[0];
				long file = id2;

				Rights.EnsureCanUpdateFile(context, storage);

				string tmpFile = null;
				JsonValue define = new JsonObject();
	
				string contentType = context.Request.Headers["content-type"];
				if(contentType.IndexOf("multipart/form-data") >= 0) {
					tmpFile = temporaryDirectory+"/"+Guid.NewGuid().ToString();

					MultipartReader reader = context.Request.ReadAsMultipart();
					MultipartPart part;
					while((part = await reader.ReadPartAsync()) != null) {
						// the JSON define part
						if(part.Headers.ContentDisposition["name"] == "define") {
							StreamReader streamReader = new StreamReader(part.Stream, Encoding.UTF8);
							string jsonString = await streamReader.ReadToEndAsync();
							define = JsonValue.Parse(jsonString);
						}
						// the file content
						else if(part.Headers.ContentDisposition["name"] == "file") {
							using(FileStream fileStream = new FileStream(tmpFile, FileMode.CreateNew, FileAccess.Write)) {
								await part.Stream.CopyToAsync(fileStream);
							}
						}
					}
				}
				else {
					define = await context.Request.ReadAsJsonAsync();
				}
				if(ChangeFile(storage, file, tmpFile, define)) {
					context.Response.StatusCode = 200;
					context.Response.Headers["cache-control"] = "no-cache, must-revalidate";
					context.Response.Content = new JsonContent(GetFileInfo(storage, file, 0));
				}
				else {
					context.Response.StatusCode = 404;
				}
			}
			// POST /[storage]/[file]/comments  create a comment 
			else if((context.Request.Method == "POST") && (parts.Length == 3) && long.TryParse(parts[1], out id2) && (parts[2] == "comments")) {
				string storage = parts[0];
				JsonValue define = await context.Request.ReadAsJsonAsync();

				Rights.EnsureCanCreateComment(context, storage, id2, (string)define["user"]);

				CreateComment(storage, id2, (string)define["user"], (string)define["content"], true);

				context.Response.StatusCode = 200;
				context.Response.Headers["cache-control"] = "no-cache, must-revalidate";
			}
			// PUT /[storage]/[file]/comments/[comment]  modify a comment 
			else if((context.Request.Method == "PUT") && (parts.Length == 4) && long.TryParse(parts[1], out id2) && (parts[2] == "comments") && long.TryParse(parts[3], out id3)) {
				string storage = parts[0];
				JsonValue define = await context.Request.ReadAsJsonAsync();

				Rights.EnsureCanUpdateComment(context, storage, id2, id3);

				ChangeComment(id3, storage, id2, (string)define["user"], (string)define["content"], true);

				context.Response.StatusCode = 200;
				context.Response.Headers["cache-control"] = "no-cache, must-revalidate";
			}
			// DELETE /[storage]/[file]/comments/[comment] delete a comment
			else if((context.Request.Method == "DELETE") && (parts.Length == 4) && long.TryParse(parts[1], out id2) && (parts[2] == "comments") && long.TryParse(parts[3], out id3)) {
				string storage = parts[0];

				JsonValue json = GetComment(storage, id2, id3);

				Rights.EnsureCanDeleteComment(context, storage, id2, id3, (string)json["user"]);

				DeleteComment(storage, id2, id3, true);

				context.Response.StatusCode = 200;
				context.Response.Headers["cache-control"] = "no-cache, must-revalidate";
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
