// PreviewService.cs
// 
//  Get an image preview of a file
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
using System.Threading;
using System.Diagnostics;
using System.Collections.Generic;
using System.Data;
using Mono.Data.Sqlite;
using Erasme.Http;
using Erasme.Cloud;
using Erasme.Cloud.Storage;
using Erasme.Cloud.Logger;

namespace Erasme.Cloud.Preview
{
	public class PreviewService: HttpHandler, IDisposable
	{
		public StorageService Storage { get; private set; }
		string basePath;
		int width;
		int height;
		string temporaryDirectory;
		int cacheDuration;
		ILogger logger;

		IDbConnection dbcon;
				
		public class Resource: IDisposable
		{
			static object globalLock = new object();
			static Dictionary<string,LockWrapper> resourceLocks = new Dictionary<string, LockWrapper>();
			
			string resource;
		
			class LockWrapper
			{
				public object instanceLock;
				public int counter;
				public bool taken;
			}

			Resource(string resource)
			{
				this.resource = resource;
			}
			
			public void Dispose()
			{
				bool last = true;
				LockWrapper wrapper;
				lock(globalLock) {
					wrapper = resourceLocks[resource];
					if(wrapper.counter == 1)
						resourceLocks.Remove(resource);
					else {
						wrapper.counter--;
						last = false;
					}
				}
				if(!last) {
					lock(wrapper.instanceLock) {
						wrapper.taken = false;
						Monitor.Pulse(wrapper.instanceLock);
					}
				}
			}
			
			public static Resource Lock(string resource)
			{
				LockWrapper wrapper = null;
				bool own = false;
				lock(globalLock) {
					if(resourceLocks.ContainsKey(resource)) {
						wrapper = resourceLocks[resource];
						wrapper.counter++;
					}
					else {
						wrapper = new LockWrapper();
						wrapper.instanceLock = new object();
						wrapper.counter = 1;
						wrapper.taken = true;
						own = true;
						resourceLocks[resource] = wrapper;
					}
				}
				if(!own) {
					lock(wrapper.instanceLock) {
						while(wrapper.taken) {
							Monitor.Wait(wrapper.instanceLock);
						}
					}
				}
				return new Resource(resource);
			}
		}
		
		public PreviewService(string basepath, StorageService storage, int width, int height, string temporaryDirectory, int cacheDuration, ILogger logger)
		{
			basePath = basepath;
			Storage = storage;
			this.width = width;
			this.height = height;
			this.temporaryDirectory = temporaryDirectory;
			this.cacheDuration = cacheDuration;
			this.logger = logger;

			if(!Directory.Exists(basepath))
				Directory.CreateDirectory(basepath);

			bool createNeeded = !File.Exists(basepath+"preview.db");

			dbcon = (IDbConnection)new SqliteConnection("URI=file:"+this.basePath+"preview.db");
			dbcon.Open();

			if(createNeeded) {
				// create the preview table
				using(IDbCommand dbcmd = dbcon.CreateCommand()) {
					dbcmd.CommandText = "CREATE TABLE preview (id INTEGER PRIMARY KEY AUTOINCREMENT, storage VARCHAR, file INTEGER, rev INTEGER, mimetype VARCHAR, fails INTEGER(1))";
					dbcmd.ExecuteNonQuery();
				}
			}
			// disable disk sync.
			using(IDbCommand dbcmd = dbcon.CreateCommand()) {
				dbcmd.CommandText = "PRAGMA synchronous=0";
				dbcmd.ExecuteNonQuery();
			}
			
			Storage.FileCreated += OnFileCreated;
			Storage.FileChanged += OnFileChanged;
			Storage.FileDeleted += OnFileDeleted;
		}
		
		void GetPreview(string storage, long file, out string previewMimetype, out string previewPath, out long rev, out bool fails)
		{
			string fileMimetype;
			string fileName;
			string filePath;
			Storage.GetDownloadFileInfo(storage, file, out fileMimetype, out fileName, out rev);
			filePath = Storage.GetFullPath(storage, file);
			previewMimetype = null;
			previewPath = basePath+"/"+storage+"/"+file;
			fails = true;

			using(Resource.Lock(storage+":"+file)) {				
				long oldRev = -1;

				lock(dbcon) {
					// check existing preview
					using(IDbCommand dbcmd = dbcon.CreateCommand()) {
						dbcmd.CommandText = "SELECT id,rev,mimetype,fails FROM preview WHERE storage=@storage AND file=@file";
						dbcmd.Parameters.Add(new SqliteParameter("storage", storage));
						dbcmd.Parameters.Add(new SqliteParameter("file", file));
						using(IDataReader reader = dbcmd.ExecuteReader()) {
							while(reader.Read()) {
								oldRev = reader.GetInt64(1);
								if(reader.IsDBNull(2))
									previewMimetype = null;
								else
									previewMimetype = reader.GetString(2);
								fails = (reader.GetInt64(3) != 0);
							}
						}
					}
				}
				// if preview is not up to date
				if(rev != oldRev) {
					fails = !BuildPreview(storage, file, filePath, fileMimetype, out previewMimetype, out previewPath);
					lock(dbcon) {					
						using(IDbTransaction transaction = dbcon.BeginTransaction()) {
							// delete from db if any
							using(IDbCommand dbcmd = dbcon.CreateCommand()) {
								dbcmd.CommandText = "DELETE FROM preview WHERE storage=@storage AND file=@file";
								dbcmd.Parameters.Add(new SqliteParameter("storage", storage));
								dbcmd.Parameters.Add(new SqliteParameter("file", file));
								dbcmd.Transaction = transaction;
								dbcmd.ExecuteNonQuery();
							}
							// insert into db
							using(IDbCommand dbcmd = dbcon.CreateCommand()) {
								if(fails) {
									dbcmd.CommandText = "INSERT INTO preview (storage,file,rev,mimetype,fails) VALUES (@storage,@file,@rev,null,1)";
									dbcmd.Parameters.Add(new SqliteParameter("storage", storage));
									dbcmd.Parameters.Add(new SqliteParameter("file", file));
									dbcmd.Parameters.Add(new SqliteParameter("rev", rev));
								}
								else {
									dbcmd.CommandText = "INSERT INTO preview (storage,file,rev,mimetype,fails) VALUES (@storage,@file,@rev,@mimetype,0)";
									dbcmd.Parameters.Add(new SqliteParameter("storage", storage));
									dbcmd.Parameters.Add(new SqliteParameter("file", file));
									dbcmd.Parameters.Add(new SqliteParameter("rev", rev));
									dbcmd.Parameters.Add(new SqliteParameter("mimetype", previewMimetype));
								}
								dbcmd.Transaction = transaction;
								dbcmd.ExecuteNonQuery();
							}
							transaction.Commit();
						}
					}
				}
			}
		}

		void DeletePreview(string storage, long file)
		{
			using(Resource.Lock(storage+":"+file)) {
				long oldId = -1;

				lock(dbcon) {
					using(IDbTransaction transaction = dbcon.BeginTransaction()) {
						// check existing preview
						using(IDbCommand dbcmd = dbcon.CreateCommand()) {
							dbcmd.CommandText = "SELECT id FROM preview WHERE storage=@storage AND file=@file";
							dbcmd.Parameters.Add(new SqliteParameter("storage", storage));
							dbcmd.Parameters.Add(new SqliteParameter("file", file));
							using(IDataReader reader = dbcmd.ExecuteReader()) {
								while(reader.Read()) {
									oldId = reader.GetInt64(0);
								}
							}
						}
						if(oldId != -1) {
							using(IDbCommand dbcmd = dbcon.CreateCommand()) {
								dbcmd.CommandText = "DELETE FROM preview WHERE storage=@storage AND file=@file";
								dbcmd.Parameters.Add(new SqliteParameter("storage", storage));
								dbcmd.Parameters.Add(new SqliteParameter("file", file));
								dbcmd.ExecuteNonQuery();
							}
						}
						transaction.Commit();
					}
				}
				if((oldId != -1) && File.Exists(basePath+"/"+storage+"/"+oldId))
					File.Delete(basePath+"/"+storage+"/"+oldId);
			}
		}

		
		bool BuildPreview(string storage, long file, string filepath, string mimetype, out string previewMimetype, out string previewPath)
		{
			IPreview preview = null;
			bool success = false;
			previewMimetype = null;
			previewPath = null;

			if(mimetype.StartsWith("image/") || mimetype.StartsWith("video/") || mimetype.StartsWith("audio/"))
				preview = new ImageVideoPreview(temporaryDirectory);
			else if(Pdf.PdfService.IsPdfCompatible(mimetype))
				preview = new PdfPreview(temporaryDirectory);
			else if(mimetype == "text/uri-list")
				preview = new UrlPreview(temporaryDirectory);
			else if(mimetype.StartsWith("text/plain"))
				preview = new TextPreview(temporaryDirectory);
			
			if(preview != null) {
				PreviewFormat format;
				string error;
				string previewFile = preview.Process(filepath, mimetype, width, height, out format, out error);
				if(previewFile != null) {
					if(!Directory.Exists(basePath+"/"+storage))
						Directory.CreateDirectory(basePath+"/"+storage);
					if(format == PreviewFormat.PNG)
						previewMimetype = "image/png";
					else
						previewMimetype = "image/jpeg";
					previewPath = basePath+"/"+storage+"/"+file;
					try {
						File.Move(previewFile, previewPath);
					}
					catch(IOException) {
						File.Replace(previewFile, previewPath, null);
					}
					success = true;
				}
				else {
					if(error != null)
						logger.Log(LogLevel.Error, error);
					success = false;
				}
			}
			return success;
		}
		
		void OnFileCreated(string storage, long file) 
		{
			string mimetype, filename;
			long rev;
			bool fails;
			GetPreview(storage, file, out mimetype, out filename, out rev, out fails);
		}
		
		void OnFileChanged(string storage, long file)
		{
			// rebuild the preview
			string mimetype, filename;
			long rev;
			bool fails;
			GetPreview(storage, file, out mimetype, out filename, out rev, out fails);
		}
		
		void OnFileDeleted(string storage, long file)
		{
			// delete the preview file
			DeletePreview(storage, file);
		}
				
		public override void ProcessRequest(HttpContext context)
		{	
			string[] parts = context.Request.Path.Split(new char[] { '/' }, System.StringSplitOptions.RemoveEmptyEntries);
			long file = 0;

			// GET /[storage_id]/[file_id] get file preview
			if((context.Request.Method == "GET") && (parts.Length == 2) && long.TryParse(parts[1], out file)) {
				// get the file
				string storage = parts[0];

				string previewMimetype;
				string previewPath;
				long rev;
				bool fails;
				GetPreview(storage, file, out previewMimetype, out previewPath, out rev, out fails);
				if(fails) {
					context.Response.StatusCode = 404;
					context.Response.Headers["cache-control"] = "max-age="+cacheDuration;
					context.Response.Content = new StringContent("File not found\n");
				}
				else {
					long argRev = -1;
					if(context.Request.QueryString.ContainsKey("rev"))
						argRev = Convert.ToInt64(context.Request.QueryString["rev"]);

					if(argRev != rev) {
						context.Response.StatusCode = 307;
						context.Response.Headers["location"] = "content?rev="+rev;
					}
					else {
						context.Response.Headers["content-type"] = previewMimetype;
						context.Response.SupportRanges = true;
						context.Response.StatusCode = 200;
						context.Response.Headers["cache-control"] = "max-age="+cacheDuration;
						context.Response.Content = new FileContent(previewPath);
					}
				}
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
